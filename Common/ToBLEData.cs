using System;

namespace EnlightenMAUI.Common;

public class ToBLEData
{
    public static byte[] convert(bool flag)
    {
        var data = new byte[1];
        data[0] = (byte)(flag ? 1 : 0);
        return data;
    }

    public static byte[] convert(byte byte1, byte byte2)
    {
        UInt16 u1 = (UInt16)byte1;
        UInt16 u2 = (UInt16)byte2;
        UInt16 word = (UInt16)((u1 << 8) | u2);
        return convert(word, len: 2);
    }

    public static byte[] convert(UInt64 value, int len)
    {
        byte[] data = new byte[len];

        // generate MSB-to-LSB order, but only least-significant "len" bytes
        // (e.g. (value 0xaabbccdd, len 2) => { 0xcc, 0xdd })
        UInt64 v = value;
        for (int i = 0; i < len; i++)
        {
            int pos = len - (i+1);
            data[pos] = (byte)(v & 0xff);
            v >>= 8;
        }
        return data;
    }

    public static UInt64 toUInt64(byte[] data)
    {
        UInt64 v = 0;
        for (int i = 0; i < data.Length; i++)
        {
            v |= ((UInt64)data[data.Length - 1 - i] << (8 * i));
        }

        return v;
    }


}
