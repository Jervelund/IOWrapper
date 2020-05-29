using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Remoting;
using System.Text;
using System.Threading.Tasks;
using Core_ESP8266.Model;
using Core_ESP8266.Model.Message;
using HidWizards.IOWrapper.DataTransferObjects;
using Tmds.MDns;

namespace Core_ESP8266.Managers
{
    public class DiscoveryManager
    {
        private const string ServiceType = "_ucr._udp";

        public Dictionary<string, DeviceInfo> InputDeviceInfos { get; set; }
        public Dictionary<string, DeviceInfo> OutputDeviceInfos { get; set; }
        public Dictionary<int, ServiceAgent> DiscoveredAgents { get; set; }
        // DiscoveredAgents = new Dictionary<int, ServiceAgent>();

        private ServiceBrowser _serviceBrowser;
        private UdpManager _udpManager;

        public DiscoveryManager(UdpManager udpManager)
        {
            _udpManager = udpManager;
            DiscoveredAgents = new Dictionary<int, ServiceAgent>();
            InputDeviceInfos = new Dictionary<string, DeviceInfo>();
            OutputDeviceInfos = new Dictionary<string, DeviceInfo>();

            _serviceBrowser = new ServiceBrowser();
            _serviceBrowser.ServiceAdded += OnServiceAdded;
            _serviceBrowser.ServiceRemoved += OnServiceRemoved;
            _serviceBrowser.ServiceChanged += OnServiceChanged;

            Debug.WriteLine($"IOWrapper | ESP8266| Browsing for type: {ServiceType}");
            _serviceBrowser.StartBrowse(ServiceType);
        }

        public DeviceInfo FindInputDeviceInfo(string name)
        {
            if (!InputDeviceInfos.ContainsKey(name)) return null;
            return InputDeviceInfos[name];
        }

        public DeviceInfo FindOutputDeviceInfo(string name)
        {
            if (!OutputDeviceInfos.ContainsKey(name)) return null;
            return OutputDeviceInfos[name];
        }

        private void OnServiceChanged(object sender, ServiceAnnouncementEventArgs e)
        {
            // Not handled
            Debug.WriteLine($"IOWrapper| ESP8266| OnServiceChanged: {e}");
        }

        private void OnServiceRemoved(object sender, ServiceAnnouncementEventArgs e)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| OnServiceRemoved: {e}");/*
            var inputDeviceInfo = FindInputDeviceInfo(e.Announcement.Hostname);
            if (inputDeviceInfo != null) InputDeviceInfos.Remove(inputDeviceInfo.DeviceReport.DeviceName);
            var outputDeviceInfo = FindOutputDeviceInfo(e.Announcement.Hostname);
            if (outputDeviceInfo != null) OutputDeviceInfos.Remove(outputDeviceInfo.DeviceReport.DeviceName);*/
        }

        private void OnServiceAdded(object sender, ServiceAnnouncementEventArgs e)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| OnServiceAdded: {e}");
            var serviceAgent = new ServiceAgent()
            {
                Hostname = e.Announcement.Hostname,
                Ip = e.Announcement.Addresses[0],
                Port = e.Announcement.Port
            };

            _udpManager.RequestDescriptor(serviceAgent);
            /*
            if (BuildDeviceReport(serviceAgent, out var inputReport, out var outputReport, out var descriptorMessage))
            {
                InputDeviceInfos.Add(e.Announcement.Hostname, new DeviceInfo()
                {
                    ServiceAgent = serviceAgent,
                    DeviceReport = inputReport,
                    DescriptorMessage = descriptorMessage
                });
                OutputDeviceInfos.Add(e.Announcement.Hostname, new DeviceInfo()
                {
                    ServiceAgent = serviceAgent,
                    DeviceReport = outputReport,
                    DescriptorMessage = descriptorMessage
                });
            }
            */
        }

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

        private DeviceReportNode BuildDeviceReportNodes(string name, BindingCategory bindingCategory, List<IODescriptor> descriptors)
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
    }
}