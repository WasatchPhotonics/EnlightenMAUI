using Android.Widget;
using Android.Content;
using Android.OS.Storage;
using Microsoft.Maui;
using System;
using System.Text;
using static System.Net.Mime.MediaTypeNames;
using System.Threading;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Telerik.Maui;
using EnlightenMAUI.Models;
using MathNet.Numerics.LinearAlgebra.Factorization;
// Create an alias for CSparse's SparseLU class.
using CSparseLU = CSparse.Double.Factorization.SparseLU;

// Create an alias for CSparse's SparseMatrix class.
using CSparseMatrix = CSparse.Double.SparseMatrix;
using CSparse;
using MathNet.Numerics.LinearAlgebra.Storage;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using SkiaSharp;

namespace EnlightenMAUI.Common;

public static class StorageHelper
{
    public const int RequestCode = 2296;
    private static TaskCompletionSource<bool>? GetPermissionTask { get; set; }

    public static async Task<bool> GetManageAllFilesPermission()
    {
        try
        {
            Android.Net.Uri uri = Android.Net.Uri.Parse("package:" + Platform.CurrentActivity.ApplicationInfo.PackageName);

            GetPermissionTask = new();
            Intent intent = new(global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission,uri);
            Platform.CurrentActivity.StartActivityForResult(intent, RequestCode);
        }
        catch (Exception ex)
        {
            // Handle Exception
        }

        return await GetPermissionTask.Task;
    }

    public static void OnActivityResult()
    {
        GetPermissionTask?.SetResult(Android.OS.Environment.IsExternalStorageManager);
    }
}

/// <summary>
/// This class provides some generic utility methods to the whole
/// application, including some which are platform-specific.
/// </summary>
public class Util
{
    private static Logger logger = Logger.getInstance();

    public static void swap(ref ushort a, ref ushort b)
    {
        var tmp = a;
        a = b;
        b = tmp;
    }
    public static double interpolate(double y0, double y1, double pct0)
    {
        return pct0 * y0 + (1 - pct0) * y1;
    }

    const double NM_TO_CM = 1.0 / 10000000.0;
    const int DIGITS_TO_ROUND = 6;

    public static double[] generateWavelengths(uint pixels, float[] coeffs)
    {            
        double[] wavelengths = new double[pixels];
        for (uint pixel = 0; pixel < pixels; pixel++)
        {   
            wavelengths[pixel] = coeffs[0];
            for (int i = 1; i < coeffs.Length; i++)
                wavelengths[pixel] += coeffs[i] * Math.Pow(pixel, i);
        }  
        return wavelengths;
    }

    public static double[] wavelengthsToWavenumbers(double laserWavelengthNM, double[] wavelengths)
    {
        const double NM_TO_CM = 1.0 / 10000000.0;
        double LASER_WAVENUMBER = 1.0 / (laserWavelengthNM * NM_TO_CM);

        if (wavelengths == null)
            return null;

        double[] wavenumbers = new double[wavelengths.Length];
        for (int i = 0; i < wavelengths.Length; i++)
        {
            double wavenumber = LASER_WAVENUMBER - (1.0 / (wavelengths[i] * NM_TO_CM));
            if (Double.IsInfinity(wavenumber) || Double.IsNaN(wavenumber))
                wavenumbers[i] = 0;
            else
                wavenumbers[i] = wavenumber;
        }
        return wavenumbers;
    }

    public static double wavelengthToWavenumber(double laserWavelengthNM, double wavelength, bool trim = false)
    {
        double laserWavenumber = 1.0 / (laserWavelengthNM * NM_TO_CM);
        if (wavelength > 0)
        {
            if (!trim)
                return laserWavenumber - (1.0 / (wavelength * NM_TO_CM));
            else
                return Math.Round(laserWavenumber - (1.0 / (wavelength * NM_TO_CM)), DIGITS_TO_ROUND);

        }
        else
            return 0;
    }

    public static double wavenumberToWavelength(double laserWavelengthNM, double wavenumber, bool trim = false)
    {
        if (!trim)
            return (1.0 / ((1.0 / laserWavelengthNM) - (wavenumber * NM_TO_CM)));
        else
            return Math.Round((1.0 / ((1.0 / laserWavelengthNM) - (wavenumber * NM_TO_CM))), DIGITS_TO_ROUND);
    }

