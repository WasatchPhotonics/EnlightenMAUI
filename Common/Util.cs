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
using Accord.Math.Decompositions;

namespace EnlightenMAUI.Common;

public static class StorageHelper
{
    public const int RequestCode = 2296;
    private static TaskCompletionSource<bool>? GetPermissionTask { get; set; }

    public static async Task<bool> GetManageAllFilesPermission()
    {
        if (!Android.OS.Environment.IsExternalStorageManager)
        {
            try
            {
                Android.Net.Uri uri = Android.Net.Uri.Parse("package:" + Platform.CurrentActivity.ApplicationInfo.PackageName);

                GetPermissionTask = new();
                Intent intent = new(global::Android.Provider.Settings.ActionManageAppAllFilesAccessPermission, uri);
                Platform.CurrentActivity.StartActivityForResult(intent, RequestCode);
            }
            catch (Exception ex)
            {
                // Handle Exception
            }

            return await GetPermissionTask.Task;
        }

        else
            return true;
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


public class LUDecomposition
{
    private double[][] decomposition = null;
    private int m = 0;
    private int n = 0;
    private int[] pivotVector = null;
    private int pivotSign = 1;

    public bool MatrixSingular
    {
        get
        {
            for (int j = 0; j < this.n; j++)
                if (0.0d == this.decomposition[j][j])
                    return true;
            return false;
        }
    }

    public double Determinant
    {
        get
        {
            if (m != n)
                return 0.0;
            double determinant = pivotSign;
            for (int i = 0; i < this.m; i++)
                determinant *= decomposition[i][i];
            return determinant;
        }
    }

    public WMatrix L
    {
        get
        {
            WMatrix Lmatrix = new WMatrix(this.m, this.n);
            double[][] L = Lmatrix.Array;
            for (int i = 0; i < this.m; i++)
                for (int j = 0; j < this.n; j++)
                    if (i > j)
                        L[i][j] = this.decomposition[i][j];
                    else if (i == j)
                        L[i][j] = 1;
                    else
                        L[i][j] = 0;
            return Lmatrix;
        }
    }

    public WMatrix U
    {
        get
        {
            WMatrix Umatrix = new WMatrix(n, n);
            double[][] U = Umatrix.Array;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    if (i <= j)
                        U[i][j] = decomposition[i][j];
                    else
                        U[i][j] = 0;
            return Umatrix;
        }
    }

    virtual public int[] Pivot
    {
        get
        {
            int[] retval = new int[m];
            for (int i = 0; i < retval.Length; i++)
                retval[i] = pivotVector[i];
            return retval;
        }
    }

    public LUDecomposition(WMatrix matrix)
    {
        decomposition = matrix.Duplicate.Array;
        m = matrix.RowCount;
        n = matrix.ColumnCount;

        pivotVector = new int[m];
        for (int i = 0; i < pivotVector.Length; i++)
            pivotVector[i] = i;

        for (int j = 0; j < n; j++)
        {
            for (int i = 0; i < m; i++)
            {
                int minimum = Math.Min(i, j);
                double product = 0.0d;
                for (int k = 0; k < minimum; k++)
                    product += decomposition[i][k] * decomposition[k][j];
                this.decomposition[i][j] -= product;
            }

            int pivot = j;
            for (int i = j + 1; i < m; i++)
                if (Math.Abs(decomposition[i][j]) > Math.Abs(decomposition[pivot][j]))
                    pivot = i;

            if (pivot != j)
            {
                for (int k = 0; k < n; k++)
                {
                    double temp = decomposition[pivot][k];
                    decomposition[pivot][k] = decomposition[j][k];
                    decomposition[j][k] = temp;
                }

                int tempPivot = pivotVector[pivot];
                pivotVector[pivot] = pivotVector[j];
                pivotVector[j] = tempPivot;
                pivotSign = -pivotSign;
            }

            if (j < m && decomposition[j][j] != 0)
                for (int i = j + 1; i < m; i++)
                    decomposition[i][j] /= decomposition[j][j];
        }
    }

    public double[] solve(double[] b)
    {
        int m = pivotVector.Length;
        double[] bp = new double[m];

        for (int row = 0; row < m; row++)
            bp[row] = b[pivotVector[row]];

        for (int col = 0; col < m; col++)
        {
            double bpCol = bp[col];
            for (int i = col + 1; i < m; i++)
                bp[i] -= bpCol * decomposition[i][col];
        }

        for (int col = m - 1; col >= 0; col--)
        {
            bp[col] /= decomposition[col][col];
            double bpCol = bp[col];
            for (int i = 0; i < col; i++)
                bp[i] -= bpCol * decomposition[i][col];
        }

        return bp;
    }

    public WMatrix solveForMatrix(WMatrix B)
    {
        if (B.RowCount != this.m)
            return new WMatrix(1, 1);
        if (true == this.MatrixSingular)
            return new WMatrix(1, 1);

        int nx = B.ColumnCount;
        WMatrix Xmatrix = B.getSubMatrixByRows(Pivot, 0, nx - 1);
        double[][] X = Xmatrix.Array;

        for (int k = 0; k < this.n; k++)
        {
            for (int i = k + 1; i < this.n; i++)
            {
                for (int j = 0; j < nx; j++)
                {
                    X[i][j] -= X[k][j] * this.decomposition[i][k];
                }
            }
        }

        for (int k = this.n - 1; k >= 0; k--)
        {
            for (int j = 0; j < nx; j++)
                X[k][j] /= this.decomposition[k][k];
            for (int i = 0; i < k; i++)
                for (int j = 0; j < nx; j++)
                    X[i][j] -= X[k][j] * this.decomposition[i][k];
        }
        return Xmatrix;
    }
}

public class WMatrix
{
    double[][] matrix = null;
    int m = 0;
    int n = 0;

    public WMatrix Duplicate
    {
        get
        {
            WMatrix retval = new WMatrix(this.m, this.n);
            double[][] values = retval.Array;
            for (int i = 0; i < this.m; i++)
                for (int j = 0; j < this.n; j++)
                    values[i][j] = this.matrix[i][j];
            return retval;
        }

    }

    public double[][] Array { get { return matrix; } }

    public int RowCount { get { return m; } }
    public int ColumnCount { get { return n; } }

    public double[] RowPackedCopy
    {
        get
        {
            double[] vals = new double[m * n];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < n; j++)
                    vals[i * n + j] = matrix[i][j];
            return vals;
        }

    }

    public double[] ColumnPackedCopy
    {
        get
        {
            double[] vals = new double[m * n];
            for (int i = 0; i < m; i++)
                for (int j = 0; j < n; j++)
                    vals[i + j * m] = matrix[i][j];
            return vals;
        }

    }

    public double Determinant { get { return LUDecomposition.Determinant; } }

    public WMatrix Inverse
    {
        get
        {
            WMatrix identity = NumericalMethods.getIdentityMatrix(this.m, this.n);
            LUDecomposition lud = LUDecomposition;
            if (!lud.MatrixSingular)
                return lud.solveForMatrix(identity);
            else
                throw new Exception("Non-invertable matrix");
        }
    }
    public LUDecomposition LUDecomposition { get { return new LUDecomposition(this); } }

    public WMatrix Transpose
    {
        get
        {
            WMatrix retval = new WMatrix(this.n, this.m);
            double[][] values = retval.Array;
            for (int i = 0; i < this.m; i++)
                for (int j = 0; j < this.n; j++)
                    values[j][i] = this.matrix[i][j];
            return retval;
        }
    }

    public WMatrix(int rows, int cols)
    {
        if (rows < 1 || cols < 1)
            throw new Exception("matrix dimensions must be positive");

        matrix = new double[rows][];
        for (int i = 0; i < rows; i++)
            matrix[i] = new double[cols];
        m = rows;
        n = cols;
    }

    public WMatrix(double[][] values)
    {
        m = values.Length;
        n = values[0].Length;
        matrix = new double[m][];
        for (int i = 0; i < m; i++)
            matrix[i] = new double[n];
        for (int i = 0; i < m; i++)
            if (values[i].Length != n)
                throw new ArgumentException("Seed value rows must all be the same length");
            else
                for (int j = 0; j < this.n; j++)
                    this.matrix[i][j] = values[i][j];
    }

    public double getElement(int i, int j)
    {
        return matrix[i][j];
    }

    public override bool Equals(Object o)
    {
        if (null == o || !(o is WMatrix))
            return false;
        WMatrix rhs = (WMatrix)o;
        if (m != rhs.m || n != rhs.n)
            return false;
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
                if (this.matrix[i][j] != rhs.matrix[i][j])
                    return false;
        return true;
    }

    public override int GetHashCode()
    {
        int hash = 13;
        hash = 17 * hash + matrix.GetHashCode();
        hash = 19 * hash + m;
        hash = 23 * hash + n;
        return hash;
    }

    public WMatrix getSubMatrix(int startRow, int endRow, int startCol, int endCol)
    {
        WMatrix retval = new WMatrix(endRow - startRow + 1, endCol - startCol + 1);
        double[][] values = retval.Array;
        for (int i = startRow; i <= endRow; i++)
            for (int j = startCol; j <= endCol; j++)
                values[i - startRow][j - startCol] = matrix[i][j];
        return retval;
    }

    public WMatrix getSubMatrixByRows(int[] rows, int startCol, int endCol)
    {
        WMatrix retval = new WMatrix(rows.Length, endCol - startCol + 1);
        double[][] values = retval.Array;
        for (int i = 0; i < rows.Length; i++)
            for (int j = startCol; j <= endCol; j++)
                values[i][j - startCol] = this.matrix[rows[i]][j];
        return retval;
    }

    public void setElement(int i, int j, double d) { matrix[i][j] = d; }

    public WMatrix plusMatrix(WMatrix rhs)
    {
        if (!doDimensionsMatch(rhs))
            throw new Exception("unequal dimensions");
        WMatrix retval = new WMatrix(m, n);
        double[][] values = retval.Array;
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
                values[i][j] = matrix[i][j] + rhs.matrix[i][j];
        return retval;
    }

    public WMatrix minusMatrix(WMatrix rhs)
    {
        if (!doDimensionsMatch(rhs))
            throw new Exception("unequal dimensions");
        WMatrix retval = new WMatrix(m, n);
        double[][] values = retval.Array;
        for (int i = 0; i < this.m; i++)
            for (int j = 0; j < this.n; j++)
                values[i][j] = this.matrix[i][j] - rhs.matrix[i][j];
        return retval;
    }

    public WMatrix timesScalar(double scalar)
    {
        WMatrix retval = new WMatrix(this.m, this.n);
        double[][] values = retval.Array;
        for (int i = 0; i < this.m; i++)
            for (int j = 0; j < this.n; j++)
                values[i][j] = this.matrix[i][j] * scalar;
        return retval;
    }

    public WMatrix timesMatrix(WMatrix rhs)
    {
        if (n != rhs.m)
            throw new Exception("matrix dimensions disagree");

        WMatrix retval = new WMatrix(m, rhs.n);
        double[][] values = retval.Array;
        for (int j = 0; j < rhs.n; j++)
            for (int i = 0; i < this.m; i++)
            {
                double product = 0;
                for (int k = 0; k < n; k++)
                    product += matrix[i][k] * rhs.matrix[k][j];
                values[i][j] = product;
            }
        return retval;
    }

    bool doDimensionsMatch(WMatrix rhs) { return m == rhs.m && n == rhs.n; }
}

public class LinearRegression
{
    public LinearRegression()
    {
    }

