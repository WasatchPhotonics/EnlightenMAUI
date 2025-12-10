using EnlightenMAUI.Models;
using MathNet.Numerics.Statistics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Common
{
    public enum IntegrationMethod { RECTANGULAR, TRAPEZOIDAL, SIMPSONS, MAXIMUM };

    public class ParabolicApproximation
    {
        /// <summary>
        /// Given an array of doubles and a peak index, use the peak and its two
        /// neighbors to form a parabola and return the interpolated maximum height of the parabola.
        /// </summary>
        ///
        /// <param name="pixel">index of a point on the spectrum</param>
        /// <param name="a">an array (spectrum)</param>
        ///
        /// <see cref="https://stackoverflow.com/a/717833"/>
        ///
        /// <returns>
        /// a point representing the interpolated vertex of a parabola drawn 
        /// through the specified pixel and its two neighbors (in pixel space)
        /// </returns>
        ///
        /// <remarks>
        /// "pixel" is ideally the array index of the pinnacle of a previously-
        /// identified peak within the spectrum, although though this will 
        /// technically generate a parabola through any pixel and its two 
        /// neighbors.
        /// </remarks>
        static public Point2D vertexFromPointAndTwoNeighbors(int pixel, double[] a)
        {
            double x1 = pixel - 1;
            double x2 = pixel;
            double x3 = pixel + 1;

            if (x1 < 0 || x3 >= a.Length)
                return null;

            double y1 = a[pixel - 1];
            double y2 = a[pixel];
            double y3 = a[pixel + 1];

            double denom = (x1 - x2) * (x1 - x3) * (x2 - x3);
            double A = (x3 * (y2 - y1) + x2 * (y1 - y3) + x1 * (y3 - y2)) / denom;
            double B = (x3 * x3 * (y1 - y2) + x2 * x2 * (y3 - y1) + x1 * x1 * (y2 - y3)) / denom;
            double C = (x2 * x3 * (x2 - x3) * y1 + x3 * x1 * (x3 - x1) * y2 + x1 * x2 * (x1 - x2) * y3) / denom;

            Point2D vertex = new Point2D();
            vertex.x = -B / (2 * A);
            vertex.y = C - B * B / (4 * A);

            return vertex;
        }
    }

    public class Point2D
    {
        public double x;
        public double y;

        public Point2D() { }

        public Point2D(double x, double y)
        {
            this.x = x;
            this.y = y;
        }

        public double distance(Point2D p2)
        {
            return Math.Sqrt(Math.Pow(x - p2.x, 2) + Math.Pow(y - p2.y, 2));
        }
    }

    public class NumericalMethods
    {
        static public WMatrix getIdentityMatrix(int rows, int cols)
        {
            WMatrix retval = new WMatrix(rows, cols);
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    retval.setElement(i, j, i == j ? 1 : 0);
            return retval;
        }

        static public double average(double[] data)
        {
            return data.Sum() / data.Length;
        }

        static public double evaluatePolynomial(double x, double[] coefficients)
        {
            if (null == coefficients)
                throw new Exception("missing coefficients");

            double y = 0;
            for (int i = 0; i < coefficients.Length; i++)
                y += coefficients[i] * Math.Pow(x, i);
            return y;
        }

        static public double rmsdEstimate(double[] pixels, double[] counts)
        {
            double[] derivs = derivative(pixels, counts, 1);
            double[] derivsPadded = new double[derivs.Length + 2];
            Array.Copy(derivs, 0, derivsPadded, 1, derivs.Length);
            List<double> sums = new List<double>();

            for (int i = 0; i < derivs.Length + 1; i++)
            {
                if (derivsPadded[i] * derivsPadded[i + 1] <= 0)
                {
                    sums.Add(derivsPadded[i] * derivsPadded[i] + derivsPadded[i + 1] * derivsPadded[i + 1]);
                }
            }

            double med = median(sums.ToArray());
            double final = Math.Sqrt(med);
            return final;
        }

        public static double order(double[] data, double order)
        {
            if (order < 0.0 || order > 1.0)
                return double.NaN;

            List<double> sortedList = new List<double>(data);
            sortedList.Sort();
            return sortedList[(int)(data.Length * order)];
        }

        public static double median(double[] data)
        {
            return order(data, 0.5);
        }


        static public Point2D computeIntersection(Point2D P1, Point2D P2, Point2D P3, Point2D P4)
        {
            double x1 = P1.x;
            double x2 = P2.x;
            double x3 = P3.x;
            double x4 = P4.x;

            double y1 = P1.y;
            double y2 = P2.y;
            double y3 = P3.y;
            double y4 = P4.y;

            if ((y4 - y3) * (x2 - x1) == (x4 - x3) * (y2 - y1))
                return null;

            double s = ((x4 - x3) * (y1 - y3) - (y4 - y3) * (x1 - x3)) / ((y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1));
            double t = ((x2 - x1) * (y1 - y3) - (y2 - y1) * (x1 - x3)) / ((y4 - y3) * (x2 - x1) - (x4 - x3) * (y2 - y1));

            if ((s >= 0d && s <= 1d && t >= 0d && t <= 1d) == false)
                return null;

            double intX = x1 + s * (x2 - x1);
            double intY = y1 + s * (y2 - y1);

            return new Point2D(intX, intY);
        }

        static public double integrate(double[] x, double[] y, double startX, double endX, IntegrationMethod integraltype)
        {
            int startPix = CommonNumericalMethods.nrHunt(x, startX);
            int endPix = CommonNumericalMethods.nrHunt(x, endX);
            if (startPix < 0)
                startPix = 0;

            if (endPix >= x.Length - 1)
                endPix = x.Length - 2;

            return integrate(x, y, startPix, endPix, integraltype);
        }

        static public double integrate(double[] x, double[] y, int startX, int endX, IntegrationMethod integraltype)
        {
            double integral = 0.0;
            switch (integraltype)
            {
                case IntegrationMethod.RECTANGULAR:
                    for (int i = startX; i < endX; i++)
                        integral += (y[i] * (x[i + 1] - x[i]));
                    break;

                case IntegrationMethod.TRAPEZOIDAL:
                    for (int i = startX; i < endX; i++)
                        integral += (0.5 * y[i] + 0.5 * y[i + 1]) * (x[i + 1] - x[i]);
                    break;

                case IntegrationMethod.SIMPSONS:
                    --endX;
                    for (int i = startX; i < endX; i++)
                        integral += ((1.0 / 3.0 * y[i]) + (4.0 / 3.0 * y[i + 1]) + (1.0 / 3.0 * y[i + 2])) * (x[i + 1] - x[i]) / 2.0;
                    break;
                case IntegrationMethod.MAXIMUM:
                    for (int i = startX; i < endX; i++)
                    {
                        if (y[i] > integral)
                            integral = y[i];
                    }
                    break;
            }
            return integral;
        }

        static public double integrate(double[] x, double[] y, IntegrationMethod integraltype)
        {
            return integrate(x, y, 0, x.Length - 1, integraltype);
        }


        static public CorrectedPeak singlePeakBaselineCorrectOld(double[] xValues, double[] yValues, int peakIndex)
        {
            double[] derivatives = NumericalMethods.derivative(xValues, yValues, 1);
            double[] secondDerivatives = NumericalMethods.derivative(xValues, yValues, 2);

            // find left shoulder
            int x = Math.Max(0, peakIndex - 1);

            double leftMaxSlope = Double.MinValue;
            double leftCutoff;
            double rightMaxSlope = Double.MinValue;
            double rightCutoff;

            while (x > 0 && secondDerivatives[x] < 0 && derivatives[x] > 0)
            {
                if (Math.Abs(derivatives[x]) > leftMaxSlope)
                    leftMaxSlope = Math.Abs(derivatives[x]);
                x--;
            }

            leftCutoff = leftMaxSlope * 0.01;

            while (x > 0 && Math.Abs(derivatives[x]) > leftCutoff && derivatives[x] > 0)
            {
                if (Math.Abs(derivatives[x]) > leftMaxSlope)
                {
                    leftMaxSlope = Math.Abs(derivatives[x]);
                    leftCutoff = leftMaxSlope * 0.01;
                }
                x--;
            }
            Point2D L = new Point2D(xValues[x], yValues[x]);

            int leftShoulder = x;

            // find right shoulder
            x = Math.Min(peakIndex + 1, yValues.Length - 1);

            while (x + 1 < yValues.Length && secondDerivatives[x] < 0 && derivatives[x] < 0)
            {
                if (Math.Abs(derivatives[x]) > rightMaxSlope)
                    rightMaxSlope = Math.Abs(derivatives[x]);
                x++;
            }

            rightCutoff = rightMaxSlope * 0.01;

            while (x + 1 < yValues.Length && Math.Abs(derivatives[x]) > rightCutoff && derivatives[x] < 0)
            {
                if (Math.Abs(derivatives[x]) > rightMaxSlope)
                {
                    rightMaxSlope = Math.Abs(derivatives[x]);
                    rightCutoff = rightMaxSlope * 0.01;
                }
                x++;
            }
            Point2D R = new Point2D(xValues[x], yValues[x]);

            int rightShoulder = x;
            // slope from left to right
            double m = (R.y - L.y) / (R.x - L.x);

            double[] correctedY = new double[yValues.Length];
            Array.Copy(yValues, correctedY, yValues.Length);

            for (int i = 0; i < leftShoulder; ++i)
                correctedY[i] = yValues[i];

            for (int i = leftShoulder; i <= rightShoulder; ++i)
            {
                Point2D B = new Point2D(xValues[i], L.y + m * (xValues[i] - L.x));
                correctedY[i] = yValues[i] - B.y;
            }

            for (int i = rightShoulder + 1; i < yValues.Length; ++i)
                correctedY[i] = yValues[i];

            CorrectedPeak p = new CorrectedPeak
            {
                left = leftShoulder,
                right = rightShoulder,
                correctedY = correctedY
            };

            return p;
        }

        static public CorrectedPeak singlePeakBaselineCorrect(double[] xValues, double[] yValues, int peakIndex)
        {
            double[] derivatives = NumericalMethods.derivative(xValues, yValues, 1);
            double[] secondDerivatives = NumericalMethods.derivative(xValues, yValues, 2);

            // find left shoulder
            int x = Math.Max(0, peakIndex - 1);

            double maxLeftSecondDeriv = secondDerivatives[x];
            int leftShoulder = x;

            double leftMaxSlope = Double.MinValue;
            double leftCutoff;
            double rightMaxSlope = Double.MinValue;
            double rightCutoff;

            while (x > 0 && secondDerivatives[x] < 0 && derivatives[x] > 0)
            {
                if (Math.Abs(derivatives[x]) > leftMaxSlope)
                    leftMaxSlope = Math.Abs(derivatives[x]);
                if (secondDerivatives[x] > maxLeftSecondDeriv)
                {
                    maxLeftSecondDeriv = secondDerivatives[x];
                    leftShoulder = x;
                }
                x--;
            }

            leftCutoff = leftMaxSlope * 0.01;

            while (x > 0 && Math.Abs(derivatives[x]) > leftCutoff && derivatives[x] > 0)
            {
                if (Math.Abs(derivatives[x]) > leftMaxSlope)
                {
                    leftMaxSlope = Math.Abs(derivatives[x]);
                    leftCutoff = leftMaxSlope * 0.01;
                }
                if (secondDerivatives[x] > maxLeftSecondDeriv)
                {
                    maxLeftSecondDeriv = secondDerivatives[x];
                    leftShoulder = x;
                }
                x--;
            }

            if (secondDerivatives[x] > maxLeftSecondDeriv)
            {
                leftShoulder = x;
            }

            leftShoulder = Math.Max(x, leftShoulder);

            Point2D L = new Point2D(xValues[leftShoulder], yValues[leftShoulder]);

            //leftShoulder = x;

            // find right shoulder
            x = Math.Min(peakIndex + 1, yValues.Length - 1);

            int rightShoulder = x + 2;
            double maxRightSecondDeriv = secondDerivatives[x];

            while (x + 1 < yValues.Length && secondDerivatives[x] < 0 && derivatives[x] < 0)
            {
                if (Math.Abs(derivatives[x]) > rightMaxSlope)
                    rightMaxSlope = Math.Abs(derivatives[x]);
                if (secondDerivatives[x] > maxRightSecondDeriv)
                {
                    maxRightSecondDeriv = secondDerivatives[x];
                    rightShoulder = x + 2;
                }
                x++;
            }

            rightCutoff = rightMaxSlope * 0.01;

            while (x + 1 < yValues.Length && Math.Abs(derivatives[x]) > rightCutoff && derivatives[x] < 0)
            {
                if (Math.Abs(derivatives[x]) > rightMaxSlope)
                {
                    rightMaxSlope = Math.Abs(derivatives[x]);
                    rightCutoff = rightMaxSlope * 0.01;
                }
                if (secondDerivatives[x] > maxRightSecondDeriv)
                {
                    maxRightSecondDeriv = secondDerivatives[x];
                    rightShoulder = x + 2;
                }
                x++;
            }

            if (secondDerivatives[x] > maxRightSecondDeriv)
            {
                rightShoulder = x + 2;
            }

            rightShoulder = Math.Min(x, rightShoulder);

            Point2D R = new Point2D(xValues[rightShoulder], yValues[rightShoulder]);

            //rightShoulder = x;
            // slope from left to right
            double m = (R.y - L.y) / (R.x - L.x);

            double[] correctedY = new double[yValues.Length];
            Array.Copy(yValues, correctedY, yValues.Length);

            for (int i = 0; i < leftShoulder; ++i)
                correctedY[i] = yValues[i];

            for (int i = leftShoulder; i <= rightShoulder; ++i)
            {
                Point2D B = new Point2D(xValues[i], L.y + m * (xValues[i] - L.x));
                correctedY[i] = yValues[i] - B.y;
            }

            for (int i = rightShoulder + 1; i < yValues.Length; ++i)
                correctedY[i] = yValues[i];

            CorrectedPeak p = new CorrectedPeak
            {
                left = leftShoulder,
                right = rightShoulder,
                correctedY = correctedY
            };

            return p;
        }

        static public CorrectedPeak singlePeakBaselineCorrect(double[] xValues, double[] yValues, int peakIndex, uint baselineHalfWidth)
        {

            int leftShoulder = peakIndex;// - (int)baselineHalfWidth;
            double minLeftInt = yValues[peakIndex];

            for (int i = peakIndex; i != 0 && i >= peakIndex - baselineHalfWidth; --i)
            {
                if (yValues[i] < minLeftInt)
                {
                    leftShoulder = i;
                    minLeftInt = yValues[i];
                }
            }


            if (leftShoulder < 0)
                leftShoulder = 0;

            Point2D L = new Point2D(xValues[leftShoulder], yValues[leftShoulder]);

            int rightShoulder = peakIndex;// + (int)baselineHalfWidth;
            double minRightInt = yValues[peakIndex];

            for (int i = peakIndex; i != yValues.Length && i <= peakIndex + baselineHalfWidth; ++i)
            {
                if (yValues[i] < minRightInt)
                {
                    rightShoulder = i;
                    minRightInt = yValues[i];
                }
            }

            if (rightShoulder >= yValues.Length)
                rightShoulder = yValues.Length - 1;

            Point2D R = new Point2D(xValues[rightShoulder], yValues[rightShoulder]);

            // slope from left to right
            double m = (R.y - L.y) / (R.x - L.x);

            double[] correctedY = new double[yValues.Length];
            Array.Copy(yValues, correctedY, yValues.Length);

            for (int i = 0; i < leftShoulder; ++i)
                correctedY[i] = yValues[i];

            for (int i = leftShoulder; i <= rightShoulder; ++i)
            {
                //if (m * (xValues[i] - L.x) < 0)
                //continue;

                Point2D B = new Point2D(xValues[i], L.y + m * (xValues[i] - L.x));

                if (B.y < 0)
                    continue;

                correctedY[i] = yValues[i] - B.y;

            }

            for (int i = rightShoulder + 1; i < yValues.Length; ++i)
                correctedY[i] = yValues[i];

            CorrectedPeak p = new CorrectedPeak
            {
                left = leftShoulder,
                right = rightShoulder,
                correctedY = correctedY
            };

            return p;
        }

        static public CorrectedPeak singlePeakStaticBaselineCorrect(double[] xValues, double[] yValues, int peakIndex, int leftPixel, int rightPixel)
        {

            Point2D L = new Point2D(xValues[leftPixel], yValues[leftPixel]);
            Point2D R = new Point2D(xValues[rightPixel], yValues[rightPixel]);

            // slope from left to right
            double m = (R.y - L.y) / (R.x - L.x);

            double[] correctedY = new double[yValues.Length];
            Array.Copy(yValues, correctedY, yValues.Length);

            for (int i = 0; i < leftPixel; ++i)
                correctedY[i] = yValues[i];

            for (int i = leftPixel; i <= rightPixel; ++i)
            {

                Point2D B = new Point2D(xValues[i], L.y + m * (xValues[i] - L.x));

                if (B.y < 0)
                    continue;

                correctedY[i] = yValues[i] - B.y;

            }

            for (int i = rightPixel + 1; i < yValues.Length; ++i)
                correctedY[i] = yValues[i];

            CorrectedPeak p = new CorrectedPeak
            {
                left = leftPixel,
                right = rightPixel,
                correctedY = correctedY
            };

            return p;
        }

        static public CorrectedPeak singlePeakBaselineCorrect(double[] xValues, double[] yValues, int peakIndex, uint leftWidth, uint rightWidth)
        {

            int leftShoulder = peakIndex;// - (int)baselineHalfWidth;
            double minLeftInt = yValues[peakIndex];

            for (int i = peakIndex; i != 0 && i >= peakIndex - leftWidth; --i)
            {
                if (yValues[i] < minLeftInt)
                {
                    leftShoulder = i;
                    minLeftInt = yValues[i];
                }
            }


            if (leftShoulder < 0)
                leftShoulder = 0;

            Point2D L = new Point2D(xValues[leftShoulder], yValues[leftShoulder]);

            int rightShoulder = peakIndex;// + (int)baselineHalfWidth;
            double minRightInt = yValues[peakIndex];

            for (int i = peakIndex; i != yValues.Length && i <= peakIndex + rightWidth; ++i)
            {
                if (yValues[i] < minRightInt)
                {
                    rightShoulder = i;
                    minRightInt = yValues[i];
                }
            }

            if (rightShoulder >= yValues.Length)
                rightShoulder = yValues.Length - 1;

            return singlePeakStaticBaselineCorrect(xValues, yValues, peakIndex, leftShoulder, rightShoulder);

            /*
            Point2D R = new Point2D(xValues[rightShoulder], yValues[rightShoulder]);

            // slope from left to right
            double m = (R.y - L.y) / (R.x - L.x);

            double[] correctedY = new double[yValues.Length];
            Array.Copy(yValues, correctedY, yValues.Length);

            for (int i = 0; i < leftShoulder; ++i)
                correctedY[i] = yValues[i];

            for (int i = leftShoulder; i <= rightShoulder; ++i)
            {
                //if (m * (xValues[i] - L.x) < 0)
                //continue;

                Point2D B = new Point2D(xValues[i], L.y + m * (xValues[i] - L.x));

                if (B.y < 0)
                    continue;

                correctedY[i] = yValues[i] - B.y;

            }

            for (int i = rightShoulder + 1; i < yValues.Length; ++i)
                correctedY[i] = yValues[i];

            CorrectedPeak p = new CorrectedPeak
            {
                left = leftShoulder,
                right = rightShoulder,
                correctedY = correctedY
            };

            return p;
            */
        }


        static public double[] derivative(double[] xArray, double[] yArray, int order)
        {
            if (xArray.Length != yArray.Length)
                throw new Exception("unequal array lengths");
            if (order <= 0)
                throw new Exception("Order must be positive");

            double[] retval = new double[xArray.Length];
            Array.Copy(yArray, 0, retval, 0, yArray.Length);

            for (int o = order; o > 0; o--)
                for (int i = 0; i < xArray.Length - 1; i++)
                    retval[i] = (retval[i + 1] - retval[i]) / (xArray[i + 1] - xArray[i]);

            return retval;
        }

    }

    public class CommonNumericalMethods
    {
        public static double[] linearInterpolation(double[] yIn, double[] xIn)
        {
            double[] yOut = new double[xIn.Length];

            for (int i = 0; i < xIn.Length; ++i)
            {
                yOut[i] = linearInterpolation(yIn, xIn[i]);
            }

            return yOut;
        }

        public static double linearInterpolation(double[] yIn, double interpIndex)
        {
            int index = (int)Math.Ceiling(interpIndex);

            if (index <= 0)
                return yIn[0];
            if (index >= yIn.Length)
                return yIn[yIn.Length - 1];

            double pctRight = interpIndex - Math.Floor(interpIndex);
            double pctLeft = 1 - pctRight;

            return yIn[index] * pctRight + yIn[index - 1] * pctLeft;
        }

        public static double linearInterpolation(double[] xIn, double[] yIn, double x)
        {
            double interpIndex = indexInterpolation(xIn, x);

            if (xIn == null || interpIndex < 0)
                return double.NaN;

            return linearInterpolation(yIn, interpIndex);
        }

        //
        //  Given a sorted array of values, and a value to search, this function returns where
        //  within the array that value would "land" as a fractional index. If greater than
        //  all values it returns the length of the array - 1, if less than all it returns 0.
        //
        //  e.g. if xIn = [ 1, 2, 3 ] and x = 1.1, then it would return 0.1
        public static double indexInterpolation(double[] xIn, double x)
        {
            if (xIn == null)
                return -1;
            int index = Array.BinarySearch(xIn, x);
            if (index < 0)
            {
                index = ~index;
                if (index == 0 || index == xIn.Length - 1)
                    return index;
                if (index > (xIn.Length - 1))
                    return xIn.Length - 1;

                double pctLeft = (x - xIn[index - 1]) / (xIn[index] - xIn[index - 1]);
                return index - 1 + pctLeft;
            }
            else if (index >= xIn.Length)
                return xIn.Length - 1;
            else
                return index;
        }

        public static double[] cubicSpline(double[] xIn, double[] yIn, double[] xOut)
        {
            double[] yOut = new double[xOut.Length];
            double[] yp = nrSpline(xIn, yIn, 0, 0);
            for (int i = 0; i < xOut.Length; i++)
                yOut[i] = nrSplint(xIn, yIn, yp, xOut[i]);
            return yOut;
        }

        public static int nrHunt(double[] x, double val)
        {
            int jm, jhi, inc;
            bool ascnd;
            int jlo = 0;
            int n = x.Length - 1;
            ascnd = (x[n] > x[1]);
            if (jlo <= 0 || jlo > n)
            {
                jlo = 0;
                jhi = n + 1;
            }
            else
            {
                inc = 1;
                if (val >= x[jlo] == ascnd)
                {
                    if (jlo == n)
                        return jlo;
                    jhi = (jlo) + 1;
                    while (val >= x[jhi] == ascnd)
                    {
                        jlo = jhi;
                        inc += inc;
                        jhi = jlo + inc;
                        if (jhi > n)
                        {
                            jhi = n + 1;
                            break;
                        }
                    }
                }
                else
                {
                    if (jlo == 1)
                    {
                        jlo = 0;
                        return jlo;
                    }
                    jhi = jlo--;
                    while (val < x[jlo] == ascnd)
                    {
                        jhi = jlo;
                        inc <<= 1;
                        if (inc >= jhi)
                        {
                            jlo = 0;
                            break;
                        }
                        else
                        {
                            jlo = jhi - inc;
                        }
                    }
                }
            }

            while ((jhi - jlo) != 1)
            {
                jm = (jhi + jlo) >> 1;
                if (val > x[jm] == ascnd)
                    jlo = jm;
                else
                    jhi = jm;
            }
            return jlo;
        }

        public static double[] nrSpline(double[] x, double[] y, double dx0, double dxn)
        {
            int n = x.Length - 1;
            double p, qn, sig, un;
            double[] u = new double[x.Length];
            double[] y2 = new double[x.Length];

            if (dx0 > 0.99e30)
            {
                y2[0] = u[0] = 0.0;
            }
            else
            {
                y2[0] = -0.5;
                u[0] = (3.0 / (x[1] - x[0])) * ((y[1] - y[0]) / (x[1] - x[0]) - dx0);
            }

            for (int i = 1; i < n - 1; i++)
            {
                sig = (x[i] - x[i - 1]) / (x[i + 1] - x[i - 1]);
                p = sig * y2[i - 1] + 2.0;
                y2[i] = (sig - 1.0) / p;
                u[i] = (y[i + 1] - y[i]) / (x[i + 1] - x[i]) - (y[i] - y[i - 1])
                        / (x[i] - x[i - 1]);
                u[i] = (6.0 * u[i] / (x[i + 1] - x[i - 1]) - sig * u[i - 1]) / p;
            }

            if (dxn > 0.99e30)
            {
                qn = un = 0.0;
            }
            else
            {
                qn = 0.5;
                un = (3.0 / (x[n] - x[n - 1])) * (dxn - (y[n] - y[n - 1]) / (x[n] - x[n - 1]));
            }

            y2[n] = (un - qn * u[n - 1]) / (qn * y2[n - 1] + 1.0);

            for (int k = n - 1; k >= 1; k--)
                y2[k] = y2[k] * y2[k + 1] + u[k];

            return y2;
        }

        public static double nrSplint(double[] x, double[] y, double[] dy, double xval)
        {
            int klo = 0;
            int khi = 0;
            double h, b, a;
            int n = x.Length - 1;
            double retval;

            klo = nrHunt(x, xval);
            if (klo >= x.Length - 1)
                return y[x.Length - 1];

            khi = klo + 1;
            h = x[khi] - x[klo];
            if (h <= 1e-9)
                return 0.0;

            a = (x[khi] - xval) / h;
            b = (xval - x[klo]) / h;
            retval = a * y[klo] + b * y[khi] + ((a * a * a - a) * dy[klo] + (b * b * b - b) * dy[khi]) * (h * h) / 6.0;
            return retval;
        }
    }
}
