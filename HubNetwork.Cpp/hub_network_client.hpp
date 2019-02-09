#pragma once

#include <Windows.h>
#include <thread>
#include <string>
#include <codecvt>
#include <sstream>
#include <condition_variable>
#include <mutex>
#include <memory>
#include <atomic>
#include <future>

#include "message.hpp"
#include "internal_message.hpp"

#define ASIO_HEADER_ONLY
#include "asio.hpp"

#define HUB_NETWORK_THROW(x) throw std::runtime_error(hub_network::detail::make_string() << x)

namespace hub_network
{
  namespace detail
  {
    class futex
    {
      CRITICAL_SECTION _cs;
    public:
      futex()
      {
        InitializeCriticalSection(&_cs);
      }

      ~futex()
      {
        DeleteCriticalSection(&_cs);
      }

      void lock()
      {
        EnterCriticalSection(&_cs);
      }

      void unlock()
      {
        LeaveCriticalSection(&_cs);
      }
    };

    template<typename KEY_T, typename VALUE_T>
    class concurrent_map
    {
    private:
      std::recursive_mutex _sync;
      std::map<KEY_T, VALUE_T> _map;

    public:
      void add(const KEY_T& key, const VALUE_T& value)
      {
        std::unique_lock<std::recursive_mutex> lock(_sync);

        _map.insert(key, value);
      }

      bool try_take(const KEY_T& key, VALUE_T& value)
      {
        std::unique_lock<std::recursive_mutex> lock(_sync);

        auto it = _map.find(key);
        if (it == _map.end()) return false;

        value = it->second;
        _map.erase(it);

        return true;
      }
    };

    class reset_event
    {
    private:
      HANDLE _event;

    public:
      reset_event(bool manual_reset)
      {
        _event = CreateEvent(NULL, manual_reset, false, NULL);
      }

      void set()
      {
        SetEvent(_event);
      }

      void reset()
      {
        ResetEvent(_event);
      }

      bool wait_one(int timeout_ms = 0)
      {
        return WaitForSingleObject(_event, timeout_ms == 0 ? INFINITE : timeout_ms) == 0;
      }
    };

    class make_string
    {
    private:
      std::stringstream _ss;

    public:
      template<typename T>
      make_string& operator<<(const T& val)
      {
        _ss << val;
        return *this;
      }

      operator std::string() const
      {
        return _ss.str();
      }
    };
  }

  struct hub_network_client_opts
  {
    bool autoreconnect = true;
    bool heartbeat = true;
    int timeout_ms = 30000;
    int heartbeat_interval_ms = 10000;
  };

  class hub_network_client
  {
  private:
    ::asio::io_service _io;
    ::asio::io_service::work _work;
    std::atomic_bool _running;

    std::string _name;
    std::string _network;
    detail::futex _connected_sync;
    bool _connected;
    ::asio::ip::tcp::socket _sock;
    ::asio::ip::basic_resolver_results<::asio::ip::tcp> _endpoints;
    hub_network_client_opts _opts;

    std::thread _hb_th;
    std::thread _reconnect_th;
    std::thread _read_th;

    detail::reset_event _connection_restored = detail::reset_event(true);
    detail::reset_event _connection_lost = detail::reset_event(true);

    std::function<void(const message&)> _on_message;

    std::mutex _write_sync;

    detail::reset_event _new_message = detail::reset_event(false);

    detail::concurrent_map<std::string, std::shared_ptr<std::promise<bool>>> _acks;
    detail::concurrent_map<std::string, std::shared_ptr<std::promise<message>>> _responses;

  private:
    void read_bytes(std::vector<char> data)
    {
      auto count = data.size();

      while (count > 0)
      {
        auto sz = ::asio::read(_sock, ::asio::buffer(&data[data.size() - count], count));
        count -= sz;
      }
    }

    std::string create_guid()
    {
      GUID guid;
      UuidCreate(&guid);

      char* guidStr;
      UuidToString(&guid, (RPC_CSTR*)guidStr);

      std::string result(guidStr);

      RpcStringFree((RPC_CSTR*)guidStr);

      return result;
    }

