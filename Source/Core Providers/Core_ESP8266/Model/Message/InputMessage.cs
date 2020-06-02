using System.Collections.Generic;
using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class InputMessage : MessageBase
    {
        [Key("b")]
        public List<short> Buttons { get; set; }

        [Key("a")]
        public List<short> Axes { get; set; }

        [Key("d")]
        public List<short> Deltas { get; set; }

        [Key("e")]
        public List<short> Events { get; set; }
    }
}
