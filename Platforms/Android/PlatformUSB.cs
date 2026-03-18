using Android.Content;
using Android.Hardware.Usb;
using EnlightenMAUI.Models;
using EnlightenMAUI.Common;
using Microsoft.Maui;
using Microsoft.Maui.Controls.PlatformConfiguration;
using System;
using System.Collections.Generic;
using System.Text;
using static Android.Widget.GridLayout;
using AndrApp = Android.App;
using EnlightenMAUI.ViewModels;

namespace EnlightenMAUI.Platforms
{
    public  class USBWrapper
    {
        Logger logger = Logger.getInstance();
        static UsbDeviceConnection udc;
        static UsbDevice acc;
        public int vid;
        public int pid;

        public const byte HOST_TO_DEVICE = 0x40;
        public const byte DEVICE_TO_HOST = 0xc0;
        Dictionary<Opcodes, byte> cmd = OpcodeHelper.getInstance().getDict();
        HashSet<Opcodes> armInvertedRetvals = OpcodeHelper.getInstance().getArmInvertedRetvals();

        public USBWrapper(UsbDeviceConnection udcI, UsbDevice accI)
        {
            udc = udcI;
            acc = accI;
            vid = accI.VendorId; 
            pid = accI.ProductId;

        }

        public void disconnect()
        {
            udc.ReleaseInterface(acc.GetInterface(0));
            udc.Close();
        }

        public bool connect()
        {
            bool ok = udc.SetConfiguration(acc.GetConfiguration(0));
            if (ok)
            {
                logger.info("successfully set configuration");
                ok = udc.ClaimInterface(acc.GetInterface(0), false);
                if (ok)
                {
                    logger.info("successfully claimed interface");
                    return true;
                }
            }

            return false;
        }

        public async Task<int> bulkTransfer(byte[] spectrumBuff, int timeout)
        {
            logger.debug("buffer init with {0} in pix 0", spectrumBuff[0]);
            int result = await udc.BulkTransferAsync(acc.GetInterface(0).GetEndpoint(0), spectrumBuff, spectrumBuff.Length, timeout);
            logger.debug("buffer transfered with {0} in pix 0", spectrumBuff[0]);
            return result;
        }


        /// <summary>
        /// Execute a request-response control transaction using the given opcode.
        /// </summary>
        /// <param name="opcode">the opcode of the desired request</param>
        /// <param name="len">the number of needed return bytes</param>
        /// <param name="wIndex">an optional numeric argument used by some opcodes</param>
        /// <param name="fullLen">the actual number of expected return bytes (not all needed)</param>
        /// <remarks>not sure fullLen is actually required...testing</remarks>
        /// <returns>the array of returned bytes (null on error)</returns>
        internal async Task<byte[]> getCmdAsync(Opcodes opcode, int len, ushort wIndex = 0, int fullLen = 0, bool minRead = true)
        {

            int bytesToRead = Math.Max(len, fullLen);
            if (minRead) // ARM should always read at least 8 bytes
                bytesToRead = Math.Min(8, bytesToRead);
            byte[] buf = new byte[bytesToRead];

            logger.debug("about to send getCmd...");

            int okI = await udc.ControlTransferAsync((UsbAddressing)DEVICE_TO_HOST, cmd[opcode], 0, wIndex, buf, bytesToRead, 100);

            if (logger.debugEnabled())
                logger.hexdump(buf, String.Format("getCmd: {0} (0x{1:x2}) index 0x{2:x4} ->", opcode.ToString(), cmd[opcode], wIndex));

            // extract just the bytes we really needed
            return await Task.Run(() => Util.truncateArray(buf, len));
        }

        internal byte[] getCmd(Opcodes opcode, int len, ushort wIndex = 0, int fullLen = 0, bool minRead = true)
        {
            int bytesToRead = Math.Max(len, fullLen);
            if (minRead) // ARM should always read at least 8 bytes
                bytesToRead = Math.Min(8, bytesToRead);
            byte[] buf = new byte[bytesToRead];

            logger.debug("about to send getCmd...");

            int okI = udc.ControlTransfer((UsbAddressing)DEVICE_TO_HOST, cmd[opcode], 0, wIndex, buf, bytesToRead, 100);

            if (logger.debugEnabled())
                logger.hexdump(buf, String.Format("getCmd: {0} (0x{1:x2}) index 0x{2:x4} ->", opcode.ToString(), cmd[opcode], wIndex));

            // extract just the bytes we really needed
            return Util.truncateArray(buf, len);
        }

