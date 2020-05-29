using System.Collections.Generic;
using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class UnsubscribeMessage : MessageBase
    {
        public UnsubscribeMessage()
        {
            Type = MessageType.Unsubscribe;
        }
    }
}
