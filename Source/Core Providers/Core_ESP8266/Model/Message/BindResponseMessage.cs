using System.Collections.Generic;
using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class BindResponseMessage : MessageBase
    {
        public BindResponseMessage()
        {
            MsgType = MessageType.BindResponse;
        }

        [Key("index")]
        public int Index { get; set; }

        [Key("category")]
        public string Category { get; set; }

        [Key("value")]
        public short Value { get; set; }

    }
}