    //
    // Pearson library match, assumes provided measurements are already corrected to be on the same axis
    //
    public static double pearsonLibraryMatch(Measurement sampleM, Measurement library, double airPLSLambda = 10000, int airPLSMaxIter = 100, bool smooth = true)
    {
        if (smooth)
        {
            //logger.info("smoothing sample and library");
            double[] yIn = AirPLS.smooth(sampleM.postProcessed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)sampleM.roiStart, (int)sampleM.roiEnd);
            //double[] array = AirPLS.smooth(library.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)sampleM.roiStart, (int)sampleM.roiEnd);
            double[] array = library.processed.Skip((int)sampleM.roiStart).Take(yIn.Length).ToArray();

            logger.info("matching library array of length {0} to {1} with start val {2} and end val {3}",
                array.Length,
                yIn.Length,
                array[0],
                array[array.Length - 1]);

            double score = 0;

            try
            {
                //logger.info("calculating match score");
                score = MathNet.Numerics.Statistics.Correlation.Pearson(yIn, array);
                //logger.info("returning match score {0}", score);
            }
            catch (Exception e)
            {
                //logger.error("Pearson score failed with error {0}", e.Message);
                score = 0;
            }

            return score;
        }
        else
        {
            double[] yIn = sampleM.postProcessed;
            double[] array = library.processed;

            return MathNet.Numerics.Statistics.Correlation.Pearson(yIn, array);
        }
    }

    // Format a 16-byte array into a standard UUID string
    //
    // 00000000-0000-1000-8000-00805F9B34FB
    //  0 1 2 3  4 5  6 7  8 9  a b c d e f
    //
    // You'd think something like this would already be in Plugin.BLE, and
    // probably it is... *shrug*
    public static string formatUUID(byte[] data)
    {
        if (data.Length != 16)
            return "invalid-uuid";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            sb.Append(string.Format("{0:x2}", data[i]));
            if (i == 3 || i == 5 || i == 7 || i == 9)
                sb.Append("-");
        }
        return sb.ToString();
    }

    public static string toASCII(byte[] data)
    {
        StringBuilder sb = new StringBuilder();
        foreach (var b in data)
            sb.Append((char)b);
        return sb.ToString();
    }

    ////////////////////////////////////////////////////////////////////////
    // File Utilities
    ////////////////////////////////////////////////////////////////////////

    public static async Task<string> readAllTextFromFile(string pathname)
    {
        logger.debug("readAllTextFromFile: start");
        string text = "";

        #if ANDROID
        logger.debug($"readAllTextFromFile: opening {pathname}");
        var infile = File.OpenRead(pathname);

        logger.debug($"readAllTextFromFile: reading {pathname}");
        using (StreamReader sr = new StreamReader(infile))
        { 
            text = await sr.ReadToEndAsync();
        }
        #endif

        logger.debug("readAllTextFromFile: done");
        return text;
    }

    ////////////////////////////////////////////////////////////////////////
    // Bluetooth Utilities
    ////////////////////////////////////////////////////////////////////////

    public static bool bluetoothEnabled()
    {
        logger.debug("Util.bluetoothEnabled: start");
        bool enabled = false;
#if ANDROID
        logger.debug("Util.bluetoothEnabled: getting bluetoothManager");
        var bluetoothManager = (Android.Bluetooth.BluetoothManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.BluetoothService);
        logger.debug("Util.bluetoothEnabled: getting bluetoothAdapter");
        var bluetoothAdapter = bluetoothManager.Adapter;
        logger.debug("Util.bluetoothEnabled: checking enabled");
        enabled = bluetoothAdapter.IsEnabled;
#endif
        logger.debug($"Util.bluetoothEnabled: returning {enabled}");
        return enabled;
    }

    public static bool enableBluetooth(bool flag)
    {
        logger.error($"Util.enableBluetooth({flag}): start");
    #if ANDROID
        logger.error($"Util.enableBluetooth({flag}): generating intent");
        var request = flag ? Android.Bluetooth.BluetoothAdapter.ActionRequestEnable 
                           : Android.Bluetooth.BluetoothAdapter.ActionRequestDiscoverable;
        var intent = new Android.Content.Intent(request);
        intent.SetFlags(Android.Content.ActivityFlags.NewTask);
        logger.error($"Util.enableBluetooth({flag}): sending intent");
        Android.App.Application.Context.StartActivity(intent);
    #endif
        logger.error($"Util.enableBluetooth({flag}): done");
        return true;
    }

    public static async Task<bool> enableAutoSave()
    {
        return await StorageHelper.GetManageAllFilesPermission();
    }

    ////////////////////////////////////////////////////////////////////////
    // GUI Utilities
    ////////////////////////////////////////////////////////////////////////

    // View is there for iOS (Android doesn't need it)
    public static async void toast(string msg, View view = null)
    {
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        ToastDuration duration = ToastDuration.Short;
        double fontSize = 14;
        var toast = CommunityToolkit.Maui.Alerts.Toast.Make(msg, duration, fontSize);
        await toast.Show(cancellationTokenSource.Token);
    }

    public static byte[] truncateArray(byte[] src, int len)
    {
        if (src == null)
            return null;

        if (src.Length <= len)
            return src;

        byte[] tmp = new byte[len];
        Array.Copy(src, tmp, len);
        return tmp;
    }
}

