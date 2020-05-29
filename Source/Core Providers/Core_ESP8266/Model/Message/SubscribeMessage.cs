﻿using System.Collections.Generic;
using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class SubscribeMessage : MessageBase
    {
        public SubscribeMessage()
        {
            Type = MessageType.Subscribe;
        }
    }
}
