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
using System.Timers;
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
        private static Dictionary<string, Timer> outputTimers { get; set; }
        private static Dictionary<string, Timer> inputTimers { get; set; }

        public NetworkManager()
        {
            DiscoveredAgents = new Dictionary<string, DeviceInfo>();
            outputTimers = new Dictionary<string, Timer>();

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
                    Port = e.Announcement.Port
                }
            };

            DiscoveredAgents.Add(e.Announcement.Hostname, deviceInfo);
            SendBaseMessage(deviceInfo.ServiceAgent, MessageBase.MessageType.DescriptorRequest);
        }

        internal bool SubscribeOutput(OutputSubscriptionRequest subReq)
        {
            // We can only send data to devices we know, so don't allow users to subscribe if the device is not able to respond with descriptors
            if (LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle).OutputMessage == null)
                return false;
            var timer = new Timer() {
                AutoReset = true,
                Enabled = true,
                Interval = 1000
            };
            timer.Elapsed += (sender, e) => onOutputTimer(sender, e, subReq.DeviceDescriptor.DeviceHandle);
            timer.Start();
            outputTimers.Add(subReq.DeviceDescriptor.DeviceHandle, timer);
            return true;
        }

        internal bool UnsubscribeOutput(OutputSubscriptionRequest subReq)
        {
            var deviceHandle = subReq.DeviceDescriptor.DeviceHandle;
            // Check if we have any timers to clear
            if (outputTimers.ContainsKey(deviceHandle))
            {
                // Clear timer and remove from list
                outputTimers[deviceHandle].Stop();
                outputTimers.Remove(deviceHandle);
            }
            return true;
        }

        internal void setOutput(OutputSubscriptionRequest subReq, BindingDescriptor bindingDescriptor, int state)
        {
            var device = LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            switch (EspUtility.GetBindingCategory(bindingDescriptor))
            {
                case BindingCategory.Signed:
                case BindingCategory.Unsigned:
                    device.OutputMessage.Axes[bindingDescriptor.Index] = (short)state;
                    break;
                case BindingCategory.Momentary:
                    device.OutputMessage.Buttons[bindingDescriptor.Index] = (short)state;
                    break;
                case BindingCategory.Event:
                    device.OutputMessage.Events[bindingDescriptor.Index] = (short)state;
                    break;
                case BindingCategory.Delta:
                    device.OutputMessage.Deltas[bindingDescriptor.Index] = (short)state;
                    break;
                default:
                    throw new NotImplementedException();
                    break;
            }
            // If we haven't updated the device within the FLOOD RATE, transmit now
            transmitOutput(device);
        }

        private void onOutputTimer(object source, ElapsedEventArgs e, String deviceHandle)
        {
            var device = LookupDeviceInfo(deviceHandle);
            if (device == null) return; // Device must have been removed
            transmitOutput(device);
        }

        private void transmitOutput(DeviceInfo device) {
            // Don't allow retransmission if we sent a packet in the last 10 miliseconds, as this will screw the ESP's receive buffer
            if (device.ServiceAgent.LastSent > DateTime.Now.AddMilliseconds(-10))
            {
                Debug.WriteLine($"IOWrapper| ESP8266| Transmit skipped due to antiflooding ({device.ServiceAgent.FullName})");
                return;
            }
            SendUdpPacket(device.ServiceAgent, device.OutputMessage);

            // Clear all deltas and events, once they have been transmitted
            device.OutputMessage.Deltas.ForEach(io => io = 0);
            device.OutputMessage.Events.ForEach(io => io = 0);
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
                    // Request descriptor report if it is missing
                    SendBaseMessage(device.Value.ServiceAgent, MessageBase.MessageType.DescriptorRequest);
                }
                // Check if we should attempt to resubscribe to a device (if no data was received in 10 seconds)
                if (device.Value.InputSubscription != null && DateTime.Now.AddSeconds(-10) >= device.Value.ServiceAgent.LastInputReceived)
                {
                    SendBaseMessage(device.Value.ServiceAgent, MessageBase.MessageType.Subscribe);
                }
            }
        }

        private void SendBaseMessage(ServiceAgent sa, MessageBase.MessageType messageType) {
            Debug.WriteLine($"IOWrapper| ESP8266| SendBaseMessage({sa.FullName}, {messageType})");
            var message = new MessageBase();
            message.Type = messageType;
            SendUdpPacket(sa, message);
        }

        private void SendUdpPacket(ServiceAgent serviceAgent, object messageBase)
        {
            Debug.WriteLine("IOWrapper| ESP8266| SendUDPPacket()");
            Debug.WriteLine($"IOWrapper| ESP8266| Sent UDP to {serviceAgent.FullName}: {messageBase}");
            var message = MessagePackSerializer.Serialize(messageBase);
            IPEndPoint ipEndpoint = new IPEndPoint(serviceAgent.Ip, serviceAgent.Port);
            udpClient.Send(message, message.Length, ipEndpoint);
            Debug.WriteLine($"IOWrapper| ESP8266| Sent UDP to {serviceAgent.FullName}: {MessagePackSerializer.ConvertToJson(message)}");
            serviceAgent.LastSent = DateTime.Now;
        }

        internal bool SubscribeInput(InputSubscriptionRequest subReq)
        {
            var device = LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            if (device.DeviceReportInput == null)
                return false;
            device.InputSubscription = subReq;
            SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.Subscribe);

            var timer = new Timer()
            {
                AutoReset = true,
                Enabled = true,
                Interval = 10000
            };
            timer.Elapsed += (sender, e) => onOutputTimer(sender, e, subReq.DeviceDescriptor.DeviceHandle);
            timer.Start();
            inputTimers.Add(subReq.DeviceDescriptor.DeviceHandle, timer);
            return true;
        }

        internal bool UnsubscribeInput(InputSubscriptionRequest subReq)
        {
            
            var deviceHandle = subReq.DeviceDescriptor.DeviceHandle;
            var device = LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            device.InputSubscription = subReq;
            SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.Unsubscribe);

            // Check if we have any timers to clear
            if (inputTimers.ContainsKey(deviceHandle))
            {
                // Clear timer and remove from list
                inputTimers[deviceHandle].Stop();
                inputTimers.Remove(deviceHandle);
            }
            return true;
        }

        private void onInputTimer(object source, ElapsedEventArgs e, String deviceHandle)
        {
            var device = LookupDeviceInfo(deviceHandle);
            if (device == null) return; // Device must have been removed
            if (device.ServiceAgent.LastInputReceived < DateTime.Now.AddSeconds(-10)) {
                // If it's been a while since the last input was received, attempt to subscribe again

            }
        }

        public static void ReceiveCallback(IAsyncResult ar)
        {
            byte[] receiveBytes;
            try
            {
                // Move received packet to receive buffer
                receiveBytes = udpClient.EndReceive(ar, ref ipEndPoint);
            }
            catch (System.ObjectDisposedException)
            {
                // Handles an exception that's thrown when this class is disposed
                return;
            }
            // Start async receive again
            udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), null);

            Debug.WriteLine($"IOWrapper| ESP8266| Received: {MessagePackSerializer.ConvertToJson(receiveBytes)}");

            MessageBase msg = MessagePackSerializer.Deserialize<MessageBase>(receiveBytes);
            DeviceInfo device = LookupDeviceInfo(msg.Hostname);
            if (device == null) // If we got a packet from someone we don't know, discard it
            {
                Debug.WriteLine($"IOWrapper| ESP8266| Got packet from yet unknown host {msg.Hostname}");
                // TODO: Use this to discover new devices?
                return;
            }
            device.ServiceAgent.LastReceived = DateTime.Now;
            switch (msg.Type)
            {
                case MessageBase.MessageType.HeartbeatResponse:
                    Debug.WriteLine("IOWrapper| ESP8266| Heartbeat response");
                    break;
                case MessageBase.MessageType.DescriptorResponse:
                    DescriptorMessage descriptorMessage = MessagePackSerializer.Deserialize<DescriptorMessage>(receiveBytes);
                    HandleReceivedDeviceDescriptorMessage(device, descriptorMessage);
                    break;
                case MessageBase.MessageType.Input:
                    InputMessage inputMessage = MessagePackSerializer.Deserialize<InputMessage>(receiveBytes);
                    HandleReceivedDeviceInputData(device, inputMessage);
                    break;
                default:
                    Debug.WriteLine($"IOWrapper| ESP8266| Unknown message type: {msg.Type}");
                    break;
            }
        }

        public List<DeviceReport> getInputDeviceReports()
        {
            return DiscoveredAgents.Select(d => d.Value.DeviceReportInput).ToList();
        }

        public List<DeviceReport> getOutputDeviceReports()
        {
            return DiscoveredAgents.Select(d => d.Value.DeviceReportOutput).ToList();
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
            // Check if we have input nodes
            if (inputDeviceReportNodes.Count() > 0) { 
                device.DeviceReportInput = new DeviceReport()
                {
                    DeviceName = device.ServiceAgent.Hostname,
                    DeviceDescriptor = descriptor,
                    Nodes = inputDeviceReportNodes
                };
            }

            var outputDeviceReportNodes = new List<DeviceReportNode>();
            if (descriptorMessage.Output.Buttons.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Buttons", BindingCategory.Momentary, descriptorMessage.Output.Buttons));
            if (descriptorMessage.Output.Axes.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Axes", BindingCategory.Signed, descriptorMessage.Output.Axes));
            if (descriptorMessage.Output.Deltas.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Deltas", BindingCategory.Delta, descriptorMessage.Output.Deltas));
            if (descriptorMessage.Output.Events.Count > 0) outputDeviceReportNodes.Add(BuildDeviceReportNodes("Events", BindingCategory.Event, descriptorMessage.Output.Events));
            // Check if we have output nodes
            if (outputDeviceReportNodes.Count() > 0)
            {
                device.DeviceReportOutput = new DeviceReport()
                {
                    DeviceName = device.ServiceAgent.Hostname,
                    DeviceDescriptor = descriptor,
                    Nodes = outputDeviceReportNodes
                };
                // Build output message
                device.OutputMessage = new OutputMessage();
                device.OutputMessage.configureMessage(descriptorMessage);
            }
        }

        private static void HandleReceivedDeviceInputData(DeviceInfo device, InputMessage inputMessage) {
            // TODO: Handle inputs
            Debug.WriteLine($"IOWrapper| ESP8266| ReceivedInput: {inputMessage.Buttons.Count} buttons");
            if (device.InputSubscription == null)
                return;
            device.ServiceAgent.LastInputReceived = DateTime.Now;
        }

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
            udpClient?.Close();
            udpClient?.Dispose();
        }
    }
}
