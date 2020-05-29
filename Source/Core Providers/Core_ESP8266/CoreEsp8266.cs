using System;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using Core_ESP8266.Managers;
using Core_ESP8266.Model;
using HidWizards.IOWrapper.DataTransferObjects;
using HidWizards.IOWrapper.ProviderInterface.Interfaces;

namespace Core_ESP8266
{
    [Export(typeof(IProvider))]
    public class CoreEsp8266 : IOutputProvider, IInputProvider
    {
        public string ProviderName => "Core_ESP8266";
        public bool IsLive { get; } = false;

        private NetworkManager NetworkManager { get; set; }

        public CoreEsp8266()
        {
            Debug.WriteLine("IOWrapper| ESP8266| ESP8266()");
            try
            {
                NetworkManager = new NetworkManager();
                IsLive = true;
            }
            catch (System.TypeInitializationException e) {
                
            }
        }

        public void RefreshLiveState()
        {
            if (IsLive == false) return;
            Debug.WriteLine("IOWrapper| ESP8266| RefreshLiveState()");
        }

        public void RefreshDevices()
        {
            if (IsLive == false) return;
            // mDNS handles device discovery / removal
            Debug.WriteLine("IOWrapper| ESP8266| RefreshDevices()");
            NetworkManager.RefreshDevices();
        }

        public ProviderReport GetOutputList()
        {
            Debug.WriteLine("IOWrapper| ESP8266| GetOutputList()");
            ProviderReport providerReport = new ProviderReport()
            {
                Title = "Core ESP8266",
                API = ProviderName,
                Description = "Send output to external ESP8266 modules",
                ProviderDescriptor = new ProviderDescriptor()
                {
                    ProviderName = ProviderName
                }
            };
            if (IsLive)
                providerReport.Devices = NetworkManager.getOutputDeviceReports();
            return providerReport;
        }

        public bool SetOutputState(OutputSubscriptionRequest subReq, BindingDescriptor bindingDescriptor, int state)
        {
            if (IsLive == false) return false;
            Debug.WriteLine($"IOWrapper| ESP8266| SetOutputState() {subReq.DeviceDescriptor.DeviceHandle} {bindingDescriptor.Type} {bindingDescriptor.Index} {bindingDescriptor.SubIndex} {state}");
            NetworkManager.setOutput(subReq, bindingDescriptor, state);
            return true;
        }

        public bool SubscribeOutputDevice(OutputSubscriptionRequest subReq)
        {
            if (IsLive == false) return false;
            Debug.WriteLine($"IOWrapper| ESP8266| SubscribeOutputDevice() {subReq.DeviceDescriptor.DeviceHandle}");
            return NetworkManager.SubscribeOutput(subReq);
            /*throw new NotImplementedException();
            var deviceInfo = DiscoveryManager.FindOutputDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            if (deviceInfo == null) return false;
            return DescriptorManager.StartOutputDevice(deviceInfo);*/
        }
        
        public bool UnSubscribeOutputDevice(OutputSubscriptionRequest subReq)
        {
            if (IsLive == false) return false;
            Debug.WriteLine($"IOWrapper| ESP8266| UnSubscribeOutputDevice() {subReq.DeviceDescriptor.DeviceHandle}");
            return NetworkManager.UnsubscribeOutput(subReq);
            /*throw new NotImplementedException();
            var deviceInfo = DiscoveryManager.FindOutputDeviceInfo(subReq.DeviceDescriptor.DeviceHandle);
            if (deviceInfo == null) return false;
            return DescriptorManager.StopOutputDevice(deviceInfo);*/
        }

        public DeviceReport GetOutputDeviceReport(DeviceDescriptor deviceDescriptor)
        {
            if (IsLive == false) return new DeviceReport();
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
            ProviderReport providerReport = new ProviderReport()
            {
                Title = "Core ESP8266",
                API = ProviderName,
                Description = "Receive input from external ESP8266 modules",
                ProviderDescriptor = new ProviderDescriptor()
                {
                    ProviderName = ProviderName
                }
            };
            if (IsLive)
                providerReport.Devices = NetworkManager.getInputDeviceReports();
            return providerReport;
        }

        public DeviceReport GetInputDeviceReport(DeviceDescriptor deviceDescriptor)
        {
            if (IsLive == false) return new DeviceReport();
            Debug.WriteLine("IOWrapper| ESP8266| GetInputDeviceReport()");
            return NetworkManager.LookupDeviceInfo(deviceDescriptor.DeviceHandle).DeviceReportInput;
        }

        public bool SubscribeInput(InputSubscriptionRequest subReq)
        {
            if (IsLive == false) return false;
            Debug.WriteLine("IOWrapper| ESP8266| SubscribeInput()");
            return NetworkManager.SubscribeInput(subReq);
            // throw new NotImplementedException();
        }

        public bool UnsubscribeInput(InputSubscriptionRequest subReq)
        {
            if (IsLive == false) return false;
            Debug.WriteLine("IOWrapper| ESP8266| UnsubscribeInput()");
            return NetworkManager.UnsubscribeInput(subReq);
            // throw new NotImplementedException();
        }
    }
}
