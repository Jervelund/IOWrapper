using System.Collections.Generic;
using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class DescriptorMessage : MessageBase
    {
        public DescriptorMessage()
        {
            MsgType = MessageType.DescriptorResponse;
        }

        [Key("i")]
        public ReportDescriptor Input { get; set; }

        [Key("o")]
        public ReportDescriptor Output { get; set; }
    }
}
