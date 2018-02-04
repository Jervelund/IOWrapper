﻿using Providers.Handlers;

namespace SharpDX_XInput.Handlers
{
    public class XiTriggerindingHandler : BindingHandler
    {
        public override void Poll(int pollValue)
        {
            // Normalization of Axes to standard scale occurs here
            _bindingDictionary[0].State =
                (pollValue * 257) - 32768;
        }
    }
}