using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Models
{
    internal class Spectrum
    {
        public List<double> intensities;
        public List<double> wavelengths;
        public List<double> wavenumbers;

        public string label = "unknown";

        Logger logger = Logger.getInstance();

        public Spectrum(string pathname)
        {
            label = pathname;
            logger.debug($"instantiating Spectrum from {pathname}");
        }

        public void interpolate(double startWavenumber, double endWavenumber, double incr=1.0)
        {
            logger.debug($"interpolating Spectrum of {intensities.Count} pixels from ({wavenumbers.First():f2}, {wavenumbers.Last():f2}) to ({startWavenumber:f2}, {endWavenumber:f2}, step {incr:f2})");

            var interpolator = MathNet.Numerics.Interpolate.Linear(wavenumbers, intensities);

            List<double> interpX = new List<double>();
            List<double> interpY = new List<double>();

            double x = startWavenumber;
            int steps = 0;
            while (x <= endWavenumber)
            {
                interpX.Add(x);
                interpY.Add(interpolator.Interpolate(x));

                x = startWavenumber + steps * incr; // more accurate than cumulative addition
                steps++;
            }

            intensities = interpY;
            wavenumbers = interpX;

            logger.debug($"interpolated Spectrum to {intensities.Count} pixels from ({wavenumbers.First():f2}, {wavenumbers.Last():f2})");
        }
    }
}
