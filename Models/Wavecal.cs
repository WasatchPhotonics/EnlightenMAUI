using System;
using EnlightenMAUI.Common;
using Telerik.Windows.Documents.Spreadsheet.Expressions.Functions;

namespace EnlightenMAUI.Models
{
    public class Wavecal
    {
        public const int defaultCoeffLength = 5;
        public Wavecal(uint pixels)
        {
            _pixels = pixels;

            pixelAxis = new double[pixels];
            for (int i = 0; i < pixels; i++)
                pixelAxis[i] = i;

            //coeffs = new float[] { 0, 1, 0, 0 };
        }

        public virtual uint pixels
        {
            get { return _pixels; }
        }

        uint _pixels;

        public double[] pixelAxis { get; protected set; }

        public virtual float[] coeffs
        {
            get { return _wavecalCoeffs; }
            set 
            {
                float[] temp = new float[defaultCoeffLength];
                Array.Copy(value, temp, value.Length);

                _wavecalCoeffs = temp;
                recalculate();
            }
        }

        float[] _wavecalCoeffs;

        public virtual double[] wavelengths
        {
            get
            {
                return _wavelengths;
            }
        }

        double[] _wavelengths;

        public virtual double[] wavenumbers
        {
            get { return _wavenumbers; }
        }

        double[] _wavenumbers;

        public virtual double excitationNM
        {
            get { return _excitationWavelengthNM; }
            set 
            {
                _excitationWavelengthNM = (float)value;
                recalculate();
            }
        }

        double _excitationWavelengthNM;

        protected void recalculate()
        {
            _wavelengths = Util.generateWavelengths(pixels, coeffs);
            if (excitationNM > 0)
                _wavenumbers = Util.wavelengthsToWavenumbers(excitationNM, wavelengths);
        }

        public double wavelengthToWavenumber(double wavelength)
        {
            return Util.wavelengthToWavenumber(excitationNM, wavelength);
        }

        public double wavenumberToWavelength(double wavenumber)
        {
            return Util.wavenumberToWavelength(excitationNM, wavenumber);
        }

        public double getWavelength(double interPixel)
        {
            double wavelength = coeffs[0]
                              + coeffs[1] * interPixel
                              + coeffs[2] * interPixel * interPixel
                              + coeffs[3] * interPixel * interPixel * interPixel
                              + coeffs[4] * interPixel * interPixel * interPixel * interPixel;

            return wavelength;
        }

        public double getWavelength(int pixel)
        {
            return (pixel < 0) ? wavelengths[pixels + pixel] : wavelengths[pixel];
        }

        public double getWavenumber(int pixel)
        {
            return (pixel < 0) ? wavenumbers[pixels + pixel] : wavenumbers[pixel];
        }

        public virtual  double getWavenumber(double interPixel)
        {
            double wavelength = coeffs[0]
                              + coeffs[1] * interPixel
                              + coeffs[2] * interPixel * interPixel
                              + coeffs[3] * interPixel * interPixel * interPixel
                              + coeffs[4] * interPixel * interPixel * interPixel * interPixel;

            double wavenumber = ((1 / excitationNM) - (1 / wavelength)) * Math.Pow(10, 7);

            return wavenumber;
        }

        public double[] getWavelengths()
        {
            return wavelengths;
        }

        public int getPixelFromWavelength(double nm)
        {
            if (wavelengths == null)
                return -1;
            int pixel = Array.BinarySearch(wavelengths, nm);
            if (pixel < 0)
                return ~pixel;
            else if (pixel >= wavelengths.Length)
                return wavelengths.Length - 1;
            else
                return pixel;
        }

        public int getPixelFromWavenumber(double cm)
        {
            if (wavenumbers == null)
                return -1;
            int pixel = Array.BinarySearch(wavenumbers, cm);
            if (pixel < 0)
                return ~pixel;
            else if (pixel >= wavenumbers.Length)
                return wavenumbers.Length - 1;
            else
                return pixel;
        }

        public double getInterpolatedCountFromWavelength(double nm, double[] counts)
        {
            double partialPixel = getInterpolatedPixelFromWavelength(nm);
            if (partialPixel == -1)
                return -1;

            double pctLow = Math.Ceiling(partialPixel) - partialPixel;
            double pctHigh = 1 - pctLow;

            return pctLow * counts[(int)Math.Floor(partialPixel)] + pctHigh * counts[(int)Math.Ceiling(partialPixel)];
        }

