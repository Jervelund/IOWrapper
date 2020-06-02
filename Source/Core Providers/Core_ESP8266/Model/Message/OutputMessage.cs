using System.Collections.Generic;
using MessagePack;

namespace Core_ESP8266.Model.Message
{
    [MessagePackObject]
    public class OutputMessage : MessageBase
    {

        [Key("b")]
        public List<short> Buttons { get; set; }

        [Key("a")]
        public List<short> Axes { get; set; }

        [Key("d")]
        public List<short> Deltas { get; set; }

        [Key("e")]
        public List<short> Events { get; set; }

        Dictionary<int, int> _buttonLookup;
        Dictionary<int, int> _axesLookup;
        Dictionary<int, int> _deltasLookup;
        Dictionary<int, int> _eventsLookup;

        public OutputMessage()
        {
            MsgType = MessageType.Output;

            Buttons = new List<short>();
            Axes = new List<short>();
            Deltas = new List<short>();
            Events = new List<short>();

            _buttonLookup = new Dictionary<int, int>();
            _axesLookup = new Dictionary<int, int>();
            _deltasLookup = new Dictionary<int, int>();
            _eventsLookup = new Dictionary<int, int>();
        }

        public void configureMessage(DescriptorMessage descriptor)
        {
            descriptor.Output.Buttons.ForEach(io => AddButton(io.Value));
            descriptor.Output.Axes.ForEach(io => AddAxis(io.Value));
            descriptor.Output.Deltas.ForEach(io => AddDelta(io.Value));
            descriptor.Output.Events.ForEach(io => AddEvent(io.Value));
        }

        public void AddButton(int index)
        {
            AddIOData(Buttons, _buttonLookup, new IOData(index, 0));
        }

        public void AddAxis(int index)
        {
            AddIOData(Axes, _axesLookup, new IOData(index, 0));
        }

        public void AddDelta(int index)
        {
            AddIOData(Deltas, _deltasLookup, new IOData(index, 0));
        }

        public void AddEvent(int index)
        {
            AddIOData(Events, _eventsLookup, new IOData(index, 0));
        }
   
        private void AddIOData(List<short> list, Dictionary<int, int> dict, IOData data)
        {
            dict.Add(data.Index, list.Count);
            list.Add(data.Value);
        }

        public void SetButton(int index, short value)
        {
            Buttons[_buttonLookup[index]] = value;
        }

        public void SetAxis(int index, short value)
        {
            Axes[_axesLookup[index]] = value;
        }

        public void SetDelta(int index, short value)
        {
            Deltas[_deltasLookup[index]] += value;
        }

        public void SetEvent(int index, short value)
        {
            Events[_eventsLookup[index]] = value;
        }
    }
}
