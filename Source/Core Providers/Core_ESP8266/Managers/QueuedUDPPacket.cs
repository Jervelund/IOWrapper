using Core_ESP8266.Model;

namespace Core_ESP8266
{
    internal class QueuedUDPPacket
    {
        public readonly ServiceAgent ServiceAgent;
        public readonly byte[] Message;
        public QueuedUDPPacket(ServiceAgent serviceAgent, byte[] message)
        {
            Message = message;
            ServiceAgent = serviceAgent;
        }
    }
}