using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class IODescriptor
    {
        [Key("k")]
        public string Name { get; set; }
        [Key("v")]
        public int Value { get; set; }
    }
}