    public double[] computeLinearRegression(int maxOrder, double[] knownX, double[] knownY)
    {
        double[][] xVals = new double[knownX.Length][];
        for (int i = 0; i < knownX.Length; i++)
            xVals[i] = new double[maxOrder + 1];
        for (int i = 0; i < xVals.Length; i++)
            for (int j = 0; j < maxOrder + 1; j++)
                xVals[i][j] = Math.Pow(knownX[i], j);
        // Wrap the result into a WMatrix
        WMatrix X = new WMatrix(xVals);
        return computeLinearRegression(X, knownY);
    }

    public double[] computeLinearRegression(Int32[] orders, double[] knownX, double[] knownY)
    {
        Array.Sort(orders);
        for (int i = 1; i < orders.Length; i++)
            if (orders[i] == orders[i - 1])
                throw new ArgumentException("duplicate order");
        double[][] xVals = new double[knownX.Length][];
        for (int i2 = 0; i2 < knownX.Length; i2++)
            xVals[i2] = new double[orders.Length];
        for (int i = 0; i < xVals.Length; i++)
            for (int j = 0; j < orders.Length; j++)
                xVals[i][j] = Math.Pow(knownX[i], orders[j]);
        WMatrix X = new WMatrix(xVals);
        double[] packedCoeffs = computeLinearRegression(X, knownY);
        int maxOrder = orders[orders.Length - 1];
        double[] coeffs = new double[maxOrder + 1];
        for (int i = 0; i < packedCoeffs.Length; i++)
            coeffs[orders[i]] = packedCoeffs[i];
        return coeffs;
    }