    void emit_disconnect_and_wait()
    {
      {
        std::lock_guard<detail::futex> lock(_connected_sync);

        if (!_opts.autoreconnect)
        {
          throw std::runtime_error("No connection");
        }

        _connection_restored.reset();
        _connection_lost.set();
      }

      _connection_restored.wait_one();
    }

    void wait_for_connection()
    {
      {
        std::lock_guard<detail::futex> lock(_connected_sync);
        if (_connected) return;
      }

      _connection_restored.wait_one();
    }

    std::unique_ptr<std::future<bool>> register_ack(const std::string& id)
    {
      auto p = std::make_shared<std::promise<bool>>();
      _acks.add(id, p);
      return std::make_unique<std::future<bool>>(p->get_future());
    }

    std::unique_ptr<std::future<message>> register_response(const std::string& id)
    {
      auto p = std::make_shared<std::promise<message>>();
      _responses.add(id, p);
      return std::make_unique<std::future<message>>(p->get_future());
    }

    void heartbeat_thread()
    {
      detail::internal_message hb;
      hb.type = detail::internal_message_type::init;

      while (_running)
      {
        try
        {
          wait_for_connection();
          send_internal(hb);
        }
        catch (const std::exception&) {}

        auto dt = std::chrono::system_clock::now();

        if (!_new_message.wait_one(_opts.heartbeat_interval_ms))
        {
          try
          {
            emit_disconnect_and_wait();
          }
          catch (const std::exception&)
          {
            return;
          }

          continue;
        }

        auto delta = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::system_clock::now() - dt).count();

        if (delta > 0)
        {
          std::this_thread::sleep_for(std::chrono::microseconds(delta));
        }
      }
    }

    void reconnect_thread()
    {
      while (_running)
      {
        _connection_lost.wait_one();

        while (true)
        {
          try
          {
            connect();
            {
              std::lock_guard<detail::futex> lock(_connected_sync);
              _connected = true;
            }

            _connection_lost.reset();
            _connection_restored.set();
            break;
          }
          catch (const std::exception& e)
          {
            std::this_thread::sleep_for(std::chrono::milliseconds(1000));
          }
        }
      }
    }

    void handshake()
    {
      auto id = create_guid();
      detail::internal_message msg;
      msg.correlation_id = id;
      msg.payload = std::vector<char>(_network.begin(), _network.end());
      msg.reciever = _name;
      msg.sender = _name;
      msg.type = detail::internal_message_type::init;

      send_internal(msg, false);

      auto resp = read_internal(false);

      if (resp.correlation_id != id)
      {
        throw std::runtime_error("Error occured during handshake process");
      }
    }

    void read_thread()
    {
      while (_running)
      {
        try
        {
          auto internal_msg = read_internal();

          if (internal_msg.type == detail::internal_message_type::heartbeat)
          {
            continue;
          }

          std::shared_ptr<std::promise<bool>> _ack_promise;
          if (_acks.try_take(internal_msg.correlation_id, _ack_promise))
          {
            if (internal_msg.type == detail::internal_message_type::error)
            {
              _ack_promise->set_exception(std::make_exception_ptr(std::runtime_error(internal_msg.payload.data())));
            }
            else
            {
              _ack_promise->set_value(true);
            }

            continue;
          }

          message msg;
          msg.correlation_id = internal_msg.correlation_id;
          msg.payload = internal_msg.payload;
          msg.reciever = internal_msg.reciever;
          msg.sender = internal_msg.sender;
          msg.type = (message_type)internal_msg.type;

          std::shared_ptr<std::promise<message>> _response_promise;
          if (_responses.try_take(internal_msg.correlation_id, _response_promise))
          {
            _response_promise->set_value(msg);
            continue;
          }

          if (_on_message)
          {
            _on_message(msg);
          }

        }
        catch (const std::exception&)
        {
          emit_disconnect_and_wait();
        }
      }
    }

