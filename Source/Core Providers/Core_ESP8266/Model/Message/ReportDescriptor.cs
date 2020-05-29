using System.Collections.Generic;
using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class ReportDescriptor : MessageBase
    {
        public ReportDescriptor()
        {

        }

        [Key("b")]
        public List<IODescriptor> Buttons { get; set; }

        [Key("a")]
        public List<IODescriptor> Axes { get; set; }

        [Key("d")]
        public List<IODescriptor> Deltas { get; set; }

        [Key("e")]
        public List<IODescriptor> Events { get; set; }
    }
}
