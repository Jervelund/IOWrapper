using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using Core_ESP8266.Managers;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.IOWrapper.ProviderInterface.Interfaces;

namespace Core_ESP8266
{
    [Export(typeof(IProvider))]
    public class CoreEsp8266 : IOutputProvider, IInputProvider
    {
        public string ProviderName => "Core_ESP8266";
        public bool IsLive => true;

        private NetworkManager NetworkManager { get; set; }

        public CoreEsp8266()
        {
            Debug.WriteLine("IOWrapper| ESP8266| ESP8266()");
            NetworkManager = new NetworkManager();
        }

        public void RefreshLiveState()
        {
            Debug.WriteLine("IOWrapper| ESP8266| RefreshLiveState()");
        }

        public void RefreshDevices()
        {
            // mDNS handles device discovery / removal
            Debug.WriteLine("IOWrapper| ESP8266| RefreshDevices()");
            NetworkManager.RefreshDevices();
        }

        public ProviderReport GetOutputList()
        {
            Debug.WriteLine("IOWrapper| ESP8266| GetOutputList()");
            if (NetworkManager.getOutputDeviceReports().Count() > 0)
                Debug.WriteLine($"{NetworkManager.getOutputDeviceReports().Count} {NetworkManager.getOutputDeviceReports()[0].DeviceDescriptor}");
            return new ProviderReport()
            {
                Title = "Core ESP8266",
                API = ProviderName,
                Description = "Send input to external ESP8266 modules",
                Devices = NetworkManager.getOutputDeviceReports(),
                ProviderDescriptor = new ProviderDescriptor()
                {
                    ProviderName = ProviderName
                }
            };
        }

        public bool SetOutputState(OutputSubscriptionRequest subReq, BindingDescriptor bindingDescriptor, int state)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| SetOutputState() {subReq.DeviceDescriptor.DeviceHandle} {state}");
            NetworkManager.setOutput(subReq, bindingDescriptor, state);
            return true;
        }

        public bool SubscribeOutputDevice(OutputSubscriptionRequest subReq)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| SubscribeOutputDevice() {subReq.DeviceDescriptor.DeviceHandle}");
            return true;
            /*throw new NotImplementedException();
            var deviceInfo = DiscoveryManager.FindOutputDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            if (deviceInfo == null) return false;
            return DescriptorManager.StartOutputDevice(deviceInfo);*/
        }
        
        public bool UnSubscribeOutputDevice(OutputSubscriptionRequest subReq)
        {
            Debug.WriteLine($"IOWrapper| ESP8266| UnSubscribeOutputDevice() {subReq.DeviceDescriptor.DeviceHandle}");
            return true;
            /*throw new NotImplementedException();
            var deviceInfo = DiscoveryManager.FindOutputDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            if (deviceInfo == null) return false;
            return DescriptorManager.StopOutputDevice(deviceInfo);*/
        }

        public DeviceReport GetOutputDeviceReport(DeviceDescriptor deviceDescriptor)
        {
            Debug.WriteLine("IOWrapper| ESP8266| GetOutputDeviceReport()");
            return NetworkManager.LookupDeviceInfo(deviceDescriptor.DeviceHandle).DeviceReportOutput;
        }

        public void Dispose()
        {
            Debug.WriteLine("IOWrapper| ESP8266| Dispose()");
            NetworkManager?.Dispose();
        }

        public ProviderReport GetInputList()
        {
            Debug.WriteLine("IOWrapper| ESP8266| GetInputList()");
            if(NetworkManager.getInputDeviceReports().Count()>0)
                Debug.WriteLine($"{NetworkManager.getInputDeviceReports().Count} {NetworkManager.getInputDeviceReports()[0].DeviceDescriptor}");
            return new ProviderReport()
            {
                Title = "Core ESP8266",
                API = ProviderName,
                Description = "Receive input from external ESP8266 modules",
                Devices = NetworkManager.getInputDeviceReports(),
                ProviderDescriptor = new ProviderDescriptor()
                {
                    ProviderName = ProviderName
                }
            };
        }

        public DeviceReport GetInputDeviceReport(DeviceDescriptor deviceDescriptor)
        {
            Debug.WriteLine("IOWrapper| ESP8266| GetInputDeviceReport()");
            return NetworkManager.LookupDeviceInfo(deviceDescriptor.DeviceHandle).DeviceReportInput;
        }

        public bool SubscribeInput(InputSubscriptionRequest subReq)
        {
            Debug.WriteLine("IOWrapper| ESP8266| SubscribeInput()");
            NetworkManager.Subscribe(subReq);
            throw new NotImplementedException();
        }

        public bool UnsubscribeInput(InputSubscriptionRequest subReq)
        {
            Debug.WriteLine("IOWrapper| ESP8266| UnsubscribeInput()");
            NetworkManager.Unsubscribe(subReq);
            throw new NotImplementedException();
        }
    }
}