        /// <summary>
        /// Execute a request-response transfer with a "second-tier" request.
        /// </summary>
        /// <param name="opcode">the wValue to send along with the "second-tier" command</param>
        /// <param name="len">how many bytes of response are expected</param>
        /// <returns>array of returned bytes (null on error)</returns>
        /// 
        internal async Task<byte[]> getCmd2Async(Opcodes opcode, int len, ushort wIndex = 0, int fakeBufferLengthARM = 0, bool minRead = true)
        {
            int bytesToRead = len;
            if (minRead)
                bytesToRead = Math.Max(bytesToRead, fakeBufferLengthARM);

            byte[] buf = new byte[bytesToRead];

            bool expectedSuccessResult = true;
            if (minRead && armInvertedRetvals.Contains(opcode))
                expectedSuccessResult = !expectedSuccessResult;

            bool result = false;

            int okI = await udc.ControlTransferAsync((UsbAddressing)DEVICE_TO_HOST, cmd[Opcodes.SECOND_TIER_COMMAND], cmd[opcode], wIndex, buf, bytesToRead, 100);

            if (result != expectedSuccessResult && okI < len)
            {
                logger.error("getCmd2: failed to get SECOND_TIER_COMMAND {0} (0x{1:x4}) via DEVICE_TO_HOST ({2} of {3} bytes read, expected {4} got {5})",
                    opcode.ToString(), cmd[opcode], okI, len, expectedSuccessResult, result);
                logger.hexdump(buf, $"{opcode} result");
                //return null;
            }

            logger.hexdump(buf, String.Format("getCmd2: {0} (0x{1:x2}) index 0x{2:x4} (result {3}, expected {4}) ->",
                    opcode.ToString(), cmd[opcode], wIndex, result, expectedSuccessResult));

            // extract just the bytes we really needed
            return Util.truncateArray(buf, len);
        }
        internal byte[] getCmd2(Opcodes opcode, int len, ushort wIndex = 0, int fakeBufferLengthARM = 0, bool minRead = true)
        {
            int bytesToRead = len;
            if (minRead)
                bytesToRead = Math.Max(bytesToRead, fakeBufferLengthARM);

            byte[] buf = new byte[bytesToRead];

            bool expectedSuccessResult = true;
            if (minRead && armInvertedRetvals.Contains(opcode))
                expectedSuccessResult = !expectedSuccessResult;

            bool result = false;

            int okI = udc.ControlTransfer((UsbAddressing)DEVICE_TO_HOST, cmd[Opcodes.SECOND_TIER_COMMAND], cmd[opcode], wIndex, buf, bytesToRead, 100);

            if (result != expectedSuccessResult || okI < len)
            {
                logger.error("getCmd2: failed to get SECOND_TIER_COMMAND {0} (0x{1:x4}) via DEVICE_TO_HOST ({2} of {3} bytes read, expected {4} got {5})",
                    opcode.ToString(), cmd[opcode], okI, len, expectedSuccessResult, result);
                logger.hexdump(buf, $"{opcode} result");
                return null;
            }

            if (logger.debugEnabled())
                logger.hexdump(buf, String.Format("getCmd2: {0} (0x{1:x2}) index 0x{2:x4} (result {3}, expected {4}) ->",
                    opcode.ToString(), cmd[opcode], wIndex, result, expectedSuccessResult));

            // extract just the bytes we really needed
            return Util.truncateArray(buf, len);
        }

        /// <summary>
        /// send a single control transfer command (response not checked)
        /// </summary>
        /// <param name="opcode">the desired command</param>
        /// <param name="wValue">an optional secondary argument used by most commands</param>
        /// <param name="wIndex">an optional tertiary argument used by some commands</param>
        /// <param name="buf">a data buffer used by some commands</param>
        /// <returns>true on success, false on error</returns>
        /// <todo>should support return code checking...most cmd opcodes return a success/failure byte</todo>
        /// 
        internal async Task<bool> sendCmdAsync(Opcodes opcode, ushort wValue = 0, ushort wIndex = 0, byte[] buf = null, bool minRead = true)
        {
            if ((minRead) && (buf is null || buf.Length < 8))
                buf = new byte[8];

            ushort wLength = (ushort)((buf is null) ? 0 : buf.Length);

            bool? expectedSuccessResult = true;
            if (minRead)
            {
                if (opcode != Opcodes.SECOND_TIER_COMMAND)
                    expectedSuccessResult = armInvertedRetvals.Contains(opcode);
                else
                    expectedSuccessResult = null; // no easy way to know, as we don't pass wValue as enum (MZ: whut?)
            }

            int okI = await udc.ControlTransferAsync((UsbAddressing)HOST_TO_DEVICE, cmd[opcode], wValue, wIndex, buf, wLength, 100);

            if (opcode == Opcodes.ACQUIRE_SPECTRUM)
            {
                logger.info("sendCmd: failed to send {0} (0x{1:x2}) (wValue 0x{2:x4}, wIndex 0x{3:x4}, wLength 0x{4:x4}) (received {5}, expected {6})",
                    opcode.ToString(), cmd[opcode], wValue, wIndex, wLength, okI, expectedSuccessResult);
            }

            if (expectedSuccessResult != null && okI < 0)
            {
                logger.error("sendCmd: failed to send {0} (0x{1:x2}) (wValue 0x{2:x4}, wIndex 0x{3:x4}, wLength 0x{4:x4}) (received {5}, expected {6})",
                    opcode.ToString(), cmd[opcode], wValue, wIndex, wLength, okI, expectedSuccessResult);
                return false;
            }

            return true;
        }
        internal bool sendCmd(Opcodes opcode, ushort wValue = 0, ushort wIndex = 0, byte[] buf = null, bool minRead = true)
        {
            if ((minRead) && (buf is null || buf.Length < 8))
                buf = new byte[8];

            ushort wLength = (ushort)((buf is null) ? 0 : buf.Length);

            bool? expectedSuccessResult = true;
            if (minRead)
            {
                if (opcode != Opcodes.SECOND_TIER_COMMAND)
                    expectedSuccessResult = armInvertedRetvals.Contains(opcode);
                else
                    expectedSuccessResult = null; // no easy way to know, as we don't pass wValue as enum (MZ: whut?)
            }

            int okI = udc.ControlTransfer((UsbAddressing)HOST_TO_DEVICE, cmd[opcode], wValue, wIndex, buf, wLength, 100);

            if (expectedSuccessResult != null && okI < 0)
            {
                logger.error("sendCmd: failed to send {0} (0x{1:x2}) (wValue 0x{2:x4}, wIndex 0x{3:x4}, wLength 0x{4:x4}) (received {5}, expected {6})",
                    opcode.ToString(), cmd[opcode], wValue, wIndex, wLength, okI, expectedSuccessResult);
                return false;
            }

            return true;
        }

