using EnlightenMAUI.Models;
using EnlightenMAUI.ViewModels;
using LibUsbDotNet;
using EnlightenMAUI.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace EnlightenMAUI.Platforms
{
    public class USBWrapper
    {
        public int vid;
        public int pid;

        public const byte HOST_TO_DEVICE = 0x40;
        public const byte DEVICE_TO_HOST = 0xc0;
        Dictionary<Opcodes, byte> cmd = OpcodeHelper.getInstance().getDict();
        HashSet<Opcodes> armInvertedRetvals = OpcodeHelper.getInstance().getArmInvertedRetvals();

        public void disconnect()
        {

        }

        public bool connect()
        {
            return false;
        }

        public async Task<int> bulkTransfer(byte[] spectrumBuff, int timeout)
        {
            return 0;
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
            return null;
        }

        internal byte[] getCmd(Opcodes opcode, int len, ushort wIndex = 0, int fullLen = 0, bool minRead = true)
        {
            return null;
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
            return null;
        }
        internal byte[] getCmd2(Opcodes opcode, int len, ushort wIndex = 0, int fakeBufferLengthARM = 0, bool minRead = true)
        {
            return null;
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
            return false;
        }
        internal bool sendCmd(Opcodes opcode, ushort wValue = 0, ushort wIndex = 0, byte[] buf = null, bool minRead = true)
        {
            return false;
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
            return false;
        }
        internal bool sendCmd2(Opcodes opcode, ushort wIndex = 0, byte[] buf = null, bool minRead = true)
        {
            return false;
        }

    }

    internal class PlatformUSB
    {
        internal static async Task<List<USBViewDevice>> doUSBScanAsync()
        {
            Logger logger = Logger.getInstance();
            List<USBViewDevice> usbDeviceList = new List<USBViewDevice>();

            return usbDeviceList;
        }

        internal static async Task<bool> doConnectOrDisconnectUSBAsync(Spectrometer spec, Spectrometer.ConnectionProgressNotification del)
        {
            return false;
        }

    }
}