public class AirPLS
{
    public static double[] smooth(double[] spectrum, double smoothnessParam = 100, int maxIterations = 10, double convergenceThreshold = 0.001, bool verbose = false, int startIndex = 0, int endIndex = 0)
    {

        //Logger.getInstance().info("smoothing sample and library");
        double[] clipped = new double[spectrum.Length];

        if (startIndex != endIndex)
        {
            //Logger.getInstance().info("creating clipped array");

            clipped = new double[endIndex - startIndex + 1];
            for (int i = startIndex; i <= endIndex; ++i)
                clipped[i - startIndex] = spectrum[i];
        }
        else
        {
            Array.Copy(spectrum, clipped, spectrum.Length);
        }

        WhittakerSmoother smoother = new WhittakerSmoother(clipped, smoothnessParam, 2);
        double[] weights = new double[clipped.Length];
        double[] baseline = new double[clipped.Length];
        double[] corrected = new double[clipped.Length];
        double totalIntensity = 0;
        for (int i = 0; i < clipped.Length; i++)
        {
            weights[i] = 1;
            totalIntensity += Math.Abs(clipped[i]);
        }

        for (int i = 0; i < maxIterations; ++i)
        {
            //Logger.getInstance().info("trying smooth iteration {0}", i);
            baseline = smoother.smooth(weights);
            double[] baselineError = new double[clipped.Length];
            bool[] mask = new bool[clipped.Length];
            double totalError = 0;
            for (int j = 0; j < clipped.Length; ++j)
            {
                corrected[j] = clipped[j] - baseline[j];

                if (corrected[j] < 0)
                {
                    baselineError[j] = -1 * corrected[j];
                    mask[j] = true;
                    totalError += baselineError[j];
                }
                else
                {
                    baselineError[j] = corrected[j];
                    mask[j] = false;
                }
            }


            double convergence = totalError / totalIntensity;
            if (convergence < convergenceThreshold)
                break;

            for (int j = 0; j < baselineError.Length; ++j)
            {
                baselineError[j] = baselineError[j] / totalError;

                if (!mask[j])
                    weights[j] = 0;
                else
                {
                    weights[j] = Math.Exp((i + 1) * baselineError[j]);
                }
            }

            weights[0] = weights[weights.Length - 1] = Math.Exp((i + 1) * baselineError.Min());
        }

        double[] final = new double[clipped.Length];

        Array.Copy(clipped, final, final.Length);
        for (int j = 0; j < final.Length; ++j)
            final[j] -= baseline[j];

        return final;
    }


}

public class WhittakerSmoother
{
    Vector<double> spectrumVec;
    CSparseMatrix storedSmooth;

    public WhittakerSmoother(double[] signal, double smoothnessParam, int derivativeOrder = 1)
    {
        //Logger.getInstance().debug("creating initial stored smooth");
        spectrumVec = CreateVector.DenseOfArray(signal);
        
        double[] diffArray = new double[derivativeOrder * 2 + 1];
        double[] diffXs = new double[derivativeOrder * 2 + 1];
        for (int i = 0; i < diffXs.Length; ++i)
            diffXs[i] = i;

        diffArray[derivativeOrder] = 1;
        diffArray = NumericalMethods.derivative(diffXs, diffArray, derivativeOrder);

        CSparse.Double.DenseMatrix mat = new CSparse.Double.DenseMatrix(signal.Length, derivativeOrder + 1);
        for (int i = 0; i < signal.Length; i++)
        {
            for (int j = 0; j < derivativeOrder + 1; j++)
            {
                //smoothingMatrix.s
                mat[i, j] = diffArray[j];
            }
        }

        //Logger.getInstance().debug("creating sparse matrix");
        CSparseMatrix smoothingMatrix = (CSparseMatrix)CSparseMatrix.OfDiagonals(mat, [0, 1, 2], signal.Length - derivativeOrder, signal.Length);
        //Logger.getInstance().debug("transpose and multiply matrix");
        storedSmooth = (CSparseMatrix)smoothingMatrix.Transpose().Multiply(smoothingMatrix);

        //Logger.getInstance().debug("diagonal multiply matrix");
        CSparseMatrix scaleMatrix = (CSparseMatrix)CSparseMatrix.CreateDiagonal(storedSmooth.ColumnCount, smoothnessParam);

        storedSmooth = (CSparseMatrix)scaleMatrix.Multiply(storedSmooth);

        //newStoredSmooth = (CSparseMatrix)CSparseMatrix.OfArray(storedSmooth.ToArray());
        //Logger.getInstance().debug("finalized initial stored smooth");
    }
    
