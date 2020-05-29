using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Core_ESP8266.Model.Message;
using HidWizards.IOWrapper.DataTransferObjects;

namespace Core_ESP8266.Model
{
    public class DeviceInfo
    {
        public ServiceAgent ServiceAgent { get; set; }
        public DeviceReport DeviceReportInput { get; set; }
        public DeviceReport DeviceReportOutput { get; set; }
        public InputMessage InputMessage { get; set; }
        public OutputMessage OutputMessage { get; set; }
        public InputSubscriptionRequest InputSubscription { get; set; }
        public OutputSubscriptionRequest OutputSubscription { get; set; }

    }
}
