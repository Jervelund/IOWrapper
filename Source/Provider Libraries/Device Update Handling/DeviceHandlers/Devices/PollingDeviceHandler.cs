﻿using System;
using System.Threading;
using Hidwizards.IOWrapper.Libraries.SubscriptionHandlerNs;
using HidWizards.IOWrapper.DataTransferObjects;

namespace Hidwizards.IOWrapper.Libraries.DeviceHandlers.Devices
{
    /// <summary>
    /// Acquires a device, polls it, and sends updates to it's <see cref="DeviceUpdateHandler"/>
    /// Also routes subscription requests through to it's <see cref="SubscriptionHandler"/>
    /// </summary>
    /// <typeparam name="TUpdate"></typeparam>
    public abstract class PollingDeviceHandler<TUpdate> : DeviceHandlerBase<TUpdate>, IDisposable
    {
        private Thread _pollThread;

        protected PollingDeviceHandler(DeviceDescriptor deviceDescriptor, EventHandler<DeviceDescriptor> deviceEmptyHandler, EventHandler<BindModeUpdate> bindModeHandler)
            : base(deviceDescriptor, deviceEmptyHandler, bindModeHandler)
        {
            _pollThread = new Thread(PollThread);
            _pollThread.Start();
        }

        protected abstract void PollThread();

        public void Dispose()
        {
            _pollThread.Abort();
            _pollThread.Join();
            _pollThread = null;
        }
    }
}