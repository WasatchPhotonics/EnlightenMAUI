using Android.Net.Eap;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Models
{
    public class PeakOptimizationConfig
    {
        public uint targetCounts { get; set; } = 35000;
        public uint targetCountTol { get; set; } = 3000;
        public uint targetCountThreshold
        {
            get { return targetCountTol; }
            set { targetCountTol = value; }
        }
        public uint defaultIntegrationTimeMS { get; set; } = 30;
        public uint maxSaneIntegrationTimeMS { get; set; } = 20000;
        public uint maxIterations { get; set; } = 50;
        public uint maxConsecutiveRounding { get; set; } = 3;
        public bool strictCounts { get; set; } = false;
        public bool fastFail { get; set; } = true;
        public double maxSaneMultiplier { get; set; } = 50;
        public int? minSanePeakCounts { get; set; } = null;
        public double approximationMultiplier { get; set; } = 0.5;
    }

    public class PeakFindingConfig
    {
        // DEPRECATED PARAMETER: now aliases for peakSearchHalfWidth
        public uint reacquireWindowPixels
        {
            get { return peakSearchHalfWidth; }
            set { peakSearchHalfWidth = value; }
        }

        /// <summary>
        /// This is strictly in pixel-space, and is used by PeakFinder when re-
        /// generating peak metrics (FWHM etc), presumably against a fresh 
        /// spectrum than originally used to snap a peak, and so must allow the 
        /// peak to "drift" a bit on the detector. PeakFinder fundamentally 
        /// returns SpectrumPeaks by pixel index (regardless of the unit used for
        /// xAxis or FWHM calculations), so it makes sense for this to be in 
        /// pixels.
        /// 
        /// PeakFinder looks for a given peak this many pixels on either side of the
        /// provided search pixel
        /// </summary>
        ///
        /// <remarks>
        /// This is NOT the same as PeakOptimization.peakSearchHalfWidth, which
        /// is used for location of emission pixels on an UNCALIBRATED spectrometer,
        /// and which will typically be a LARGER window because an unknown "shift"
        /// or "offset" may be included.  In contrast, this "reacquire" window can
        /// be tighter because the "horizOffset" has presumably already been tracked
        /// in the SnappedPeak, and therefore all we need support is measurement-
        /// to-measurement drift due to temperature.
        /// </remarks>
        public uint peakSearchHalfWidth { get; set; } = 5;

        /// <summary>
        /// A slightly odd parameter, used when enumerating "all peaks." Essentially
        /// this is used to say "how many pixels apart can peak candidates be." So at
        /// 5 we could have peaks at pixels 0, 5, 10, 15, 20, etc. In practice, the highest
        /// pixels become candidates and "squash" surrounding pixels within this many pixels.
        /// 
        /// This can become problematic when we are looking for peaks within a tight section of
        /// dense peaks (such as Neon). In those cases, a lower number can help.
        /// 
        /// The oddness of the parameter is in the fact that it took over a function from a pervious
        /// parameter, which is "how many pixels do the counts need to drop consecutively
        /// for this to be considered a peak." As of now, that value is this parameter divided by 2.
        /// 
        /// So, at 5, the counts must drop for 3 consecutive pixels in both directions for a candidate
        /// to be considered a peak.
        /// 
        /// In general these two uses are negatively correlated, but it still may be reasonable to
        /// separate them sometime in the future.
        /// 
        /// </summary>
        public uint minIndicesBetweenPeaks { get; set; } = 5;

        /// <summary>
        /// This is used by SnappedPeaks when associating PeakFinder xCoord
        /// results with expected SourcePeakConfigs.  The unit is interpreted
        /// as wavelengths if using emission sources, and wavenumbers if using
        /// Raman sources.
        /// 
        /// Generally, the peak finding expects us to be within this many
        /// wavelengths/numbers, and if the peak is not close enough the
        /// algorithm will reject the match.
        /// 
        /// The default 5 can be problematic for raman peaks in units with 
        /// narrow wavelength bandwidths such as the 532.
        /// </summary>
        public double xCoordWindow { get; set; } = 10;


        /// <summary>
        /// This uses a 3-point parabolic approximation to interpolate peak 
        /// position in x and y.
        /// </summary>
        /// <see cref="Wasatch.Math/PeakFinding.cs" />
        public bool useParabolicApproximation { get; set; } = true;

        /// <summary>
        /// This is mostly used as a general test paramater to determine whether we need to
        /// collect darks for the test. This probably needs to be moved to the master TestConfig
        /// class
        /// </summary>
        public bool useDarkSubtraction { get; set; } = false;

        /// <summary>
        /// Parabolic approximation is not appropriate for larger slits (100+, sometimes 50) and
        /// simply picking the highest pixel can also lead to stochastic results. If this is turned
        /// on we use a "wide peak" method for finding the peak center. Our current implementation
        /// uses the average of the half maxes on either side to find the peak center.
        /// </summary>
        public bool widePeak { get; set; } = false;

        /// <summary>
        /// Whether or not to use our baseline subtraction for peak calculations. On by default,
        /// the only current option is a simple linear subtraction. The half width sets how far
        /// to look in both directions for a minimal value. These minimum are then used to define
        /// the line that we subract from the spectrum.
        /// 
        /// If left and right are set instead of the half width, we will perform an asymmetric
        /// search for minima, using the values provided. This is typically used for especially
        /// broad/complex raman peaks where throughput is of interest (e.g. aluminum, polystyrene)
        /// </summary>
        public bool useBaselineSubtraction { get; set; } = true;
        public uint? baselineHalfWidth { get; set; } = 25;
        public uint? leftBaselineWidth { get; set; }
        public uint? rightBaselineWidth { get; set; }

    }

    public class PeakInfo
    {
        public double wavelength;
        public double intensity;
        public double fwhm;
        public double fwtm;
        public double interpolatedPixel;
        public double peakHeight;
        public int startPixel;
        public int endPixel;

        public PeakInfo(double wavelength_in, double intensity_in, double fwhm_in, double ip, double ph = 0, int sp = 0, int ep = 0, double fwtm_in = 0)
        {
            wavelength = wavelength_in;
            intensity = intensity_in;
            fwhm = fwhm_in;
            interpolatedPixel = ip;
            peakHeight = ph;
            startPixel = sp;
            endPixel = ep;
            fwtm = fwtm_in;
        }

        public PeakInfo(PeakInfo rhs)
        {
            wavelength = rhs.wavelength;
            intensity = rhs.intensity;
            fwhm = rhs.fwhm;
        }
    }

    public class PeakFinder
    {
        PeakFindingConfig cfg;
        Logger logger = Logger.getInstance();

        public PeakFinder(PeakFindingConfig pfc)
        {
            cfg = pfc;
        }

        public PeakInfo findExpectedPeak(double[] wavelengths, double[] spectrum, uint pixel)
        {
            PeakInfo peak = null;

            if (wavelengths == null)
            {
                // Throw exception, because this is a software bug -- give us a 
                // callstack.
                //
                // This is normally called from updateFromMeasurement, which uses 
                // Wavecal to get the appropriate x-axis: wavelengths for emission
                // peaks, wavenumbers for Raman peaks.  And it uses WasatchNET.Spectrometer
                // to get the wavelength axis, so that should never be null.
                throw new Exception("findPeaks: wavelengths was null");
            }
            else if (spectrum == null)
            {
                // return an error, because this is most likely a hardware / measurement error 
                logger.error("findPeaks: spectrum was null");
                return peak;
            }
            else if (spectrum.Length != wavelengths.Length)
            {
                throw new Exception(string.Format("findPeaks: spectrum.Length != wavelengths.Length ({0} != {1})", spectrum.Length, wavelengths.Length));
            }
            //this one is weird! dunno what this is about -TS
            else if (spectrum.Length < 2)
            {
                logger.error("findPeaks: spectrum.Length was {0}", spectrum.Length);
                return peak;
            }

            SpectrumPeak p;
            if (cfg.baselineHalfWidth != null)
                p = PeakFinding.getExpectedPeak(wavelengths, spectrum, pixel, cfg.useParabolicApproximation, cfg.useBaselineSubtraction, (uint)cfg.baselineHalfWidth, cfg.widePeak);
            else
                p = PeakFinding.getExpectedPeak(wavelengths, spectrum, pixel, cfg.useParabolicApproximation, cfg.useBaselineSubtraction, 0, cfg.widePeak);


            int pixelResult = p.pixelNumber;

            // Centroid is problematic...tail must drop below 5% of peak 
            // height INCLUDING baseline, hard to achieve on closely-spaced 
            // peaks and doublets

            // Wavelength is...also problematic. It does not deal well with
            // any asymmetry in the peaks. This is a move to a pure pixel peak
            // assessment of location

            double wavelength = p.centerWavelength;

            if (p.interpolatedPeakXValue.HasValue && !cfg.widePeak)
                wavelength = p.interpolatedPeakXValue.Value;

            double intensity = p.intensityAboveBackground; //  spectrum[pixel]; // YOU ARE HERE 
            double fwhm = p.wavelengthFullWidthHalfMaximum;
            double fwtm = p.wavelengthFullWidthTenthMaximum;
            double interpPixel = p.interpolatedPixel;
            double peakHeight = p.peakHeight;
            int startPixel = p.startPixel;
            int endPixel = p.endPixel;

            peak = new PeakInfo(wavelength, intensity, fwhm, interpPixel, peakHeight, startPixel, endPixel, fwtm);

            return peak;
        }


        /// <summary>
        /// This function is intended to find "any and all" x-coordinates within
        /// the passed spectrum which might be peaks; it tends to have a lot of
        /// false positives.
        /// </summary>
        /// <param name="wavelengths"></param>
        /// <param name="spectrum"></param>
        /// <returns>null on error, can return empty set on no peaks found</returns>
        /// <remarks>
        /// Compare to locateExpectedPeaks(), which is optimized for the use-case
        /// where a specific set of dominant peaks are expected to be easily matched
        /// against the current wavecal.
        ///
        /// This function is currently called from SnappedPeaks.updateFromMeasurement,
        /// and nowhere else.
        /// </remarks>
        public SortedDictionary<int, PeakInfo> findPeaks(double[] wavelengths, double[] spectrum)
        {
            SortedDictionary<int, PeakInfo> peaks = null;

            if (wavelengths == null)
            {
                // Throw exception, because this is a software bug -- give us a 
                // callstack.
                //
                // This is normally called from updateFromMeasurement, which uses 
                // Wavecal to get the appropriate x-axis: wavelengths for emission
                // peaks, wavenumbers for Raman peaks.  And it uses WasatchNET.Spectrometer
                // to get the wavelength axis, so that should never be null.
                throw new Exception("findPeaks: wavelengths was null");
            }
            else if (spectrum == null)
            {
                // return an error, because this could be a hardware / measurement error 
                logger.error("findPeaks: spectrum was null");
                return peaks;
            }
            else if (spectrum.Length != wavelengths.Length)
            {
                throw new Exception(string.Format("findPeaks: spectrum.Length != wavelengths.Length ({0} != {1})", spectrum.Length, wavelengths.Length));
            }
            else if (spectrum.Length < 2)
            {
                logger.error("findPeaks: spectrum.Length was {0}", spectrum.Length);
                return peaks;
            }

            SpectrumPeak[] results = null;
            try
            {
                // YOU ARE HERE - background subtraction MUST occur within 
                // PeakFinding, because it is used to generate the FWHM which 
                // comes back to us in this SpectrumPeak[] array.  Realistically,
                // we should add an intensityAboveBaseline attribute to 
                // SpectrumPeak.

                if (cfg.baselineHalfWidth == null)
                    results = PeakFinding.getAllPeaks(wavelengths, spectrum, (int)cfg.minIndicesBetweenPeaks, 0, cfg.useParabolicApproximation, useBackgroundSubtraction: cfg.useBaselineSubtraction);
                else
                    results = PeakFinding.getAllPeaks(wavelengths, spectrum, (int)cfg.minIndicesBetweenPeaks, 0, cfg.useParabolicApproximation, useBackgroundSubtraction: cfg.useBaselineSubtraction, (uint)cfg.baselineHalfWidth);


            }
            catch (Exception ex)
            {
                logger.error("Caught exception calling WasatchMath.PeakFinding.getAllPeaks:");
                logger.error("  wavelengths = {0} values ({1}, {2})", wavelengths.Length, wavelengths[0], wavelengths[wavelengths.Length - 1]);
                logger.error("  spectrum = {0} values ({1}, {2})", spectrum.Length, spectrum[0], spectrum[spectrum.Length - 1]);
                logger.error("  minIndicesBetweenPeaks = {0}", (int)cfg.minIndicesBetweenPeaks);
                logger.error("  baseline = {0}", 0);
                logger.error("  useParabolicApproximation = {0}", cfg.useParabolicApproximation);
                logger.error("Exception = {0}", ex.Message);
                logger.error("Stack = {0}", ex.StackTrace);
                return peaks;
            }

            peaks = new SortedDictionary<int, PeakInfo>();
            foreach (SpectrumPeak peak in results)
            {
                int pixel = peak.pixelNumber;

                // Centroid is problematic...tail must drop below 5% of peak 
                // height INCLUDING baseline, hard to achieve on closely-spaced 
                // peaks and doublets

                // Wavelength is...also problematic. It does not deal well with
                // any asymmetry in the peaks. This is a move to a pure pixel peak
                // assessment of location

                double wavelength = peak.centerWavelength;

                if (peak.interpolatedPeakXValue.HasValue && !cfg.widePeak)
                    wavelength = peak.interpolatedPeakXValue.Value;
                double intensity = peak.intensityAboveBackground; //  spectrum[pixel]; // YOU ARE HERE 
                double fwhm = peak.wavelengthFullWidthHalfMaximum;
                double interpPixel = peak.interpolatedPixel;

                peaks.Add(pixel, new PeakInfo(wavelength, intensity, fwhm, interpPixel, peak.peakHeight, peak.startPixel, peak.endPixel));
            }
            return peaks;
        }


        /// <summary>
        /// Process the given Measurement's spectrum to [re]generate peak metrics
        /// for the peak closest to the given pixel using the provided x-axis.
        /// </summary>
        ///
        /// <remarks>
        /// The use-case here is that we have previously collected this measurement,
        /// and previously extracted a set of peaks on specific pixels.  However, 
        /// metrics of those peaks (like FWHM) are dependent on the wavelength
        /// calibration.  Therefore, whenever the program (or the user) changes the
        /// wavecal, we need to re-generate the metrics for each peak.
        /// </remarks>
        /// 
        /// <todo>
        /// This would be a little faster if we did all of the peaks at once, so
        /// we'd only have to call findPeaks() once for the new axis.
        /// </todo>
        public PeakInfo generateMetrics(uint pixel, Measurement m, double[] xaxis)
        {
            if (xaxis == null)
            {
                // this can happen if asked to regenerate metrics on the wavenumber axis
                // for a non-Raman spectrometer
                logger.debug("can't generate metrics without x-axis");
                return null;
            }

            // re-find peaks in this spectrum
            //
            // (note: this uses processed, meaning that when we re-generate metrics 
            // following dark collection, the dark-corrected version of the measurement
            // is automatically used)
            //
            //SortedDictionary<int, PeakInfo> measuredPeaks = findPeaks(xaxis, m.processed);

            PeakInfo measuredPeak = findExpectedPeak(xaxis, m.processed, pixel);

            if (measuredPeak == null)
            {
                logger.error("ERROR: NO PEAKS FOUND ON REACQUIRE: couldn't reacquire pixel {0} within {1} window", pixel, cfg.peakSearchHalfWidth);
                return null;
            }

            //this function previously tried to "refind" the peak, but as it stands now, no workflows come here
            //unless they've already found the peak they're looking for

            return measuredPeak;
        }
    }
}