    public double[] computeLinearRegression(WMatrix X, double[] knownY)
    {
        double[][] yVals = new double[knownY.Length][];
        for (int i = 0; i < yVals.Length; i++)
            yVals[i] = new double[] { knownY[i] };
        WMatrix Y = new WMatrix(yVals);
        WMatrix coeffs = computeLinearRegression(X, Y);
        double[][] cArray = coeffs.Array;
        double[] vec = new double[cArray.Length];
        for (int i = 0; i < vec.Length; i++)
            vec[i] = cArray[i][0];
        return vec;
    }

    public WMatrix computeLinearRegression(WMatrix X, WMatrix Y)
    {
        WMatrix Xt = X.Transpose;
        WMatrix XtX = Xt.timesMatrix(X);
        WMatrix XtXInv = XtX.Inverse;
        WMatrix XtXInvXt = XtXInv.timesMatrix(Xt);
        return XtXInvXt.timesMatrix(Y);
    }

    public double[] computeResiduals(double[] x, double[] y, double[] coefficients)
    {
        double[] retval = new double[y.Length];
        for (int i = 0; i < retval.Length; i++)
        {
            double predicted = NumericalMethods.evaluatePolynomial(x[i], coefficients);
            retval[i] = y[i] - predicted;
        }
        return retval;
    }

    public double computeRSquared(double[] x, double[] y, double[] coefficients)
    {
        double[] e = computeResiduals(x, y, coefficients);
        double yMean = NumericalMethods.average(y);

        double sumSquaredResiduals = 0;
        double sumSquaredDelta_y = 0;
        for (int i = 0; i < y.Length; i++)
        {
            sumSquaredResiduals += e[i] * e[i];
            sumSquaredDelta_y += (y[i] - yMean) * (y[i] - yMean);
        }
        double temp = sumSquaredResiduals / sumSquaredDelta_y;
        return 1.0d - temp;
    }
}