        /// <summary>
        /// send a single 2nd-tier control transfer command (response not checked)
        /// </summary>
        /// <param name="opcode">the desired command</param>
        /// <param name="wIndex">an optional secondary argument used by some 2nd-tier commands</param>
        /// <param name="buf">a data buffer used by some commands</param>
        /// <returns>true on success, false on error</returns>
        /// <todo>should support return code checking...most cmd opcodes return a success/failure byte</todo>
        internal async Task<bool> sendCmd2Async(Opcodes opcode, ushort wIndex = 0, byte[] buf = null, bool minRead = true)
        {
            if ((minRead) && (buf is null || buf.Length < 8))
                buf = new byte[8];

            ushort wLength = (ushort)((buf is null) ? 0 : buf.Length);

            int okI = await udc.ControlTransferAsync((UsbAddressing)HOST_TO_DEVICE, cmd[Opcodes.SECOND_TIER_COMMAND], cmd[opcode], wIndex, buf, wLength, 100);

            return true;
        }
        internal bool sendCmd2(Opcodes opcode, ushort wIndex = 0, byte[] buf = null, bool minRead = true)
        {
            if ((minRead) && (buf is null || buf.Length < 8))
                buf = new byte[8];

            ushort wLength = (ushort)((buf is null) ? 0 : buf.Length);

            int okI = udc.ControlTransfer((UsbAddressing)HOST_TO_DEVICE, cmd[Opcodes.SECOND_TIER_COMMAND], cmd[opcode], wIndex, buf, wLength, 100);

            return true;
        }

    }

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
                        logger.info("usb device matches vid, adding to list");
                        USBViewDevice uvd = new USBViewDevice(acc.DeviceName, acc.VendorId.ToString("x4"), acc.ProductId.ToString("x4"));
                        usbDeviceList.Add(uvd);

                        usbIntent = AndrApp.PendingIntent.GetBroadcast(con, 0, new Intent(ACTION_USB_PERMISSION), AndrApp.PendingIntentFlags.Immutable);

                        //LibUsbDotNet.UsbDevice usbDevice = LibUsbDotNet.UsbDevice.OpenUsbDevice(d => d.Pid == acc.ProductId);
                        logger.info("requesting permission to access USB device");
                        manager.RequestPermission(acc, usbIntent);

                    }

                }
            }
            catch (Exception ex)
            {
                logger.info("USB grab failed with error {0}", ex.Message);
            }

            logger.info("returning {0} USB devices", usbDeviceList.Count);
            return usbDeviceList;
        }


        internal static async Task<bool> doConnectOrDisconnectUSBAsync(Spectrometer spec, BluetoothViewModel bvm)
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
                        USBWrapper passthrough = new USBWrapper(udc, device);
                        USBSpectrometer usbSpectrometer = new USBSpectrometer(passthrough);
                        spec = usbSpectrometer;
                        spec.showConnectionProgress += bvm.showSpectrometerConnectionProgress;
                        logger.info("about to init device");
                        bool ok = await (spec as USBSpectrometer).initAsync();
                        if (ok)
                        {
                            logger.debug("invoking new connection");
                        }
                        logger.debug("init complete setting instance and paired");
                        USBSpectrometer.setInstance(usbSpectrometer);
                        USBViewDevice.paired = true;
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
