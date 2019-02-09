#pragma once

#include <string>
#include <vector>

#include "message_stream.hpp"

namespace hub_network
{
  namespace detail
  {
    enum internal_message_type
    {
      direct,
      broadcast,
      random,
      roundRobin,
      init,
      ack,
      heartbeat,
      error
    };

    struct internal_message
    {
      internal_message_type type = internal_message_type::direct;
      std::string correlation_id = "";
      std::string sender = "";
      std::string reciever = "";
      std::vector<char> payload = {};
      bool no_ack = false;

      internal_message()
      {

      }

      internal_message(const std::vector<char>& bytes)
      {
        message_stream ms(bytes);
        int int_type;

        ms >> int_type
          >> correlation_id
          >> sender
          >> reciever
          >> no_ack
          >> payload;

        type = (internal_message_type)int_type;
      }

      std::vector<char> to_bytes() const
      {
        message_stream ms(
          4
          + 4 + correlation_id.size() + 1 // zero byte
          + 4 + sender.size() + 1 // zero byte
          + 4 + reciever.size() + 1 // zero byte
          + 4 + payload.size()
          + 1);

        ms << (int)type
          << correlation_id
          << sender
          << reciever
          << no_ack
          << payload;

        return ms.to_bytes();
      }
    };
  }

}