        public double getInterpolatedCountFromWavenumber(double cm, double[] counts)
        {
            double partialPixel = getInterpolatedPixelFromWavenumber(cm);
            if (partialPixel == -1)
                return -1;

            double pctLow = Math.Ceiling(partialPixel) - partialPixel;
            double pctHigh = 1 - pctLow;

            return pctLow * counts[(int)Math.Floor(partialPixel)] + pctHigh * counts[(int)Math.Ceiling(partialPixel)];
        }


        public double getInterpolatedPixelFromWavelength(double nm)
        {
            if (wavelengths == null)
                return -1;
            int pixel = Array.BinarySearch(wavelengths, nm);
            if (pixel < 0)
            {
                pixel = ~pixel;
                if (pixel == 0 || pixel == wavelengths.Length - 1)
                    return pixel;
                if (pixel > (wavelengths.Length - 1))
                    return wavelengths.Length - 1;

                double pctLeft = (nm - wavelengths[pixel - 1]) / (wavelengths[pixel] - wavelengths[pixel - 1]);
                return pixel - 1 + pctLeft;
            }
            else if (pixel >= wavelengths.Length)
                return wavelengths.Length - 1;
            else
                return pixel;
        }

        public double getInterpolatedPixelFromWavenumber(double cm)
        {
            if (wavenumbers == null)
                return -1;
            int pixel = Array.BinarySearch(wavenumbers, cm);
            if (pixel < 0)
            {
                pixel = ~pixel;
                if (pixel == 0)
                    return pixel;
                if (pixel >= wavenumbers.Length)
                    return wavenumbers.Length - 1;
                double pctLeft = (cm - wavenumbers[pixel - 1]) / (wavenumbers[pixel] - wavenumbers[pixel - 1]);
                return pixel - 1 + pctLeft;
            }
            else if (pixel >= wavenumbers.Length)
                return wavenumbers.Length - 1;
            else
                return pixel;
        }

        public double getCoeff(int n)
        {
            if (coeffs != null && n < coeffs.Length)
                return coeffs[n];
            return 0;
        }

        public virtual bool looksValid()
        {
            if (coeffs[0] == 0 && coeffs[2] == 0 && coeffs[3] == 0 && coeffs[4] == 0 && (coeffs[1] == 0 || coeffs[1] == 1))
                return false;
            if (wavelengths == null)
                return false;
            if (wavelengths.Length != pixels)
                return false;
            if (wavelengths.Length < 2)
                return false;
            if (wavelengths[0] <= 0)
                return false;

            return true;
        }

        public double getRangeNM() { return getWavelength(-1) - getWavelength(0); }
        public double getRangeCM() { return getWavenumber(-1) - getWavenumber(0); }
        public double getPixelResolutionNM(int pixels) { return getRangeNM() / pixels; }
        public double getPixelResolutionCM(int pixels) { return getRangeCM() / pixels; }

        public Measurement crossMapIntensityPixel(Wavecal otherWC, Measurement mToMap)
        {
            Measurement m = mToMap.copy();
            
            m.raw = new double[pixels];
            m.dark = new double[pixels];
            m.reference = new double[pixels];

            double scale = mToMap.pixels / pixels;

            if (scale == 1.0)
            {
                m.raw = mToMap.raw;
                m.dark = mToMap.dark;
                m.reference = mToMap.reference;
            }
            else if (scale < 1.0)
            {
                scale = 1 / scale;

                for (int i = 0; i < pixels; ++i)
                {
                    double sample = i / scale;
                    int block = System.Convert.ToInt32(Math.Floor(sample));

                    m.raw[i] = mToMap.raw[block];
                    m.dark[i] = mToMap.dark[block];
                    m.reference[i] = mToMap.reference[block];
                }
            }
            else
            {
                for (int i = 0; i < pixels; ++i)
                {
                    double sample = i * scale;
                    int blockStart = System.Convert.ToInt32(Math.Floor(sample));
                    double rawsum = 0;
                    double darksum = 0;
                    double refsum = 0;

                    for (int j = 0; j < scale; ++j)
                    {
                        rawsum += mToMap.raw[blockStart + j];
                        darksum += mToMap.dark[blockStart + j];
                        refsum += mToMap.reference[blockStart + j];
                    }

                    m.raw[i] = rawsum / scale;
                    m.dark[i] = darksum / scale;
                    m.reference[i] = refsum / scale;
                }
            }

            return m;
        }

