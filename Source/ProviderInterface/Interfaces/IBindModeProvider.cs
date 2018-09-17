﻿using System;
using HidWizards.IOWrapper.DataTransferObjects;

namespace HidWizards.IOWrapper.ProviderInterface.Interfaces
{
    /// <summary>
    /// Provider supports "Bind Mode" (Press any input to bind)
    /// </summary>
    public interface IBindModeProvider : IProvider
    {
        void SetDetectionMode(DetectionMode detectionMode, DeviceDescriptor deviceDescriptor, Action<ProviderDescriptor, DeviceDescriptor, BindingDescriptor, int> callback = null);
    }
}