#pragma once

#include <vector>
#include <string>
#include <sstream>

namespace hub_network
{
  namespace detail
  {
    class message_stream
    {
    private:
      std::vector<char> _memory;
      std::size_t _r_pos = 0;
      std::size_t _w_pos = 0;

    private:
      void read(char* val, std::size_t sz)
      {
        std::memcpy(val, &_memory[_r_pos], sz);
        _r_pos += sz;
      }

      void write(const char* val, std::size_t sz)
      {
        std::memcpy(&_memory[_w_pos], val, sz);
        _w_pos += sz;
      }

    public:
      message_stream(std::size_t size)
      {
        _memory.resize(size + 4);
        _w_pos = 4;
      }

      message_stream(const std::vector<char> data)
      {
        _memory = data;
      }

      message_stream& operator>>(bool& val)
      {
        char bool_byte;
        read(&bool_byte, 1);
        val = bool_byte == 1;

        return *this;
      }

      message_stream& operator>>(int& val)
      {
        char int_bytes[4];
        read(int_bytes, 4);
        val = *(int*)(int_bytes);

        return *this;
      }

      message_stream& operator>>(std::string& val)
      {
        std::vector<char> bytes;
        *this >> bytes;
        val = std::string(bytes.data(), bytes.size());
      }

      message_stream& operator>>(std::vector<char>& val)
      {
        int sz;
        *this >> sz;

        val.resize(sz);
        read(val.data(), sz);

        return *this;
      }

      message_stream& operator<<(bool val)
      {
        char b_val = val ? 1 : 0;
        write(&b_val, 1);

        return *this;
      }

      message_stream& operator<<(int val)
      {
        write((char*)&val, 4);

        return *this;
      }

      message_stream& operator<<(const std::string& val)
      {
        *this << (int)val.size() + 1;
        write(val.c_str(), val.size());
        *this << false; // zero end

        return *this;
      }

      message_stream& operator<<(const std::vector<char>& val)
      {
        *this << (int)val.size();
        write(val.data(), val.size());

        return *this;
      }

      const std::vector<char>& to_bytes()
      {
        auto old_w_pos = _w_pos;
        _w_pos = 0;

        *this << (int)_memory.size() - 4;

        _w_pos = old_w_pos;

        return _memory;
      }
    };
  }
}