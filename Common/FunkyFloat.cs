using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Common
{
    public class FunkyFloat
    {
        /// <summary>
        /// convert a standard IEEE float into the MSB-LSB UInt16 used within the spectrometer for gain control
        /// </summary>
        /// <param name="f">single-precision IEEE float</param>
        /// <returns>a UInt16 in which the MSB is a standard 8-bit byte and the LSB represents 8 bits of decreasing fractional precision</returns>
        public static ushort fromFloat(float f)
        {
            if (f < 0 || f >= 256)
            {
                Logger.getInstance().error("FunkyFloat: input float out-of-range: {0}", f);
                return 0;
            }

            byte msb = (byte)Math.Floor(f);
            double frac = f - Math.Floor(f);
            byte lsb = 0;

            // iterate RIGHTWARDS from the decimal point, in DECREASING significance
            // (traverse the LSB in order --> 0123 4567)
            for (int bit = 0; bit < 8; bit++)
            {
                double placeValue = Math.Pow(2, -1 - bit);
                if (frac >= placeValue)
                {
                    byte mask = (byte)(1 << (7 - bit));
                    lsb |= mask;
                    frac -= placeValue;
                }
            }

            return (ushort)((msb << 8) | lsb);
        }

        /// <summary>
        /// convert the MSB-LSB UInt16 used within the spectrometer for gain control into a standard single-precision IEEE float
        /// </summary>
        /// <param name="n">UInt16 in which the MSB is a standard 8-bit byte and the LSB represents 8 bits of decreasing fractional precision</param>
        /// <returns>single-precision IEEE float</returns>
        public static float toFloat(ushort n)
        {
            byte msb = (byte)((n >> 8) & 0xff);
            byte lsb = (byte)(n & 0xff);
            double frac = 0;

            // iterate RIGHTWARDS from the decimal point, in DECREASING significance
            // (traverse the LSB in order --> 0123 4567)
            for (int bit = 0; bit < 8; bit++)
            {
                byte mask = (byte)(1 << (7 - bit));
                if ((lsb & mask) != 0)
                {
                    double placeValue = Math.Pow(2, -1 - bit);
                    frac += placeValue;
                }
            }
            return (float)(msb + frac);
        }
    }
}
