using Core_ESP8266.Model;
using Core_ESP8266.Model.Message;
using HidWizards.IOWrapper.DataTransferObjects;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Tmds.MDns;

namespace Core_ESP8266
{
    class NetworkManager
    {
        private const string ServiceType = "_ucr._udp";

        private static Dictionary<string, DeviceInfo> DiscoveredAgents { get; set; }
        private static UdpClient udpClient = new UdpClient(8090);
        private static IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, 0); // Receive from any endpoints
        private readonly ServiceBrowser serviceBrowser;

        public NetworkManager()
        {
            DiscoveredAgents = new Dictionary<string, DeviceInfo>();

            // Start UDP client
            udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);

            // Listen for mDNS services
            serviceBrowser = new ServiceBrowser();
            serviceBrowser.ServiceAdded += OnServiceAdded;
            serviceBrowser.ServiceRemoved += OnServiceRemoved;
            serviceBrowser.ServiceChanged += OnServiceChanged; // Would it benefit us to listen to these events?
            Debug.WriteLine($"IOWrapper | ESP8266| Browsing for type: {ServiceType}");
            serviceBrowser.StartBrowse(ServiceType);
        }

        private void OnServiceChanged(object sender, ServiceAnnouncementEventArgs e)
        {
            // Not handled
            Debug.WriteLine($"IOWrapper| ESP8266| OnServiceChanged: {e}");
        }

        private void OnServiceRemoved(object sender, ServiceAnnouncementEventArgs e)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| OnServiceRemoved: {e}");
            DiscoveredAgents.Remove(e.Announcement.Hostname);
        }

        private void OnServiceAdded(object sender, ServiceAnnouncementEventArgs e)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| OnServiceAdded: {e}");
            var deviceInfo = new DeviceInfo()
            {
                ServiceAgent = new ServiceAgent()
                {
                    Hostname = e.Announcement.Hostname,
                    Ip = e.Announcement.Addresses[0],
                    Port = e.Announcement.Port,
                    LastReceived = DateTime.Now,
                    LastSent = DateTime.Now
                }
            };

            DiscoveredAgents.Add(e.Announcement.Hostname, deviceInfo);
            SendBaseMessage(deviceInfo.ServiceAgent, MessageBase.MessageType.DescriptorRequest);
        }

        internal void setOutput(OutputSubscriptionRequest subReq, BindingDescriptor bindingDescriptor, int state)
        {
            switch (EspUtility.GetBindingCategory(bindingDescriptor)) {
                case BindingCategory.Signed:
                case BindingCategory.Unsigned:
                    LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle).OutputMessage.Axes[bindingDescriptor.Index].Value = (short)state;
                    break;
                case BindingCategory.Momentary:
                    LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle).OutputMessage.Buttons[bindingDescriptor.Index].Value = (short)state;
                    break;
                case BindingCategory.Event:
                    LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle).OutputMessage.Events[bindingDescriptor.Index].Value = (short)state;
                    break;
                case BindingCategory.Delta:
                    LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle).OutputMessage.Deltas[bindingDescriptor.Index].Value = (short)state;
                    break;
                default:
                        throw new NotImplementedException();
                break;
            }
        }

        public static DeviceInfo LookupDeviceInfo(string name)
        {
            if (!DiscoveredAgents.ContainsKey(name)) return null;
            return DiscoveredAgents[name];
        }

        public void RefreshDevices() {
            foreach (var device in DiscoveredAgents)
            {
                // Check if we got a device report back from this agent
                if (device.Value.DeviceReportInput == null || device.Value.DeviceReportOutput == null)
                {
                    SendBaseMessage(device.Value.ServiceAgent, MessageBase.MessageType.DescriptorRequest);
                }
                // Check if we should attempt to resubscribe to a device (if no data was received in 10 seconds)
                if (DateTime.Now.AddSeconds(-10) >= device.Value.ServiceAgent.LastInputReceived)
                {
                    SendBaseMessage(device.Value.ServiceAgent, MessageBase.MessageType.Subscribe);
                }
            }
        }

        private void SendBaseMessage(ServiceAgent sa, MessageBase.MessageType messageType) {
            Debug.WriteLine($"IOWrapper| ESP8266| SendBaseMessage({sa.FullName}, {messageType})");
            var requestDescriptorMessage = new MessageBase();
            requestDescriptorMessage.Type = MessageBase.MessageType.DescriptorRequest;
            SendUdpPacket(sa, requestDescriptorMessage);
        }

        private void SendUdpPacket(ServiceAgent serviceAgent, object messageBase)
        {
            Debug.WriteLine("IOWrapper| ESP8266| SendUDPPacket()");
            var message = MessagePackSerializer.Serialize(messageBase);
            IPEndPoint ipEndpoint = new IPEndPoint(serviceAgent.Ip, serviceAgent.Port);
            udpClient.Send(message, message.Length, ipEndpoint);
            Debug.WriteLine($"IOWrapper| ESP8266| Sent UDP to {serviceAgent.FullName}: {MessagePackSerializer.ConvertToJson(message)}");
            serviceAgent.LastSent = DateTime.Now;
        }

        internal void Subscribe(InputSubscriptionRequest subReq)
        {
            var device = LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            device.InputSubscription = subReq;
            SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.Subscribe);
        }

        internal void Unsubscribe(InputSubscriptionRequest subReq)
        {
            var device = LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            device.InputSubscription = subReq;
            SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.Unsubscribe);
        }

        public static void ReceiveCallback(IAsyncResult ar)
        {
            // Move received packet to receive buffer
            byte[] receiveBytes = udpClient.EndReceive(ar, ref ipEndPoint);
            // Start async receive again
            udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);

            Debug.WriteLine($"IOWrapper| ESP8266| Received: {MessagePackSerializer.ConvertToJson(receiveBytes)}");

            MessageBase msg = MessagePackSerializer.Deserialize<MessageBase>(receiveBytes);
            Debug.WriteLine($"IOWrapper| ESP8266| Msg Type: {msg.Type}");
            DeviceInfo device = LookupDeviceInfo(msg.Hostname);
            device.ServiceAgent.LastReceived = DateTime.Now;
            switch (msg.Type)
            {
                case MessageBase.MessageType.DescriptorResponse:
                    DescriptorMessage descriptorMessage = MessagePackSerializer.Deserialize<DescriptorMessage>(receiveBytes);
                    HandleReceivedDeviceDescriptorMessage(device, descriptorMessage);
                    break;
                case MessageBase.MessageType.Input:
                    InputMessage inputMessage = MessagePackSerializer.Deserialize<InputMessage>(receiveBytes);
                    HandleReceivedDeviceInputData(device, inputMessage);
                    break;
                default:
                    Debug.WriteLine("IOWrapper| ESP8266| Msg Type: Unknown message type");
                    break;
            }
        }

        public List<DeviceReport> getInputDeviceReports()
        {
            return DiscoveredAgents.Select(d => d.Value.DeviceReportInput).ToList();
/*            List<DeviceReport> reports = new List<DeviceReport>();
            foreach (var device in DiscoveredAgents)
            {
                reports.Add(device.Value.DeviceReportInput);
            }
            return reports;*/
        }

        public List<DeviceReport> getOutputDeviceReports()
        {
            return DiscoveredAgents.Select(d => d.Value.DeviceReportOutput).ToList();
/*            List<DeviceReport> reports = new List<DeviceReport>();
            foreach (var device in DiscoveredAgents)
            {
                reports.Add(device.Value.DeviceReportInput);
            }
            return reports;*/
        }

        private static void HandleReceivedDeviceDescriptorMessage(DeviceInfo device, DescriptorMessage descriptorMessage)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| HandleReceivedDeviceDescriptorMessage()");
            DeviceDescriptor descriptor = new DeviceDescriptor()
            {
                DeviceHandle = device.ServiceAgent.Hostname,
                DeviceInstance = 0 // Unused
            };

            var inputDeviceReportNodes = new List<DeviceReportNode>();
            if (descriptorMessage.Input.Buttons.Count > 0) inputDeviceReportNodes.Add(BuildDeviceReportNodes("Buttons", BindingCategory.Momentary, descriptorMessage.Input.Buttons));
            if (descriptorMessage.Input.Axes.Count > 0) inputDeviceReportNodes.Add(BuildDeviceReportNodes("Axes", BindingCategory.Signed, descriptorMessage.Input.Axes));
            if (descriptorMessage.Input.Deltas.Count > 0) inputDeviceReportNodes.Add(BuildDeviceReportNodes("Deltas", BindingCategory.Delta, descriptorMessage.Input.Deltas));
            if (descriptorMessage.Input.Events.Count > 0) inputDeviceReportNodes.Add(BuildDeviceReportNodes("Events", BindingCategory.Event, descriptorMessage.Input.Events));
            DeviceReport deviceReportInput = new DeviceReport()
            {
                DeviceName = device.ServiceAgent.Hostname,
                DeviceDescriptor = descriptor,
                Nodes = inputDeviceReportNodes
            };
            device.DeviceReportInput = device.DeviceReportOutput = (inputDeviceReportNodes.Count() > 0 ? deviceReportInput : null); ;

            var outputDeviceReportNodes = new List<DeviceReportNode>();
            if (descriptorMessage.Output.Buttons.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Buttons", BindingCategory.Momentary, descriptorMessage.Output.Buttons));
            if (descriptorMessage.Output.Axes.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Axes", BindingCategory.Signed, descriptorMessage.Output.Axes));
            if (descriptorMessage.Output.Deltas.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Deltas", BindingCategory.Delta, descriptorMessage.Output.Deltas));
            if (descriptorMessage.Output.Events.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Events", BindingCategory.Event, descriptorMessage.Output.Events));
            DeviceReport deviceReportOutput = new DeviceReport()
            {
                DeviceName = device.ServiceAgent.Hostname,
                DeviceDescriptor = descriptor,
                Nodes = outputDeviceReportNodes
            };
            device.DeviceReportOutput = (outputDeviceReportNodes.Count() > 0?deviceReportOutput:null);
        }

        private static void HandleReceivedDeviceInputData(DeviceInfo device, InputMessage inputMessage) {
            // TODO: Handle inputs
            Debug.WriteLine($"IOWrapper| ESP8266| ReceivedInput: {inputMessage.Buttons.Count} buttons");
            if (device.InputSubscription == null)
                return;
            device.ServiceAgent.LastInputReceived = DateTime.Now;
        }

        /*
        private bool BuildDeviceReport(ServiceAgent serviceAgent, out DeviceReport inputDeviceReport, out DeviceReport outputDeviceReport, out DescriptorMessage requestDescriptor)
        {
            requestDescriptor = _udpManager.RequestDescriptor(serviceAgent);

            if (requestDescriptor == null || !MessageBase.MessageType.DescriptorResponse.Equals(requestDescriptor.Type))
            {
                inputDeviceReport = null;
                outputDeviceReport = null;
                return false;
            }

            var inputDeviceReportNodes = new List<DeviceReportNode>();
            if (requestDescriptor.Input.Buttons.Count > 0) inputDeviceReportNodes.Add(BuildDeviceReportNodes("Buttons", BindingCategory.Momentary, requestDescriptor.Input.Buttons));
            if (requestDescriptor.Input.Axes.Count > 0) inputDeviceReportNodes.Add(BuildDeviceReportNodes("Axes", BindingCategory.Signed, requestDescriptor.Input.Axes));
            if (requestDescriptor.Input.Deltas.Count > 0) inputDeviceReportNodes.Add(BuildDeviceReportNodes("Deltas", BindingCategory.Delta, requestDescriptor.Input.Deltas));
            if (requestDescriptor.Input.Events.Count > 0) inputDeviceReportNodes.Add(BuildDeviceReportNodes("Events", BindingCategory.Event, requestDescriptor.Input.Events));

            var outputDeviceReportNodes = new List<DeviceReportNode>();
            if (requestDescriptor.Output.Buttons.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Buttons", BindingCategory.Momentary, requestDescriptor.Output.Buttons));
            if (requestDescriptor.Output.Axes.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Axes", BindingCategory.Signed, requestDescriptor.Output.Axes));
            if (requestDescriptor.Output.Deltas.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Deltas", BindingCategory.Delta, requestDescriptor.Output.Deltas));
            if (requestDescriptor.Output.Events.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Events", BindingCategory.Event, requestDescriptor.Output.Events));

            var descriptor = new DeviceDescriptor()
            {
                DeviceHandle = serviceAgent.Hostname,
                DeviceInstance = 0 // Unused
            };

            inputDeviceReport = new DeviceReport()
            {
                DeviceName = serviceAgent.Hostname,
                DeviceDescriptor = descriptor,
                Nodes = inputDeviceReportNodes
            };

            outputDeviceReport = new DeviceReport()
            {
                DeviceName = serviceAgent.Hostname,
                DeviceDescriptor = descriptor,
                Nodes = outputDeviceReportNodes
            };

            return true;
        }
        */
        private static DeviceReportNode BuildDeviceReportNodes(string name, BindingCategory bindingCategory, List<IODescriptor> descriptors)
        {
            var bindings = new List<BindingReport>();
            foreach (var ioDescriptor in descriptors)
            {
                bindings.Add(new BindingReport()
                {
                    Title = ioDescriptor.Name,
                    Category = bindingCategory,
                    Path = $"{name} > {ioDescriptor.Name}",
                    Blockable = false,
                    BindingDescriptor = EspUtility.GetBindingDescriptor(bindingCategory, ioDescriptor.Value)
                });
            }

            return new DeviceReportNode()
            {
                Title = name,
                Bindings = bindings
            };
        }

        public void Dispose() {
            // TODO: Let agents know we are shutting down
            udpClient?.Dispose();
        }
    }
}
