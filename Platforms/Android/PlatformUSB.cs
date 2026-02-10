using Android.Content;
using Android.Hardware.Usb;
using EnlightenMAUI.Models;
using Microsoft.Maui.Controls.PlatformConfiguration;
using System;
using System.Collections.Generic;
using System.Text;
using static Android.Widget.GridLayout;
using AndrApp = Android.App;

namespace EnlightenMAUI.Platforms.Android
{
    internal class PlatformUSB
    {
        static UsbDevice device;
        static AndrApp.PendingIntent usbIntent;
        private static string ACTION_USB_PERMISSION = "com.android.example.USB_PERMISSION";


        internal static async Task<List<USBViewDevice>> doUSBScanAsync()
        {
            Logger logger = Logger.getInstance();
            List<USBViewDevice> usbDeviceList = new List<USBViewDevice>();

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
                        //usbDeviceList.Add(uvd);

                        usbIntent = AndrApp.PendingIntent.GetBroadcast(con, 0, new Intent(ACTION_USB_PERMISSION), AndrApp.PendingIntentFlags.Immutable);

                        //LibUsbDotNet.UsbDevice usbDevice = LibUsbDotNet.UsbDevice.OpenUsbDevice(d => d.Pid == acc.ProductId);
                        manager.RequestPermission(acc, usbIntent);

                    }

                }
            }
            catch (Exception ex)
            {
                logger.info("USB grab failed with error {0}", ex.Message);
            }

            return usbDeviceList;
        }


        internal static async Task<bool> doConnectOrDisconnectUSBAsync(Spectrometer spec)
        {
            Logger logger = Logger.getInstance();
            if (spec == null || (spec is BluetoothSpectrometer))
            {
                try
                {
                    Context con = AndrApp.Application.Context;
                    UsbManager manager = (UsbManager)con.GetSystemService(Context.UsbService);
                    int interfaces = device.InterfaceCount;

                    logger.info("usb device has {0} interfaces", interfaces);
                    for (int i = 0; i < interfaces; i++)
                    {
                        logger.info("interface {0} has {1} endpoints", i, device.GetInterface(i).EndpointCount);
                    }

                    UsbDeviceConnection udc = manager.OpenDevice(device);
                    logger.info("device has {0} configurations", device.ConfigurationCount);
                    if (udc != null)
                    {
                        logger.info("successfully opened device");
                        USBSpectrometer usbSpectrometer = new USBSpectrometer(udc, device);
                        spec = usbSpectrometer;

                        bool ok = await (spec as USBSpectrometer).initAsync();
                        if (ok)
                        {
                            logger.debug("invoking new connection");
                        }
                        logger.debug("init complete setting instance and paired");
                        USBSpectrometer.setInstance(usbSpectrometer);
                        USBViewDevice.paired = true;
                        Settings.getInstance().spec = spec;
                        return ok;
                    }
                    else
                    {
                        logger.info("failed to open device");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    logger.error("USB connect failed with exception {0}", ex.Message);
                    return false;
                }

            }
            else
            {
                logger.info("already initialized as usb spec, reconnecting");

                try
                {
                    if ((spec as USBSpectrometer).paired)
                        spec.disconnect();
                    else
                    {
                        (spec as USBSpectrometer).connect();
                        return await (spec as USBSpectrometer).initAsync();
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    logger.error("USB toggle failed with exception {0}", ex.Message);
                    return false;
                }
            }
        }


    }
}
