using System;
using System.Collections.Generic;
using System.Text;
using EnlightenMAUI.Models;

namespace EnlightenMAUI.Platforms.iOS
{
    internal class PlatformBluetooth
    {
        internal static async Task<List<USBViewDevice>> doUSBScanAsync()
        {
            Logger logger = Logger.getInstance();
            List<USBViewDevice> usbDeviceList = new List<USBViewDevice>();

            return usbDeviceList;
        }

        internal static async Task<bool> doConnectOrDisconnectUSBAsync(Spectrometer spec)
        {
            return false;
        }

    }
}
