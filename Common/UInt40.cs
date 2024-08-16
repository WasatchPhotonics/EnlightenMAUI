﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Common
{
    /// <summary>
    /// 40-bit unsigned value, used for many of the laser functions.
    /// </summary>
    [ComVisible(true)]
    [Guid("5BC91277-C373-44F3-8C84-77964365F627")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IUInt40
    {
        ushort LSW { get; }
        ushort MidW { get; }
        byte MSB { get; }
        byte[] buf { get; }
    }

    /// <summary>
    /// 40-bit unsigned value, used for many of the laser functions.
    /// </summary>

    [ComVisible(true)]
    [Guid("E78373A6-98A0-4F79-BEFC-60ED44FE5A87")]
    [ProgId("WasatchNET.UInt40")]
    [ClassInterface(ClassInterfaceType.None)]
    class UInt40 : IUInt40
    {
        public ushort LSW { get; private set; }
        public ushort MidW { get; private set; }
        public byte MSB { get; private set; }
        public byte[] buf { get; private set; }

        public UInt40(UInt64 value)
        {
            // if we have to do this twice, make a UInt40 class
            const UInt64 max = (((UInt64)1) << 40) - 1;
            if (value > max)
                throw new ArgumentOutOfRangeException();

            LSW = (ushort)(value & 0xffff);         // least-significant word
            MidW = (ushort)((value >> 16) & 0xffff); // next-least significant word
            MSB = (byte)(value >> 32);              // most-significant byte

            buf = new byte[1];
            buf[0] = MSB;
        }
    }
}