    public double[] smooth(double[] weights)
    {
        //Logger.getInstance().debug("setting up weight matrix");
        CSparseMatrix weightID =  (CSparseMatrix)CSparseMatrix.OfDiagonalArray(weights);

        //Logger.getInstance().debug("creating dense vector");
        Vector<double> weightVec = CreateVector.DenseOfArray(weights);

        //Logger.getInstance().debug("creating sparse matrix");
        CSparseMatrix A = (CSparseMatrix)weightID.Add(storedSmooth);
        //Logger.getInstance().debug("multiplying vector");
        Vector<double> B = weightVec.PointwiseMultiply(spectrumVec);
        //Logger.getInstance().debug("converting sparse matrix");
        SparseLU solver = SparseLU.Create(A, ColumnOrdering.MinimumDegreeAtPlusA);

        //Logger.getInstance().debug("solving final sparse matrix");
        Vector<double> background = solver.Solve(B);
        //Logger.getInstance().debug("returning solved matrix");
        return background.ToArray();
    }
}

public class SparseLU : ISolver<double>
{
    int n;
    CSparseLU lu;

    private SparseLU(CSparseLU lu, int n)
    {
        this.n = n;
        this.lu = lu;
    }

    /// <summary>
    /// Compute the sparse LU factorization for given matrix.
    /// </summary>
    /// <param name="matrix">The matrix to factorize.</param>
    /// <param name="ordering">The column ordering method to use.</param>
    /// <param name="tol">Partial pivoting tolerance (form 0.0 to 1.0).</param>
    /// <returns>Sparse LU factorization.</returns>
    public static SparseLU Create(SparseMatrix matrix, CSparse.ColumnOrdering ordering,
        double tol = 1.0)
    {
        int n = matrix.RowCount;

        // Check for proper dimensions.
        if (n != matrix.ColumnCount)
        {
            //throw new ArgumentException(Resources.MatrixMustBeSquare);
        }

        // Get CSR storage.
        var storage = (SparseCompressedRowMatrixStorage<double>)matrix.Storage;

        // Create CSparse matrix.
        var A = new CSparseMatrix(n, n);

        // Assign storage arrays.
        A.ColumnPointers = storage.RowPointers;
        A.RowIndices = storage.ColumnIndices;
        A.Values = storage.Values;

        return new SparseLU(CSparseLU.Create(A, ordering, tol), n);
    }
    
    public static SparseLU Create(CSparseMatrix matrix, CSparse.ColumnOrdering ordering,
        double tol = 1.0)
    {
        int n = matrix.RowCount;

        // Check for proper dimensions.
        if (n != matrix.ColumnCount)
        {
            //throw new ArgumentException(Resources.MatrixMustBeSquare);
        }

        return new SparseLU(CSparseLU.Create(matrix, ordering, tol), n);
    }

    /// <summary>
    /// Solves a system of linear equations, <c>Ax = b</c>, with A LU factorized.
    /// </summary>
    /// <param name="input">The right hand side vector, <c>b</c>.</param>
    /// <param name="result">The left hand side vector, <c>x</c>.</param>
    public void Solve(Vector<double> input, Vector<double> result)
    {
        // Check for proper arguments.
        if (input == null)
        {
            throw new ArgumentNullException("input");
        }

        if (result == null)
        {
            throw new ArgumentNullException("result");
        }

        // Check for proper dimensions.
        if (input.Count != result.Count)
        {
            //throw new ArgumentException(Resources.ArgumentVectorsSameLength);
        }

        if (input.Count != n)
        {
            throw new ArgumentException("Dimensions don't match", "input");
        }

        var b = input.Storage as DenseVectorStorage<double>;
        var x = result.Storage as DenseVectorStorage<double>;

        if (b == null || x == null)
        {
            throw new NotSupportedException("Expected dense vector storage.");
        }

        lu.Solve(b.Data, x.Data);
    }


    public Vector<double> Solve(Vector<double> input)
    {
        var result = Vector<double>.Build.Dense(input.Count);

        Solve(input, result);

        return result;
    }

    public void Solve(MathNet.Numerics.LinearAlgebra.Matrix<double> input, MathNet.Numerics.LinearAlgebra.Matrix<double> result)
    {
        throw new NotImplementedException();
    }

    public MathNet.Numerics.LinearAlgebra.Matrix<double> Solve(MathNet.Numerics.LinearAlgebra.Matrix<double> input)
    {
        throw new NotImplementedException();
    }
}
