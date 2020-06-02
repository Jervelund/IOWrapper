using Core_ESP8266.Model;
using Core_ESP8266.Model.Message;
using Hidwizards.IOWrapper.Libraries.SubscriptionHandlers;
using HidWizards.IOWrapper.DataTransferObjects;
using MessagePack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
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
        private static Action<ProviderDescriptor, DeviceDescriptor, BindingReport, short> BindModeCallback { get; set; }
        private static ProviderDescriptor _providerDescriptor;
        private static ConcurrentQueue<QueuedUDPPacket> SendQueue;

        public NetworkManager(ProviderDescriptor providerDescriptor)
        {
            DiscoveredAgents = new Dictionary<string, DeviceInfo>();
            outputTimers = new Dictionary<string, Timer>();
            inputTimers = new Dictionary<string, Timer>();
            _providerDescriptor = providerDescriptor;
            SendQueue = new ConcurrentQueue<QueuedUDPPacket>();

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
            var deviceDescriptor = new DeviceDescriptor()
            {
                DeviceHandle = e.Announcement.Hostname,
                DeviceInstance = 0
            };
            deviceInfo.InputSubscriptionHandler = new SubscriptionHandler(deviceDescriptor, deviceEmptyHandler, CallbackHandler);

            DiscoveredAgents.Add(e.Announcement.Hostname, deviceInfo);
            SendBaseMessage(deviceInfo.ServiceAgent, MessageBase.MessageType.DescriptorRequest, 1);
        }

        private void CallbackHandler(InputSubscriptionRequest subReq, short value)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| callbackHandler: {subReq}");
            Task.Factory.StartNew(() => subReq.Callback(value));
        }

        private void deviceEmptyHandler(object sender, DeviceDescriptor e)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| deviceEmptyHandler: {e.DeviceHandle}");
            var device = LookupDeviceInfo(e.DeviceHandle);
            if (device == null) return;

            // No more subscriptions - notify device that it should stop updating us
            SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.Unsubscribe, 1);

            // Check if we have any timers to clear
            if (inputTimers.ContainsKey(e.DeviceHandle))
            {
                // Clear timer and remove from list
                inputTimers[e.DeviceHandle].Stop();
                inputTimers.Remove(e.DeviceHandle);
            }
        }

        internal bool SubscribeOutput(OutputSubscriptionRequest subReq)
        {
            // We can only send data to devices we know, so don't allow users to subscribe if the device is not able to respond with descriptors
            var deviceHandle = subReq.DeviceDescriptor.DeviceHandle;
            var device = LookupDeviceInfo(deviceHandle);
            if (device == null || device.OutputMessage == null)
                return false;

            // Add subReq to list of active output subsctiptions
            if (!device.ActiveOutputSubscriptions.ContainsKey(subReq.SubscriptionDescriptor.SubscriberGuid))
                device.ActiveOutputSubscriptions.Add(subReq.SubscriptionDescriptor.SubscriberGuid, subReq);

            if (!outputTimers.ContainsKey(deviceHandle)){ 
                var timer = new Timer() {
                    AutoReset = true,
                    Enabled = true,
                    Interval = 1000
                };
                timer.Elapsed += (sender, e) => onOutputTimer(sender, e, deviceHandle);
                timer.Start();
                outputTimers.Add(deviceHandle, timer);
                Debug.WriteLine($"IOWrapper| ESP8266| Added output timer ({device.ServiceAgent.FullName})");
            }
            return true;
        }

        internal bool UnsubscribeOutput(OutputSubscriptionRequest subReq)
        {
            Debug.WriteLine("IOWrapper| ESP8266| UnsubscribeOutput()");
            // We can only send data to devices we know, so don't allow users to subscribe if the device is not able to respond with descriptors
            var deviceHandle = subReq.DeviceDescriptor.DeviceHandle;
            var device = LookupDeviceInfo(deviceHandle);
            if (device == null || device.OutputMessage == null)
                return false;
            // Remove subReq from the active output subscription list
            device.ActiveOutputSubscriptions.Remove(subReq.SubscriptionDescriptor.SubscriberGuid);
            Debug.WriteLine($"IOWrapper| ESP8266| -> Active output subscription count: {device.ActiveOutputSubscriptions.Count}");
            // If the active subscription list is now empty, remove the output timer
            if (device.ActiveOutputSubscriptions.Count == 0) { 
                if (outputTimers.ContainsKey(deviceHandle))
                {
                    outputTimers[deviceHandle].Stop();
                    outputTimers.Remove(deviceHandle);
                    Debug.WriteLine($"IOWrapper| ESP8266| Removed output timer ({device.ServiceAgent.FullName})");
                }
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
            // Attempt to transmit the output message right away
            transmitOutput(device);
        }

        private void onOutputTimer(object source, ElapsedEventArgs e, String deviceHandle)
        {
            var device = LookupDeviceInfo(deviceHandle);
            if (device == null) return; // Device must have been removed
            transmitOutput(device);
        }

        private void transmitOutput(DeviceInfo device) {
            SendUdpPacket(device.ServiceAgent, device.OutputMessage, 0);

            // Clear all deltas and events, once they have been transmitted
            for (int i = 0; i < device.OutputMessage.Deltas.Count; i++)
                device.OutputMessage.Deltas[i] = 0;
            for (int i = 0; i < device.OutputMessage.Events.Count; i++)
                device.OutputMessage.Events[i] = 0;
        }

        internal void SetDetectionMode(DetectionMode detectionMode, DeviceDescriptor deviceDescriptor, Action<ProviderDescriptor, DeviceDescriptor, BindingReport, short> callback)
        {
            var device = LookupDeviceInfo(deviceDescriptor.DeviceHandle);
            // Check if device exists
            if (device == null) return;

            if (detectionMode == DetectionMode.Bind)
            {
                // Switch to bind mode
                BindModeCallback = callback;
                device._detectionMode = detectionMode;
                SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.BindStart, 1);
            }
            if(detectionMode == DetectionMode.Subscription)
            {
                // Switch to subscription mode
                device._detectionMode = detectionMode;
                SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.BindStop, 1);
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
                    // Request descriptor report if it is missing
                    SendBaseMessage(device.Value.ServiceAgent, MessageBase.MessageType.DescriptorRequest, 1);
                }
            }
        }

        private bool SendBaseMessage(ServiceAgent sa, MessageBase.MessageType messageType, int serviceLevel) {
            Debug.WriteLine($"IOWrapper| ESP8266| SendBaseMessage({sa.FullName}, {messageType})");
            var message = new MessageBase();
            message.MsgType = messageType;
            return SendUdpPacket(sa, message, serviceLevel);
        }

        private bool SendUdpPacket(ServiceAgent serviceAgent, object messageBase, int serviceLevel)
        {
            Debug.WriteLine("IOWrapper| ESP8266| SendUDPPacket()");
            var message = MessagePackSerializer.Serialize(messageBase);
            // Don't allow retransmission if we sent a packet in the last 20 miliseconds, as this will screw the ESP's receive buffer
            if (serviceAgent.LastSent > DateTime.Now.AddMilliseconds(-20))
            {
                if (serviceLevel == 0)
                {
                    Debug.WriteLine($"IOWrapper| ESP8266| Transmit skipped due to antiflooding ({serviceAgent.FullName})");
                    return false;
                }
                else
                {
                    SendQueue.Enqueue(item: new QueuedUDPPacket(serviceAgent, message));
                }
            }
            // If we are going to send a non-important packet - check if we have an important to send instead
            if (serviceLevel == 0 && SendQueue.Count > 0 )
            {
                SendQueue.TryDequeue(out QueuedUDPPacket result);
                serviceAgent = result.ServiceAgent;
                message = result.Message;
            }
            IPEndPoint ipEndpoint = new IPEndPoint(serviceAgent.Ip, serviceAgent.Port);
            udpClient.Send(message, message.Length, ipEndpoint);
            serviceAgent.LastSent = DateTime.Now;
            Debug.WriteLine($"IOWrapper| ESP8266| Sent UDP to {serviceAgent.FullName}: {messageBase}");
            return true;
        }

        internal bool SubscribeInput(InputSubscriptionRequest subReq)
        {
            var device = LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            if (device.DeviceReportInput == null)
                return false;
            device.InputSubscriptionHandler.Subscribe(subReq);

            // Make sure the keepalive timer for this device is running
            if (!inputTimers.ContainsKey(subReq.DeviceDescriptor.DeviceHandle)) {
                SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.Subscribe, 1);
                var timer = new Timer()
                {
                    AutoReset = true,
                    Enabled = true,
                    Interval = 1000
                };
                timer.Elapsed += (sender, e) => OnInputTimer(sender, e, subReq.DeviceDescriptor.DeviceHandle);
                timer.Start();
                inputTimers.Add(subReq.DeviceDescriptor.DeviceHandle, timer);
            }
            return true;
        }

        internal bool UnsubscribeInput(InputSubscriptionRequest subReq)
        {
            
            var deviceHandle = subReq.DeviceDescriptor.DeviceHandle;
            var device = LookupDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            device.InputSubscriptionHandler.Unsubscribe(subReq);
            return true;
        }

        private void OnInputTimer(object source, ElapsedEventArgs e, String deviceHandle)
        {
            var device = LookupDeviceInfo(deviceHandle);
            if (device == null) return; // Device must have been removed
            if (device.ServiceAgent.LastSent < DateTime.Now.AddSeconds(-2))
            {
                SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.HeartbeatResponse, 0);
            }
            if (device.ServiceAgent.LastInputReceived < DateTime.Now.AddSeconds(-10))
            {
                // If it's been a while since the last input was received, attempt to subscribe again
                Debug.WriteLine($"IOWrapper| ESP8266| Did not receive input messages from {deviceHandle} for a while. Attempting to resubscribe.");
                SendBaseMessage(device.ServiceAgent, MessageBase.MessageType.Subscribe, 1);
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
                Debug.WriteLine($"IOWrapper| ESP8266| ReceiveCallback() caught ObjectDisposedException");
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
            switch (msg.MsgType)
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
                case MessageBase.MessageType.BindResponse:
                    BindResponseMessage bindResponseMessage = MessagePackSerializer.Deserialize<BindResponseMessage>(receiveBytes);
                    HandleBindResponseMessageReceived(device, bindResponseMessage);
                    break;
                default:
                    Debug.WriteLine($"IOWrapper| ESP8266| Unknown message type: {msg.MsgType}");
                    break;
            }
        }

        private static void HandleBindResponseMessageReceived(DeviceInfo device, BindResponseMessage bindResponseMessage)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| HandleBindResponseMessageReceived()");
            if (device._detectionMode == DetectionMode.Bind)
            {
                Debug.WriteLine($"IOWrapper| ESP8266| -> Device is in bind mode");
                var bindingType = BindingCategory.Momentary;
                var inputList = device.DescriptorMessage.Input.Buttons;
                switch (bindResponseMessage.Category)
                {
                    default:
                    case "b":
                        // Use default initialization above
                        break;
                    case "a":
                        bindingType = BindingCategory.Signed;
                        inputList = device.DescriptorMessage.Input.Axes;
                        break;
                    case "e":
                        bindingType = BindingCategory.Event;
                        inputList = device.DescriptorMessage.Input.Events;
                        break;
                    case "d":
                        bindingType = BindingCategory.Delta;
                        inputList = device.DescriptorMessage.Input.Deltas;
                        break;
                }
                
                var bindingDesctiptor = EspUtility.GetBindingDescriptor(bindingType, bindResponseMessage.Index);
                DeviceDescriptor deviceDescriptor = new DeviceDescriptor()
                {
                    DeviceHandle = device.ServiceAgent.Hostname,
                    DeviceInstance = 0 // Unused
                };
                BindingReport bindingReport = new BindingReport()
                {
                    Title = device.ServiceAgent.Hostname,
                    Category = bindingType,
                    Path = $"{bindingType} > {inputList[bindResponseMessage.Index].Name}",
                    Blockable = false,
                    BindingDescriptor = bindingDesctiptor
                };
                Debug.WriteLine($"IOWrapper| ESP8266| -> Invoking callback");
                BindModeCallback?.Invoke(_providerDescriptor, deviceDescriptor, bindingReport, bindResponseMessage.Value);
            }
        }

        public List<DeviceReport> getInputDeviceReports()
        {
            Debug.WriteLine($"IOWrapper| ESP8266| getInputDeviceReports()");
            return DiscoveredAgents.Select(d => d.Value.DeviceReportInput).ToList();
        }

        public List<DeviceReport> getOutputDeviceReports()
        {
            Debug.WriteLine($"IOWrapper| ESP8266| getOutputDeviceReports()");
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

            device.DescriptorMessage = descriptorMessage;
        }

        private static void processInputType(DeviceInfo device, List<short> inputs, BindingCategory type, List<IODescriptor> descriptorInput)
        {
            for (int key = 0; key < inputs.Count; key++)
            {
                //var bindingDesctiptor = new BindingDescriptor() { Type = BindingType.Button, Index = key, SubIndex = 0 };
                var bindingDesctiptor = EspUtility.GetBindingDescriptor(type, key);
                if (device._detectionMode == DetectionMode.Subscription)
                {
                    device.InputSubscriptionHandler.FireCallbacks(bindingDesctiptor, inputs[key]);
                }
                if (device._detectionMode == DetectionMode.Bind)
                {
                    DeviceDescriptor deviceDescriptor = new DeviceDescriptor()
                    {
                        DeviceHandle = device.ServiceAgent.Hostname,
                        DeviceInstance = 0 // Unused
                    };
                    BindingReport bindingReport = new BindingReport()
                    {
                        Title = device.ServiceAgent.Hostname,
                        Category = type,
                        Path = $"{type} > {descriptorInput[key].Name}",
                        Blockable = false,
                        BindingDescriptor = bindingDesctiptor
                    };

                    BindModeCallback?.Invoke(_providerDescriptor, deviceDescriptor, bindingReport, inputs[key]);
                }
            }
        }

        private static void HandleReceivedDeviceInputData(DeviceInfo device, InputMessage inputMessage) {
            // TODO: Handle inputs
            Debug.WriteLine($"IOWrapper| ESP8266| ReceivedInput: {inputMessage.Buttons.Count} buttons");

            processInputType(device, inputMessage.Buttons, BindingCategory.Momentary, device.DescriptorMessage.Input.Buttons);
            processInputType(device, inputMessage.Axes, BindingCategory.Signed, device.DescriptorMessage.Input.Axes);
            processInputType(device, inputMessage.Deltas, BindingCategory.Delta, device.DescriptorMessage.Input.Deltas);
            processInputType(device, inputMessage.Events, BindingCategory.Event, device.DescriptorMessage.Input.Events);

            device.ServiceAgent.LastInputReceived = DateTime.Now;
        }

        private static DeviceReportNode BuildDeviceReportNodes(string category, BindingCategory bindingCategory, List<IODescriptor> descriptors)
        {
            var bindings = new List<BindingReport>();
            foreach (var ioDescriptor in descriptors)
            {
                bindings.Add(new BindingReport()
                {
                    Title = ioDescriptor.Name,
                    Category = bindingCategory,
                    Path = $"{category} > {ioDescriptor.Name}",
                    Blockable = false,
                    BindingDescriptor = EspUtility.GetBindingDescriptor(bindingCategory, ioDescriptor.Value)
                });
            }

            return new DeviceReportNode()
            {
                Title = category,
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
