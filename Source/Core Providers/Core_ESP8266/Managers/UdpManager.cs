using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Core_ESP8266.Model;
using Core_ESP8266.Model.Message;
using MessagePack;

namespace Core_ESP8266.Managers
{
    public class UdpManager : IDisposable
    {
        public struct UdpState
        {
            public UdpClient udpClient;
            public IPEndPoint ipEndpoint;
        }

        private static UdpClient _udpClient;
        private static IPEndPoint _ipEndpoint;

        public UdpManager()
        {
            Debug.WriteLine("IOWrapper| ESP8266| UdpManager()");

            // Receive from any endpoints
            _ipEndpoint = new IPEndPoint(IPAddress.Any, 0);
            // Receive from any listen on port 8090 (any reason for this port?)
            _udpClient = new UdpClient(8090);
            _udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);
        }

        public void SendDataMessage(ServiceAgent serviceAgent, OutputMessage dataMessage)
        {
            // TODO Should each device have its own UDPClient?
            SendUdpPacket(serviceAgent, dataMessage);
        }
        /*
        public static DescriptorMessage getLastDescriptor() {
            return _lastDescriptorMessage;
        }
        public static DescriptorMessage getLastInputMessage()
        {
            return _lastDescriptorMessage;
        }
        */
        public static void ReceiveCallback(IAsyncResult ar)
        {   
            // Move received packet to receive buffer
            byte[] receiveBytes = _udpClient.EndReceive(ar, ref _ipEndpoint);
            // Start async receive again
            _udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);
            string receiveString = Encoding.ASCII.GetString(receiveBytes);
            
            Debug.WriteLine($"IOWrapper| ESP8266| Received: {receiveString}");

            MessageBase msg = MessagePackSerializer.Deserialize<MessageBase>(receiveBytes);
            Debug.WriteLine($"IOWrapper| ESP8266| Msg Type: {msg.Type}");
            switch (msg.Type) {
                case MessageBase.MessageType.DescriptorResponse:
                    DescriptorMessage _lastDescriptorMessage = MessagePackSerializer.Deserialize<DescriptorMessage>(receiveBytes);
                    //Debug.WriteLine($"IOWrapper| ESP8266| Received: {descriptorMessage.Input.Buttons.Count}");
                    break;
                case MessageBase.MessageType.Input:
                    InputMessage _lastInputMessage = MessagePackSerializer.Deserialize<InputMessage>(receiveBytes);
                    Debug.WriteLine($"IOWrapper| ESP8266| Received: {_lastInputMessage.Buttons.Count}");
                    break;
            }
           
        }

        public DescriptorMessage RequestDescriptor(ServiceAgent serviceAgent)
        {
            Debug.WriteLine("IOWrapper| ESP8266| RequestDescriptor()");
            var requestDescriptorMessage = new MessageBase();
            requestDescriptorMessage.Type = MessageBase.MessageType.DescriptorRequest;
            SendUdpPacket(serviceAgent, requestDescriptorMessage);

            return null;
            // Don't use blocking receive
            if (!ReceiveUdpPacket(serviceAgent, out var receiveBuffer)) return null;
            return MessagePackSerializer.Deserialize<DescriptorMessage>(receiveBuffer);
        }

        private void SendUdpPacket(ServiceAgent serviceAgent, object messageBase)
        {
            Debug.WriteLine("IOWrapper| ESP8266| SendUDPPacket()");
            var message = MessagePackSerializer.Serialize(messageBase);
            IPEndPoint ipEndpoint = new IPEndPoint(serviceAgent.Ip, serviceAgent.Port);
            _udpClient.Send(message, message.Length, ipEndpoint);
            Debug.WriteLine($"IOWrapper| ESP8266| Sent UDP to {serviceAgent.FullName}: {MessagePackSerializer.ConvertToJson(message)}");
        }

        private bool ReceiveUdpPacket(ServiceAgent serviceAgent, out byte[] receiveBuffer)
        {
            Debug.WriteLine("IOWrapper| ESP8266| ReceiveUDPPacket()");
            var ipEndPoint = new IPEndPoint(serviceAgent.Ip, serviceAgent.Port);
            try
            {
                receiveBuffer = _udpClient.Receive(ref ipEndPoint);
                var responseString = Encoding.Default.GetString(receiveBuffer);
                Debug.WriteLine($"IOWrapper| ESP8266| Received UDP: {responseString}");
                return true;
            }
            catch (SocketException e)
            {
                receiveBuffer = null;
                return false;
            }
        }

        public void Dispose()
        {
            _udpClient?.Dispose();
        }
    }
}