    detail::internal_message read_internal(bool wait_for_connection_flag = true)
    {
      if (wait_for_connection_flag) wait_for_connection();

      std::vector<char> _size_buf = { 0, 0, 0, 0 };

      read_bytes(_size_buf);

      std::vector<char> bytes;
      bytes.resize(*(int*)_size_buf.data());
      read_bytes(bytes);

      return detail::internal_message(bytes);
    }

    void send_internal(const detail::internal_message& msg, bool throw_if_not_connected = true)
    {
      if (throw_if_not_connected)
      {
        std::lock_guard<detail::futex> lock(_connected_sync);
        if (!_connected) throw std::runtime_error("No connection");
      }

      auto msg_bytes = msg.to_bytes();

      std::lock_guard<std::mutex> lock(_write_sync);

      ::asio::error_code ec;
      ::asio::write(_sock, asio::buffer(msg_bytes), ec);

      if (ec)
      {
        HUB_NETWORK_THROW("Unable to send message. Error code: " << ec);
      }
    }

  public:
    hub_network_client(const std::string& name, const std::string& host, int port, const hub_network_client_opts& opts)
      : hub_network_client(name, "", host, port, opts)
    {

    }

    hub_network_client(const std::string& name, const std::string& network, const std::string& host, int port, const hub_network_client_opts& opts)
      : _io(1)
      , _work(_io)
      , _sock(_io)
    {
      ::asio::ip::basic_resolver<::asio::ip::tcp> resolver(_io);
      _endpoints = resolver.resolve(host, std::to_string(port));

      _name = name;
      _network = network;
      _opts = opts;
    }

    void connect()
    {
      {
        std::lock_guard<detail::futex> lock(_connected_sync);
        if (_connected) return;
      }

      ::asio::error_code ec;
      for (auto& ep : _endpoints)
      {
        _sock.connect(ep.endpoint(), ec);

        if (!ec) break;
      }

      if (ec)
      {
        HUB_NETWORK_THROW("Unable to connect to host. Error code: " << ec);
      }

      handshake();

      {
        std::lock_guard<detail::futex> lock(_connected_sync);
        _connected = true;
      }

      if (_opts.autoreconnect) _reconnect_th = std::thread([this]() { reconnect_thread(); });
      if (_opts.heartbeat) _hb_th = std::thread([this]() { heartbeat_thread(); });
      _read_th = std::thread([this]() { read_thread(); });
    }

    void send_message(const message& msg, bool noack = false)
    {
      auto id = msg.correlation_id.empty() ? create_guid() : msg.correlation_id;

      detail::internal_message internal_msg;
      internal_msg.correlation_id = id;
      internal_msg.no_ack = noack;
      internal_msg.payload = msg.payload;
      internal_msg.reciever = msg.reciever;
      internal_msg.sender = msg.sender;
      internal_msg.type = (detail::internal_message_type)msg.type;

      if (noack)
      {
        send_internal(internal_msg);
      }
      else
      {
        auto f = register_ack(id);
        send_internal(internal_msg);
        if (f->wait_for(std::chrono::milliseconds(_opts.timeout_ms)) == std::future_status::timeout)
        {
          throw std::runtime_error("Timeout");
        }
      }
    }

    message send_direct_wait_response(const message& msg)
    {
      auto id = msg.correlation_id.empty() ? create_guid() : msg.correlation_id;

      detail::internal_message internal_msg;
      internal_msg.correlation_id = id;
      internal_msg.no_ack = false;
      internal_msg.payload = msg.payload;
      internal_msg.reciever = msg.reciever;
      internal_msg.sender = msg.sender;
      internal_msg.type = (detail::internal_message_type)msg.type;

      auto f = register_ack(id);
      auto resp = register_response(id);
      send_internal(internal_msg);

      if (f->wait_for(std::chrono::milliseconds(_opts.timeout_ms)) == std::future_status::timeout)
      {
        throw std::runtime_error("Timeout");
      }

      if (resp->wait_for(std::chrono::milliseconds(_opts.timeout_ms)) == std::future_status::timeout)
      {
        throw std::runtime_error("Timeout");
      }

      return resp->get();
    }
  };
}