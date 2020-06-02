using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core_ESP8266.Model.Message;
using Hidwizards.IOWrapper.Libraries.SubscriptionHandlers;
using HidWizards.IOWrapper.DataTransferObjects;

namespace Core_ESP8266.Model
{
    public class DeviceInfo
    {
        public DeviceInfo()
        {
            ActiveOutputSubscriptions = new Dictionary<Guid, OutputSubscriptionRequest>();
        }
        public ServiceAgent ServiceAgent { get; set; }
        public DeviceReport DeviceReportInput { get; set; }
        public DeviceReport DeviceReportOutput { get; set; }
        public InputMessage LastInputMessage { get; set; }
        public DescriptorMessage DescriptorMessage { get; set; }
        public OutputMessage OutputMessage { get; set; }
        public SubscriptionHandler InputSubscriptionHandler { get; set; }
        public Dictionary<Guid, OutputSubscriptionRequest> ActiveOutputSubscriptions { get; set; }
        public Action<ProviderDescriptor, DeviceDescriptor, BindingReport, short> BindCallback { get; set; }
        public DetectionMode _detectionMode { get; set; }
    }
}
