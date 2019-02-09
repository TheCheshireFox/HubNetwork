#pragma once

#include <string>
#include <vector>

namespace hub_network
{
  enum message_type
  {
    direct,
    broadcast,
    random,
    round_robin,
  };

  struct message
  {
    message_type type = message_type::direct;
    std::string correlation_id = "";
    std::string sender = "";
    std::string reciever = "";
    std::vector<char> payload = {};
  };
}