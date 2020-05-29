using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class MessageBase
    {
        public enum MessageType
        {
            HeartbeatRequest = 0,
            HeartbeatResponse = 1,
            DescriptorRequest = 2,
            DescriptorResponse = 3,
            Output = 4,
            Subscribe = 5,
            Unsubscribe = 6,
            Input = 7
        }

        [Key("MsgType")]
        public MessageType Type { get; set; }

        [Key("hostname")]
        public string Hostname { get; set; }

    }
}
