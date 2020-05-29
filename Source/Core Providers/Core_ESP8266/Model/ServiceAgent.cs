using System;
using System.Net;

namespace Core_ESP8266.Model
{
    public class ServiceAgent
    {
        public ServiceAgent()
        {
            LastSent = DateTime.Now;
            LastReceived = DateTime.Now;
            LastSent = DateTime.Now;
        }

        public string FullName => $"{Hostname} ({Ip}:{Port})";
        public string Hostname { get; set; }

        public IPAddress Ip { get; set; }
        public int Port { get; set; }

        public DateTime LastInputReceived { get; set; }
        public DateTime LastReceived { get; set; }
        public DateTime LastSent { get; set; }
    }
}
