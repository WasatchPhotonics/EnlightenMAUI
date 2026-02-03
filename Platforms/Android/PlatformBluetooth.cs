using AndrApp = Android.App;
using Android.Content;
using Android.Hardware.Usb;
using Microsoft.Maui.Controls.PlatformConfiguration;
using System;
using System.Collections.Generic;
using System.Text;
using EnlightenMAUI.Models;

namespace EnlightenMAUI.Platforms.Android
{
    internal class PlatformBluetooth
    {
        static UsbDevice device;
        static AndrApp.PendingIntent usbIntent;


        private static async Task<bool> doUSBScanAsync()
        {
            Logger logger = Logger.getInstance();

            logger.info("Looking for usb devices via Android services");
            try
            {
                Context con = AndrApp.Application.Context;
                UsbManager manager = (UsbManager)con.GetSystemService(Context.UsbService);

                var features = con.PackageManager.GetSystemAvailableFeatures();
                foreach (var feature in features)
                {
                    logger.info("{0} feature available", feature.Name);
                }

                if (manager.DeviceList.Count == 0)
                {
                    logger.info("No USB devices found");
                }

                foreach (UsbDevice acc in manager.DeviceList.Values)
                {
                    device = acc;

                    String desc = String.Format("Vid:0x{0:x4} Pid:0x{1:x4} (rev:{2}) - {3}",
                        acc.VendorId,
                        acc.ProductId,
                        acc.Version,
                        acc.DeviceName);

                    logger.info("found usb device {0}", desc);

                    if (acc.VendorId == 0x24aa)
                    {
                        USBViewDevice uvd = new USBViewDevice(acc.DeviceName, acc.VendorId.ToString("x4"), acc.ProductId.ToString("x4"));
                        usbDeviceList.Add(uvd);

                        usbIntent = AndrApp.PendingIntent.GetBroadcast(con, 0, new Intent(ACTION_USB_PERMISSION), PendingIntentFlags.Immutable);

                        //LibUsbDotNet.UsbDevice usbDevice = LibUsbDotNet.UsbDevice.OpenUsbDevice(d => d.Pid == acc.ProductId);
                        manager.RequestPermission(acc, usbIntent);

                    }

                }
            }
            catch (Exception ex)
            {
                logger.info("USB grab failed with error {0}", ex.Message);
            }

            return true;

        }

    }
}
