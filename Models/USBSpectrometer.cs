using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Models
{
    public class USBSpectrometer : Spectrometer
    {
        public override void disconnect()
        {

        }
        public override void reset()
        {

        }

        protected override async Task<List<byte[]>> readEEPROMAsync()
        {
            //throw new NotImplementedException();
            return new List<byte[]> { };
        }

        protected override async Task<bool> updateBatteryAsync()
        {
            return true;
            //throw new NotImplementedException();
        }

        public override async Task<bool> takeOneAveragedAsync()
        {
            return true;
            //throw new NotImplementedException();
        }

        protected override async Task<double[]> takeOneAsync(bool disableLaserAfterFirstPacket)
        {
            return [];
            //throw new NotImplementedException();
        }



    }
}
