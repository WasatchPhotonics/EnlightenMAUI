using MathNet.Numerics.Statistics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Common
{

    public class NumericalMethods
    {
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

}
