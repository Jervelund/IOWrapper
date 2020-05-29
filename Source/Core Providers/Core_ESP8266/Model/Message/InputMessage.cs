using System.Collections.Generic;
using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class InputMessage : MessageBase
    {

        [Key("b")]
        public List<int> Buttons { get; set; }

        [Key("a")]
        public List<int> Axes { get; set; }

        [Key("d")]
        public List<int> Deltas { get; set; }

        [Key("e")]
        public List<int> Events { get; set; }
    }
}