        public Measurement crossMapIntensityWavelength(Wavecal otherWC, Measurement mToMap)
        {
            Measurement m = mToMap.copy();

            m.raw = new double[pixels];
            if (mToMap.dark != null)
                m.dark = new double[pixels];
            if (mToMap.reference != null)
                m.reference = new double[pixels];

            //foreach (double wavelength)
            for (int i = 0; i < pixels; ++i)
            {
                double mappedPixel = otherWC.getInterpolatedPixelFromWavelength(getWavelength(i));
                int first = System.Convert.ToInt32(Math.Floor(mappedPixel));
                int second = System.Convert.ToInt32(Math.Ceiling(mappedPixel));
                double pctFirst = 1 - (mappedPixel - first);
                m.raw[i] = Util.interpolate(mToMap.raw[first], mToMap.raw[second], pctFirst);
                if (mToMap.dark != null)
                    m.dark[i] = Util.interpolate(mToMap.dark[first], mToMap.dark[second], pctFirst);
                if (mToMap.reference != null)
                    m.reference[i] = Util.interpolate(mToMap.reference[first], mToMap.reference[second], pctFirst);
            }

            m.raw = m.raw;

            return m;
        }

        public Measurement crossMapIntensityWavenumber(Wavecal otherWC, Measurement mToMap)
        {
            Measurement m = mToMap.copy();

            m.raw = new double[pixels];
            if (mToMap.dark != null)
                m.dark = new double[pixels];
            if (mToMap.reference != null)
                m.reference = new double[pixels];

            //foreach (double wavelength)
            for (int i = 0; i < pixels; ++i)
            {
                double mappedPixel = otherWC.getInterpolatedPixelFromWavenumber(getWavenumber(i));
                int first = System.Convert.ToInt32(Math.Floor(mappedPixel));
                int second = System.Convert.ToInt32(Math.Ceiling(mappedPixel));
                double pctFirst = 1 - (mappedPixel - first);

                 m.raw[i] = Util.interpolate(mToMap.raw[first], mToMap.raw[second], pctFirst);
                if (mToMap.dark != null)
                    m.dark[i] = Util.interpolate(mToMap.dark[first], mToMap.dark[second], pctFirst);
                if (mToMap.reference != null)
                    m.reference[i] = Util.interpolate(mToMap.reference[first], mToMap.reference[second], pctFirst);
                
            }

            m.raw = m.raw;

            return m;
        }

        public Measurement crossMapWavenumberData(double[] otherWavenumbers, double[] intensities)
        {
            Measurement m = new Measurement();
            m.raw = new double[wavenumbers.Length];

            for (int i = 0; i < pixels; ++i)
            {
                while (wavenumbers[i] < otherWavenumbers[0])
                {
                    m.raw[i] = intensities[0];
                    ++i;
                }

                int index = Array.BinarySearch(otherWavenumbers, wavenumbers[i]);
                if (index < 0)
                    index = ~index;

                //Logger.getInstance().info("mapping new pixel {0} at new wavenumber {1:f2} to pixel {2}", i, wavenumbers[i], index);

                if (index < intensities.Length)
                {
                    int first = index - 1;
                    int second = index;

                    double pixelSize = otherWavenumbers[second] - otherWavenumbers[first];
                    double pctFirst = (pixelSize - (otherWavenumbers[second] - wavenumbers[i])) / pixelSize;

                    //Logger.getInstance().info("interpolating at {0:f3} pct {1:f2} vs. {2:f2}", pctFirst, otherWavenumbers[first], otherWavenumbers[second]);
                    m.raw[i] = Util.interpolate(intensities[first], intensities[second], pctFirst);
                }
                else
                {
                    //Logger.getInstance().info("interpolating beyond end as last pixel");

                    m.raw[i] = intensities.Last();
                }
            }

            m.postProcess();
            Logger.getInstance().info("returning interpolated sample with pixel 1000 = {0}", m.processed[1000]);
            return m;
        }

    }
}
