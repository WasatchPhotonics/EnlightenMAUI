using EnlightenMAUI.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Models
{
    public class CorrectedPeak
    {
        public int left;
        public int right;
        public double[] correctedY;
    }

    public class SpectrumPeak
    {
        public double areaLowCutoff;
        public double centerWavelength;
        public double centroid;
        public double enclosingWidth;
        public double halfCrossingHigh_Half;
        public double halfCrossingHigh_Quarter;
        public double halfCrossingHigh_Tenth;
        public double halfCrossingLow_Half;
        public double halfCrossingLow_Quarter;
        public double halfCrossingLow_Tenth;
        public double integral;
        public double peakXValue;
        public double? interpolatedPeakXValue;
        public double pixelCrossingHigh_Half;
        public double pixelCrossingHigh_Quarter;
        public double pixelCrossingHigh_Tenth;
        public double pixelCrossingLow_Half;
        public double pixelCrossingLow_Quarter;
        public double pixelCrossingLow_Tenth;
        public double pixelFullWidthHalfMaximum;
        public double pixelFullWidthQuarterMaximum;
        public double pixelFullWidthTenthMaximum;
        public double wavelengthFullWidthHalfMaximum;
        public double wavelengthFullWidthQuarterMaximum;
        public double wavelengthFullWidthTenthMaximum;
        public double interpolatedPixel;
        public int startingXIndex;
        public int endingXIndex;
        public int startingXEnclosed;
        public int endingXEnclosed;
        public int leftXIndex_Half;
        public int leftXIndex_Quarter;
        public int leftXIndex_Tenth;
        public int pixelNumber;
        public int rightXIndex_Half;
        public int rightXIndex_Quarter;
        public int rightXIndex_Tenth;
        public int startPixel;
        public int endPixel;
        public bool useParabolicApproximation;
        public bool useBackgroundSubtraction;
        public bool widePeak;
        public double intensityAboveBackground;
        public double peakHeight;

        /// <throws>
        /// System.NullReferenceException: Object reference not set to an instance of an object
        /// </throws>
        public SpectrumPeak(int newPeakIndex, double[] xValues, double[] yValues, bool useParabolicApproximation = false, bool useBackgroundSubtraction = false, uint baselineHalfWidth = 0, bool widePeak = false)
        {
            this.useParabolicApproximation = useParabolicApproximation;
            this.useBackgroundSubtraction = useBackgroundSubtraction;
            this.widePeak = widePeak;

            interpolatedPixel = pixelNumber = newPeakIndex;
            peakXValue = xValues[pixelNumber];
            areaLowCutoff = yValues[pixelNumber] * 0.05;
            if (useBackgroundSubtraction)
            {

                double[] correctedY;
                CorrectedPeak correctedPeak;
                if (baselineHalfWidth == 0)
                    correctedPeak = NumericalMethods.singlePeakBaselineCorrect(xValues, yValues, pixelNumber);
                else
                    correctedPeak = NumericalMethods.singlePeakBaselineCorrect(xValues, yValues, pixelNumber, baselineHalfWidth);

                correctedY = correctedPeak.correctedY;
                startPixel = correctedPeak.left;
                endPixel = correctedPeak.right;

                intensityAboveBackground = correctedY[pixelNumber];
                //computeIntensityAboveBackground(xValues, yValues, pixelNumber);
                computeHalfCrossings(xValues, correctedY, pixelNumber);
                computeQuarterCrossings(xValues, correctedY, pixelNumber);
                computeTenthCrossings(xValues, correctedY, pixelNumber);
                computeFullWidthAtHalfMaximum();
                computeFullWidthAtQuarterMaximum();
                computeFullWidthAtTenthMaximum();
                computeCenterWavelength();
                computeCentroid(xValues, correctedY, pixelNumber);
                computeIntegral(xValues, correctedY, pixelNumber, IntegrationMethod.RECTANGULAR);
                computeEnclosingWidth(xValues, correctedY, pixelNumber, 0.9d);
            }
            else
            {
                computeHalfCrossings(xValues, yValues, pixelNumber);
                computeQuarterCrossings(xValues, yValues, pixelNumber);
                computeTenthCrossings(xValues, yValues, pixelNumber);
                computeFullWidthAtHalfMaximum();
                computeFullWidthAtQuarterMaximum();
                computeFullWidthAtTenthMaximum();
                computeCenterWavelength();
                computeCentroid(xValues, yValues, pixelNumber);
                computeIntegral(xValues, yValues, pixelNumber, IntegrationMethod.RECTANGULAR);
                computeEnclosingWidth(xValues, yValues, pixelNumber, 0.9d);
            }

        }

        public SpectrumPeak(int peakIndex, double[] xValues, double[] yValues, double lowCutoff, IntegrationMethod method)
        {
            peakXValue = xValues[peakIndex];
            areaLowCutoff = lowCutoff;
            computeIntensityAboveBackground(xValues, yValues, peakIndex);
            computeHalfCrossings(xValues, yValues, peakIndex);
            computeQuarterCrossings(xValues, yValues, peakIndex);
            computeTenthCrossings(xValues, yValues, peakIndex);
            computeFullWidthAtHalfMaximum();
            computeFullWidthAtQuarterMaximum();
            computeFullWidthAtTenthMaximum();
            computeCenterWavelength();
            computeCentroid(xValues, yValues, peakIndex);
            computeIntegral(xValues, yValues, peakIndex, method);
        }

        void computeEnclosingWidth(double[] xValues, double[] yValues, int peakIndex, double fraction)
        {
            double fullArea = integral;
            double targetArea = fullArea * fraction;
            int high = peakIndex;
            int low = peakIndex;

            int extent = Math.Max(peakIndex - startingXIndex, endingXIndex - peakIndex);
            double accumulatedArea = yValues[peakIndex];
            for (int i = 1; i < extent && accumulatedArea < targetArea; i++)
            {
                high = peakIndex + i;
                low = peakIndex - i;

                if (high <= endingXIndex)
                {
                    double deltaX = (xValues[high + 1] - xValues[high - 1]) / 2;
                    accumulatedArea += yValues[high] * deltaX;
                }
                else
                {
                    high = endingXIndex;
                }

                if (low >= startingXIndex)
                {
                    double deltaX = (xValues[low + 1] - xValues[low - 1]) / 2;
                    accumulatedArea += yValues[low] * deltaX;
                }
                else
                {
                    low = startingXIndex;
                }
            }
            enclosingWidth = xValues[high] - xValues[low];
            startingXEnclosed = low;
            endingXEnclosed = high;
        }

        double maxHeight(double[] yValues, int peakIndex)
        {
            double retval = yValues[peakIndex];

            interpolatedPixel = (double)peakIndex;

            if (!useParabolicApproximation)
                return retval;

            Point2D vertex = null;
            try
            {
                vertex = ParabolicApproximation.vertexFromPointAndTwoNeighbors(peakIndex, yValues);
            }
            catch (Exception ex)
            {
                return retval;
            }

            if (vertex == null)
                return retval;

            interpolatedPixel = vertex.x;
            return Math.Max(yValues[peakIndex], vertex.y);
        }

        // If peak is on a slope, or valley between two strong peaks, draw a line
        // from the left shoulder to the right shoulder and find the baseline 
        // intensity for computing a "background subtracted" peak height.
        //
        //        ^ P
        //       /:\
        //      / : \      
        //     /  :  \
        //    /   :   \
        // \_/    :    \
        //  L     :     \   /
        //        B      \_/ 
        //                R  
        //
        // I'm unsure if this should use startingXIndex or startingXEnclosed 
        // (and -Ending).  Technically, neither are currently defined as
        // minima, more as "probable extent of the cone for purpose of area 
        // calculation."  
        //
        // As it turns out, we can use neither, because this needs to be 
        // generated well prior to computeIntegral() when those are computed.  
        // Therefore, we will locate the shoulders here, defining them simply
        // as the first local minima to either side.
        void computeIntensityAboveBackground(double[] xValues, double[] yValues, int peakIndex)
        {
            // find left shoulder
            int x = Math.Max(0, peakIndex - 1);
            while (x > 0 && yValues[x - 1] < yValues[x])
                x--;
            Point2D L = new Point2D(xValues[x], yValues[x]);

            // find right shoulder
            x = Math.Min(peakIndex + 1, yValues.Length - 1);
            while (x + 1 < yValues.Length && yValues[x + 1] < yValues[x])
                x++;
            Point2D R = new Point2D(xValues[x], yValues[x]);

            // the peak
            Point2D P = new Point2D(xValues[peakIndex], maxHeight(yValues, peakIndex));

            // slope from left to right
            double m = (R.y - L.y) / (R.x - L.x);

            // inferred baseline height at (under) peak
            Point2D B = new Point2D(P.x, L.y + m * (P.x - L.x));

            // background subtracted height
            intensityAboveBackground = P.y - B.y;
        }



        //       ^ 
        //      / \
        //    B/___\A     full width at half max, with two points (A) and (B)
        //    /     \
        //   /       \
        // _/         \_
        void computeHalfCrossings(double[] xValues, double[] yValues, int peakIndex)
        {
            peakHeight = maxHeight(yValues, peakIndex);
            interpolatedPeakXValue = CommonNumericalMethods.linearInterpolation(xValues, interpolatedPixel);

            double halfMax = useBackgroundSubtraction ? (peakHeight - 0.5 * intensityAboveBackground) : 0.5 * peakHeight;

            // find index <= (A)
            rightXIndex_Half = findNextCrossingInnerIndex(xValues, yValues, peakIndex, halfMax);

            // find precise (interpolated) Y-value for (A)
            halfCrossingHigh_Half = findNextCrossingValue(xValues, yValues, rightXIndex_Half, halfMax);

            // find precise (interpolated) pixel for (A)
            pixelCrossingHigh_Half = findNextCrossingPixel(yValues, rightXIndex_Half, halfMax);

            // repeat for (B)
            leftXIndex_Half = findPreviousCrossingInnerIndex(xValues, yValues, peakIndex, halfMax);
            halfCrossingLow_Half = findPreviousCrossingValue(xValues, yValues, leftXIndex_Half, halfMax);
            pixelCrossingLow_Half = findPreviousCrossingPixel(yValues, leftXIndex_Half, halfMax);
        }

        void computeQuarterCrossings(double[] xValues, double[] yValues, int peakIndex)
        {
            double peakHeight = maxHeight(yValues, peakIndex);
            double quarterMax = useBackgroundSubtraction ? (peakHeight - 0.75 * intensityAboveBackground) : 0.25 * peakHeight;

            rightXIndex_Quarter = findNextCrossingInnerIndex(xValues, yValues, peakIndex, quarterMax);
            halfCrossingHigh_Quarter = findNextCrossingValue(xValues, yValues, this.rightXIndex_Quarter, quarterMax);
            pixelCrossingHigh_Quarter = findNextCrossingPixel(yValues, this.rightXIndex_Quarter, quarterMax);
            leftXIndex_Quarter = findPreviousCrossingInnerIndex(xValues, yValues, peakIndex, quarterMax);
            halfCrossingLow_Quarter = findPreviousCrossingValue(xValues, yValues, this.leftXIndex_Quarter, quarterMax);
            pixelCrossingLow_Quarter = findPreviousCrossingPixel(yValues, this.leftXIndex_Quarter, quarterMax);
        }

        void computeTenthCrossings(double[] xValues, double[] yValues, int peakIndex)
        {
            double peakHeight = maxHeight(yValues, peakIndex);
            double tenthMax = useBackgroundSubtraction ? (peakHeight - 0.9 * intensityAboveBackground) : 0.1 * peakHeight;

            rightXIndex_Tenth = findNextCrossingInnerIndex(xValues, yValues, peakIndex, tenthMax);
            halfCrossingHigh_Tenth = findNextCrossingValue(xValues, yValues, this.rightXIndex_Tenth, tenthMax);
            pixelCrossingHigh_Tenth = findNextCrossingPixel(yValues, this.rightXIndex_Tenth, tenthMax);
            leftXIndex_Tenth = findPreviousCrossingInnerIndex(xValues, yValues, peakIndex, tenthMax);
            halfCrossingLow_Tenth = findPreviousCrossingValue(xValues, yValues, this.leftXIndex_Tenth, tenthMax);
            pixelCrossingLow_Tenth = findPreviousCrossingPixel(yValues, this.leftXIndex_Tenth, tenthMax);
        }

        void computeFullWidthAtHalfMaximum()
        {
            wavelengthFullWidthHalfMaximum = halfCrossingHigh_Half - halfCrossingLow_Half;
            pixelFullWidthHalfMaximum = pixelCrossingHigh_Half - pixelCrossingLow_Half;
            if (widePeak)
                interpolatedPixel = (pixelCrossingHigh_Half + pixelCrossingLow_Half) / 2;
        }

        void computeFullWidthAtQuarterMaximum()
        {
            wavelengthFullWidthQuarterMaximum = halfCrossingHigh_Quarter - halfCrossingLow_Quarter;
            pixelFullWidthQuarterMaximum = pixelCrossingHigh_Quarter - pixelCrossingLow_Quarter;
        }

        void computeFullWidthAtTenthMaximum()
        {
            wavelengthFullWidthTenthMaximum = halfCrossingHigh_Tenth - halfCrossingLow_Tenth;
            pixelFullWidthTenthMaximum = pixelCrossingHigh_Tenth - pixelCrossingLow_Tenth;
        }

        void computeCenterWavelength()
        {
            centerWavelength = (halfCrossingHigh_Half + halfCrossingLow_Half) / 2;
        }

        void computeCentroid(double[] xValues, double[] yValues, int peakIndex)
        {
            int startingX = findPreviousCrossingInnerIndex(xValues, yValues, peakIndex, areaLowCutoff);
            int endingX = findNextCrossingInnerIndex(xValues, yValues, peakIndex, areaLowCutoff);
            double numer = 0;
            double denom = 0;
            for (int i = startingX; i <= endingX; i++)
            {
                numer += xValues[i] * yValues[i];
                denom += yValues[i];
            }
            centroid = numer / denom;
        }

        void computeIntegral(double[] xValues, double[] yValues, int peakIndex, IntegrationMethod method)
        {
            startingXIndex = findPreviousCrossingInnerIndex(xValues, yValues, peakIndex, areaLowCutoff);
            if (0 == startingXIndex)
                startingXIndex++;
            endingXIndex = findNextCrossingInnerIndex(xValues, yValues, peakIndex, areaLowCutoff);
            if (xValues.Length - 1 == endingXIndex)
                endingXIndex--;
            integral = NumericalMethods.integrate(xValues, yValues, startingXIndex, endingXIndex, method);
        }

        // moving rightward from peakIndex, find the LAST pixel BEFORE y drops below target
        // (last pixel where y >= target)
        int findNextCrossingInnerIndex(double[] xVals, double[] yVals, int peakIndex, double target)
        {
            for (int i = peakIndex; i < xVals.Length - 1; i++)
                if (yVals[i] >= target && yVals[i + 1] < target)
                    return i;
            return xVals.Length - 1;
        }

        // given the pixel BEFORE y crosses target, return the interpolated x 
        // where the crossing would occur
        double findNextCrossingValue(double[] xVals, double[] yVals, int pixel, double target)
        {
            if (pixel == xVals.Length - 1)
                return xVals[pixel];
            else
                return findXCrossingY(
                    xVals[pixel],      // p1
                    yVals[pixel],
                    xVals[pixel + 1],  // p2
                    yVals[pixel + 1],
                    target);           // threshold
        }

        int findPreviousCrossingInnerIndex(double[] xVals, double[] yVals, int startingIndex, double target)
        {
            int lowerInner = 0;
            for (lowerInner = startingIndex; lowerInner > 0; lowerInner--)
                if (yVals[lowerInner] >= target && yVals[lowerInner - 1] < target)
                    break;
            return lowerInner;
        }

        double findPreviousCrossingValue(double[] xVals, double[] yVals, int innerIndex, double target)
        {
            if (0 == innerIndex)
                return xVals[0];
            else
                return findXCrossingY(xVals[innerIndex - 1], yVals[innerIndex - 1], xVals[innerIndex], yVals[innerIndex], target);
        }

        double findPreviousCrossingPixel(double[] yVals, int innerIndex, double target)
        {
            if (0 == innerIndex)
                return 0;
            else
                return findXCrossingY(innerIndex - 1, yVals[innerIndex - 1], innerIndex, yVals[innerIndex], target);
        }

        // given the pixel BEFORE y crosses the target, return the interpolated 
        // fractional PIXEL (not x) where the crossing would occur
        double findNextCrossingPixel(double[] yVals, int pixel, double target)
        {
            if (pixel == yVals.Length - 1)
                return pixel;
            else
                return findXCrossingY(
                    pixel,
                    yVals[pixel],
                    pixel + 1,
                    yVals[pixel + 1],
                    target);
        }

        // given two points (p1 and p2), draw a line between them and return the
        // 'x' where the line intersects target 'y'
        double findXCrossingY(double x1, double y1, double x2, double y2, double targetY)
        {
            Point2D p1 = new Point2D(x1, y1);
            Point2D p2 = new Point2D(x2, y2);

            // create a fake horizontal vector at the target Y, increasing (horizontally)
            // in the same direction (in x) as the input points
            Point2D p3;
            Point2D p4;
            if (p1.x <= p2.x)
            {
                p3 = new Point2D(x1 - 1, targetY);
                p4 = new Point2D(x2 + 1, targetY);
            }
            else
            {
                p3 = new Point2D(x1 + 1, targetY);
                p4 = new Point2D(x2 - 1, targetY);
            }

            // find the intersection of the two vectors
            Point2D intersection = NumericalMethods.computeIntersection(p1, p2, p3, p4);
            return intersection == null ? 0 : intersection.x;
        }
    }
    public class PeakFinding
    {

        public static SpectrumPeak getExpectedPeak(double[] xValues, double[] yValues, uint pixel, bool useParabolicApproximation = false, bool useBackgroundSubtraction = false, uint baselineHalfWidth = 0, bool widePeak = false)
        {
            SpectrumPeak peak = new SpectrumPeak((int)(pixel), xValues, yValues, useParabolicApproximation, useBackgroundSubtraction, baselineHalfWidth, widePeak);

            return peak;
        }

        public static SpectrumPeak[] getAllPeaks(double[] xValues, double[] yValues, int minIndicesBetweenPeaks, double baseline, bool useParabolicApproximation = false, bool useBackgroundSubtraction = false, uint baselineHalfWidth = 0)
        {
            int[] peakIndices = getPeakIndices(yValues, minIndicesBetweenPeaks, baseline);
            if (peakIndices == null)
                return null;

            List<SpectrumPeak> peaks = new List<SpectrumPeak>();
            for (int i = 0; i < peakIndices.Length; i++)
            {
                SpectrumPeak peak = new SpectrumPeak(peakIndices[i], xValues, yValues, useParabolicApproximation, useBackgroundSubtraction, baselineHalfWidth);
                peaks.Add(peak);
            }
            return peaks.ToArray();
        }

        public static SpectrumPeak[] getAllDerivativePeaks(double[] xValues, double[] yValues, int minIndicesBetweenPeaks, int order)
        {
            int[] peakIndices = getDerivativePeakIndices(yValues, minIndicesBetweenPeaks, order);
            List<SpectrumPeak> peaks = new List<SpectrumPeak>();
            for (int i = 0; i < peakIndices.Length; i++)
                peaks.Add(new SpectrumPeak(peakIndices[i], xValues, yValues));
            return peaks.ToArray();
        }

        public static int[] getPeakIndices(double[] spectrum, int minIndicesBetweenPeaks, double baseline)
        {
            List<int> peaks = new List<int>();

            minIndicesBetweenPeaks = Math.Max(1, minIndicesBetweenPeaks);

            for (int i = 0; i < spectrum.Length; i++)
            {
                bool foundGreater = false;
                if (spectrum[i] < baseline)
                    continue;

                for (int j = -minIndicesBetweenPeaks; j <= minIndicesBetweenPeaks; j++)
                {
                    if (0 == j)
                        continue;

                    if ((i + j >= 0) && (i + j < spectrum.Length))
                        if (spectrum[i + j] > spectrum[i])
                            foundGreater = true;
                }

                if (!foundGreater)
                    peaks.Add(i);
            }

            return peaks.ToArray();
        }

        public static int[] getDerivativePeakIndices(double[] spectrum, int minIndicesBetweenPeaks, int order)
        {
            List<int> peaks = new List<int>();

            if (minIndicesBetweenPeaks < 1)
                minIndicesBetweenPeaks = 1;

            for (int i = 0; i < spectrum.Length; i += minIndicesBetweenPeaks)
            {
                bool stride = false;
                int zeroCrossing = 0;
                int zeroIndex = 0;

                for (int j = 0; j <= minIndicesBetweenPeaks; j++)
                {
                    if (0 == j)
                        continue;

                    if ((i + j >= 0) && (i + j < spectrum.Length))
                    {
                        if (spectrum[i + j] > 0 && !stride)
                            stride = true;

                        if (spectrum[i + j] <= 0 && stride)
                        {
                            stride = false;
                            ++zeroCrossing;
                            zeroIndex = i + j;
                        }
                    }
                }

                if (zeroCrossing != 1)
                    continue;

                peaks.Add(zeroIndex);
            }
            return peaks.ToArray();
        }

        public static int getPeakIndexClosestToWavelength(double[] wavelengths, double[] spectrum, double targetWavelength, int minIndicesBetweenPeaks, double baseline)
        {
            int[] peaks = getPeakIndices(spectrum, minIndicesBetweenPeaks, baseline);
            if (null == peaks || 0 == peaks.Length)
                throw new Exception("no peaks");

            double leastDifference = System.Double.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < peaks.Length; i++)
            {
                double wl = wavelengths[peaks[i]];
                double delta = System.Math.Abs(wl - targetWavelength);
                if (delta < leastDifference)
                {
                    bestIndex = peaks[i];
                    leastDifference = delta;
                }
            }
            return bestIndex;
        }

        public static int getPeakIndexClosestToIndex(double[] spectrum, int targetIndex, int minIndicesBetweenPeaks, double baseline)
        {
            int[] peaks = getPeakIndices(spectrum, minIndicesBetweenPeaks, baseline);
            if (null == peaks || 0 == peaks.Length)
                throw new Exception("no peaks");

            int leastDifference = Int32.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < peaks.Length; i++)
            {
                int index = peaks[i];
                int delta = Math.Abs(index - targetIndex);
                if (delta < leastDifference)
                {
                    bestIndex = peaks[i];
                    leastDifference = delta;
                }
            }
            return bestIndex;
        }

        public static double[] getPeakWavelengths(double[] wavelengths, double[] spectrum, int minIndicesBetweenPeaks, double baseline)
        {
            try
            {
                int[] peaks = getPeakIndices(spectrum, minIndicesBetweenPeaks, baseline);
                double[] retval = new double[peaks.Length];
                for (int i = 0; i < retval.Length; i++)
                    retval[i] = wavelengths[peaks[i]];
                return retval;
            }
            catch
            {
                return null;
            }
        }

        public static double getPeakWavelengthClosestToWavelength(double[] wavelengths, double[] spectrum, double targetWavelength, int minIndicesBetweenPeaks, double baseline)
        {
            try
            {
                int closestIndex = getPeakIndexClosestToWavelength(wavelengths, spectrum, targetWavelength, minIndicesBetweenPeaks, baseline);
                return wavelengths[closestIndex];
            }
            catch
            {
                return 0;
            }
        }

        public static int getNextPeakIndex(double[] spectrum, int startingIndex, int minIndicesBetweenPeaks, double baseline)
        {
            int[] peaks = getPeakIndices(spectrum, minIndicesBetweenPeaks, baseline);
            if (peaks != null && peaks.Length > 0)
            {
                for (int i = 0; i < peaks.Length; i++)
                {
                    if (peaks[i] <= startingIndex)
                        continue;

                    return peaks[i];
                }
            }
            return 0;
        }

        public static int getPreviousPeakIndex(double[] spectrum, int startingIndex, int minIndicesBetweenPeaks, double baseline)
        {
            int[] peaks = getPeakIndices(spectrum, minIndicesBetweenPeaks, baseline);
            if (peaks != null && peaks.Length > 0)
            {
                for (int i = peaks.Length - 1; i >= 0; i--)
                {
                    if (peaks[i] >= startingIndex)
                        continue;
                    return peaks[i];
                }
            }
            return 0;
        }

        public static double getNextPeakWavelength(double[] wavelengths, double[] spectrum, double startingWavelength, int minIndicesBetweenPeaks, double baseline)
        {
            double[] peakWavelengths = getPeakWavelengths(wavelengths, spectrum, minIndicesBetweenPeaks, baseline);
            if (null == peakWavelengths || 0 == peakWavelengths.Length)
                return 0.0;

            for (int i = 0; i < peakWavelengths.Length; i++)
            {
                if (peakWavelengths[i] <= startingWavelength)
                    continue;
                return peakWavelengths[i];
            }
            return 0;
        }

        public static double getPreviousPeakWavelength(double[] wavelengths, double[] spectrum, double startingWavelength, int minIndicesBetweenPeaks, double baseline)
        {
            double[] peakWavelengths = getPeakWavelengths(wavelengths, spectrum, minIndicesBetweenPeaks, baseline);
            if (null == peakWavelengths || 0 == peakWavelengths.Length)
                return 0.0;
            for (int i = peakWavelengths.Length - 1; i >= 0; i--)
            {
                if (peakWavelengths[i] >= startingWavelength)
                    continue;
                return peakWavelengths[i];
            }
            return 0;
        }
    }
}
