using EnlightenMAUI.Models;
using Intents;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Transforms.Onnx;
using Newtonsoft.Json;
using NumSharp;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Telerik.Maui.Controls.Scheduler;
namespace EnlightenMAUI.Platforms;

public static class StorageHelper
{
    public static async Task<bool> GetManageAllFilesPermission()
    {
        return false;
    }

    public static void OnActivityResult()
    {
        //GetPermissionTask?.SetResult(AndrOS.Environment.IsExternalStorageManager);
    }
}

internal class PlatformUtil
{
    static Logger logger = Logger.getInstance();
    public static bool transformerLoaded = false;
    public static bool simpleTransformerLoaded = false;
    public static bool complexTransformerLoaded = false;
    public static int REQUEST_TREE = 85;
    static Dictionary<string, double[]> correctionFactors = new Dictionary<string, double[]>();


    static string savePath;
    static string userLibraryPath;
    static string configurationPath;
    static string autoSavePath;

    public static void RequestSelectLogFolder()
    {

    }

    public static void OpenLogFileForWriting(string file_name, string file_contents)
    {

    }

    public static bool HasFolderBeenSelectedAndPermissionsGiven()
    {
        return false;
    }

    public async static Task loadONNXModel(string extension, string correctionPath)
    {

    }



    public static async Task<Dictionary<string, Measurement>> findUserFiles(Spectrometer spec)
    {
        return null;
    }


    static async Task loadCorrections(string file)
    {
        logger.info("start loading correction factors from {0}", file);

        SimpleCSVParser parser = new SimpleCSVParser();
        Stream s = System.IO.File.OpenRead(file);
        StreamReader sr = new StreamReader(s);
        string blob = await sr.ReadToEndAsync();

        try
        {
            correctionFactors = JsonConvert.DeserializeObject<Dictionary<string, double[]>>(blob);
            logger.info("finished loading correction factor from {0}", file);
        }
        catch (Exception ex)
        {
            logger.error("correction load failed with error {0}", ex.Message);
        }
    }

    public static double[] ProcessBackground(double[] wavenumbers, double[] counts, string serial, double fwhm, int roiStart, bool useSimple = false)
    {
        return null;
    }

    static double[] deconvoluteSpectrum(double[] wnOut, double[] spectrum, double fwhm)
    {
        logger.info("entered deconvolution");

        double padWidth = 3;
        int maxIterations = 25;
        double[] wavenumberPerPixel = new double[wnOut.Length];
        for (int i = 1; i < wnOut.Length; ++i)
        {
            wavenumberPerPixel[i] = wnOut[i] - wnOut[i - 1];
        }

        double avgPixelFWHM = fwhm / wavenumberPerPixel.Average();
        int padPixels = (int)Math.Ceiling(padWidth * avgPixelFWHM);

        double[] wavenumberPerPixelPadded = new double[wavenumberPerPixel.Length + 2 * padPixels];
        for (int i = 0; i < padPixels; ++i)
        {
            wavenumberPerPixelPadded[i] = wavenumberPerPixel.First();
            wavenumberPerPixelPadded[wavenumberPerPixelPadded.Length - 1 - i] = wavenumberPerPixel.Last();
        }

        for (int i = 0; i < wavenumberPerPixel.Length; ++i)
        {
            wavenumberPerPixelPadded[i + padPixels] = wavenumberPerPixel[i];
        }

        double[] pixelFWHM = new double[wavenumberPerPixelPadded.Length];
        for (int i = 0; i < pixelFWHM.Length; ++i)
        {
            pixelFWHM[i] = fwhm / wavenumberPerPixelPadded[i];
        }

        double[] pixelSigma = new double[wavenumberPerPixelPadded.Length];
        double[] pixelSigma2 = new double[wavenumberPerPixelPadded.Length];
        for (int i = 0; i < pixelFWHM.Length; ++i)
        {
            pixelSigma[i] = pixelFWHM[i] / (2 * Math.Sqrt(2 * Math.Log(2)));
            pixelSigma2[i] = pixelSigma[i] * pixelSigma[i];
        }

        int numPixelPadded = wavenumberPerPixelPadded.Length;

        double[] spectrumPadded = new double[wavenumberPerPixel.Length + 2 * padPixels];
        for (int i = 0; i < padPixels; ++i)
        {
            spectrumPadded[i] = spectrum.First();
            spectrumPadded[spectrumPadded.Length - 1 - i] = spectrum.Last();
        }

        for (int i = 0; i < spectrum.Length; ++i)
        {
            spectrumPadded[i + padPixels] = spectrum[i];
        }

        double[] pixels = new double[numPixelPadded];
        for (int i = 0; i < pixels.Length; ++i)
        {
            pixels[i] = i;
        }

        logger.info("non-matrix packing complete");

        MathNet.Numerics.LinearAlgebra.Matrix<double> resolutionH = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.Dense(numPixelPadded, numPixelPadded);
        logger.info("matrix allocated");

        foreach (int row in pixels)
        {
            double[] resolutionSpectrum = new double[pixels.Length];

            logger.info("row {0} contruction started", row);
            if (pixelSigma2[row] != 0)
            {
                for (int i = 0; i < resolutionSpectrum.Length; ++i)
                {
                    double inner = -0.5 * Math.Pow((pixels[i] - row), 2) / pixelSigma2[row];
                    resolutionSpectrum[i] = Math.Exp(inner);
                }
            }
            else
            {
                for (int i = 0; i < resolutionSpectrum.Length; ++i)
                {
                    resolutionSpectrum[i] = 1;
                }
            }
            logger.info("row {0} construction ended", row);

            double sum = resolutionSpectrum.Sum();

            for (int i = 0; i < resolutionSpectrum.Length; ++i)
            {
                resolutionH[row, i] = resolutionSpectrum[i] / sum;
            }

            logger.info("row {0} fill ended", row);
        }
        logger.info("matrix packed");

        double[] origSpectrumPadded = spectrumPadded;
        double epsilon = 1e-5;
        double[] spectrumDeconvPadded = new double[origSpectrumPadded.Length];
        for (int i = 0; i < spectrumDeconvPadded.Length; ++i)
        {
            spectrumDeconvPadded[i] = Math.Max(origSpectrumPadded[i], epsilon);
        }

        var hTrans = resolutionH.Transpose();
        logger.info("matrix transposed");

        for (int i = 0; i < maxIterations; ++i)
        {
            var hTimesX = resolutionH * MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(spectrumDeconvPadded);

            logger.info("iter {0} multiply complete", i);

            double[] yOverHTimesX = new double[hTimesX.Count];
            for (int j = 0; j < hTimesX.Count - 1; ++j)
            {
                yOverHTimesX[j + 1] = origSpectrumPadded[j] / hTimesX[j];
            }
            yOverHTimesX[0] = yOverHTimesX[1];

            var yOverHXVec = MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(yOverHTimesX);

            double[] fullSum = new double[yOverHXVec.Count];


            for (int j = 0; j < yOverHXVec.Count; ++j)
            {
                fullSum[j] = yOverHXVec.DotProduct(hTrans.Row(j));
                spectrumDeconvPadded[j] = spectrumDeconvPadded[j] * fullSum[j];
                spectrumDeconvPadded[j] = Math.Max(epsilon, spectrumDeconvPadded[j]);
            }

            logger.info("iter {0} dotprods complete", i);
        }

        double[] spectrumDeconv = new double[spectrum.Length];
        for (int i = 0; i < spectrum.Length; ++i)
            spectrumDeconv[i] = spectrumDeconvPadded[i + padPixels];

        logger.info("decon complete");

        return spectrumDeconv;
    }

    static double[] numpyDecon(double[] wnOut, double[] spectrum, double fwhm)
    {
        logger.info("entered deconvolution");

        double padWidth = 3;
        int maxIterations = 25;

        var cleanedSpectrum = np.array(spectrum);

        double[] wavenumberPerPixel = new double[wnOut.Length];
        for (int i = 1; i < wnOut.Length; ++i)
        {
            wavenumberPerPixel[i] = wnOut[i] - wnOut[i - 1];
        }

        var npwnpp = np.array(wavenumberPerPixel);
        var npavgwn = np.mean(npwnpp);

        double avgPixelFWHM = fwhm / wavenumberPerPixel.Average();
        int padPixels = (int)Math.Ceiling(padWidth * avgPixelFWHM);

        double[] wavenumberPerPixelPadded = new double[wavenumberPerPixel.Length + 2 * padPixels];
        for (int i = 0; i < padPixels; ++i)
        {
            wavenumberPerPixelPadded[i] = wavenumberPerPixel.First();
            wavenumberPerPixelPadded[wavenumberPerPixelPadded.Length - 1 - i] = wavenumberPerPixel.Last();
        }

        var wnppPad = np.array(wavenumberPerPixelPadded);

        double[] pixelFWHM = new double[wavenumberPerPixelPadded.Length];
        for (int i = 0; i < pixelFWHM.Length; ++i)
        {
            pixelFWHM[i] = fwhm / wavenumberPerPixelPadded[i];
        }

        double[] pixelSigma = new double[wavenumberPerPixelPadded.Length];
        double[] pixelSigma2 = new double[wavenumberPerPixelPadded.Length];
        for (int i = 0; i < pixelFWHM.Length; ++i)
        {
            pixelSigma[i] = pixelFWHM[i] / (2 * Math.Sqrt(2 * Math.Log(2)));
            pixelSigma2[i] = pixelSigma[i] * pixelSigma[i];
        }


        for (int i = 0; i < wavenumberPerPixel.Length; ++i)
        {
            wavenumberPerPixelPadded[i + padPixels] = wavenumberPerPixel[i];
        }

        var npPFWHM = fwhm / wnppPad;
        var npPSigma = npPFWHM / (2 * Math.Sqrt(2 * Math.Log(2)));
        var npPSigma2 = npPSigma * npPSigma;

        int numPixelPadded = wavenumberPerPixelPadded.Length;

        double[] spectrumPadded = new double[wavenumberPerPixel.Length + 2 * padPixels];
        for (int i = 0; i < padPixels; ++i)
        {
            spectrumPadded[i] = spectrum.First();
            spectrumPadded[spectrumPadded.Length - 1 - i] = spectrum.Last();
        }

        for (int i = 0; i < spectrum.Length; ++i)
        {
            spectrumPadded[i + padPixels] = spectrum[i];
        }

        double[] pixels = new double[numPixelPadded];
        for (int i = 0; i < pixels.Length; ++i)
        {
            pixels[i] = i;
        }

        var npPix = np.arange(numPixelPadded);
        var npResH = np.zeros(numPixelPadded, numPixelPadded);
        foreach (var row in npPix)
        {
            int rowN = (int)row;

            double[] resolutionSpectrum = new double[pixels.Length];

            logger.info("row {0} contruction started", row);
            if (pixelSigma2[rowN] != 0)
            {
                for (int i = 0; i < resolutionSpectrum.Length; ++i)
                {
                    double inner = -0.5 * Math.Pow((pixels[i] - rowN), 2) / pixelSigma2[rowN];
                    resolutionSpectrum[i] = Math.Exp(inner);
                }
            }
            else
            {
                for (int i = 0; i < resolutionSpectrum.Length; ++i)
                {
                    resolutionSpectrum[i] = 1;
                }
            }
            logger.info("row {0} construction ended", row);

            double sum = resolutionSpectrum.Sum();
            for (int i = 0; i < resolutionSpectrum.Length; ++i)
                resolutionSpectrum[i] = resolutionSpectrum[i] / sum;

            npResH.SetData(resolutionSpectrum, [rowN]);
        }


        double[] origSpectrumPadded = spectrumPadded;
        double epsilon = 1e-5;
        double[] spectrumDeconvPadded = new double[origSpectrumPadded.Length];
        for (int i = 0; i < spectrumDeconvPadded.Length; ++i)
        {
            spectrumDeconvPadded[i] = Math.Max(origSpectrumPadded[i], epsilon);
        }

        var hTrans = np.transpose(npResH);

        var npOrig = np.array(origSpectrumPadded);
        var npSDP = np.array(spectrumDeconvPadded);

        logger.info("matrix transposed");

        for (int i = 0; i < maxIterations; ++i)
        {
            var hTimesX = np.matmul(npResH, npSDP);
            var yOverHtimesX = npOrig / hTimesX;
            for (int j = 0; j < yOverHtimesX.size; ++j)
            {
                if (j == 0)
                    yOverHtimesX[j] = yOverHtimesX[j];
                else
                    yOverHtimesX[j] = yOverHtimesX[j - 1];

            }

            var fullSum = np.dot(hTrans, yOverHtimesX);
            npSDP = spectrumDeconvPadded * fullSum;
            for (int j = 0; j < npSDP.size; ++j)
            {
                if (npSDP[j] < epsilon)
                    npSDP[j] = epsilon;
            }

            logger.info("iter {0} dotprods complete", i);
        }

        double[] spectrumDeconv = new double[spectrum.Length];
        for (int i = 0; i < spectrum.Length; ++i)
            spectrumDeconv[i] = npSDP[i + padPixels];

        logger.info("decon complete");

        return spectrumDeconv;
    }

    static double[] customDeconvoluteSpectrum(double[] wnOut, double[] spectrum, double fwhm)
    {
        Logger logger = Logger.getInstance();

        logger.info("entered deconvolution");

        double padWidth = 3;
        int maxIterations = 25;
        double[] wavenumberPerPixel = new double[wnOut.Length];
        for (int i = 1; i < wnOut.Length; ++i)
        {
            wavenumberPerPixel[i] = wnOut[i] - wnOut[i - 1];
        }

        double avgPixelFWHM = fwhm / wavenumberPerPixel.Average();
        int padPixels = (int)Math.Ceiling(padWidth * avgPixelFWHM);

        double[] wavenumberPerPixelPadded = new double[wavenumberPerPixel.Length + 2 * padPixels];
        for (int i = 0; i < padPixels; ++i)
        {
            wavenumberPerPixelPadded[i] = wavenumberPerPixel.First();
            wavenumberPerPixelPadded[wavenumberPerPixelPadded.Length - 1 - i] = wavenumberPerPixel.Last();
        }

        for (int i = 0; i < wavenumberPerPixel.Length; ++i)
        {
            wavenumberPerPixelPadded[i + padPixels] = wavenumberPerPixel[i];
        }

        double[] pixelFWHM = new double[wavenumberPerPixelPadded.Length];
        for (int i = 0; i < pixelFWHM.Length; ++i)
        {
            pixelFWHM[i] = fwhm / wavenumberPerPixelPadded[i];
        }

        double[] pixelSigma = new double[wavenumberPerPixelPadded.Length];
        double[] pixelSigma2 = new double[wavenumberPerPixelPadded.Length];
        for (int i = 0; i < pixelFWHM.Length; ++i)
        {
            pixelSigma[i] = pixelFWHM[i] / (2 * Math.Sqrt(2 * Math.Log(2)));
            pixelSigma2[i] = pixelSigma[i] * pixelSigma[i];
        }

        int numPixelPadded = wavenumberPerPixelPadded.Length;

        double[] spectrumPadded = new double[wavenumberPerPixel.Length + 2 * padPixels];
        for (int i = 0; i < padPixels; ++i)
        {
            spectrumPadded[i] = spectrum.First();
            spectrumPadded[spectrumPadded.Length - 1 - i] = spectrum.Last();
        }

        for (int i = 0; i < spectrum.Length; ++i)
        {
            spectrumPadded[i + padPixels] = spectrum[i];
        }

        double[] pixels = new double[numPixelPadded];
        for (int i = 0; i < pixels.Length; ++i)
        {
            pixels[i] = i;
        }

        //logger.info("non-matrix packing complete");

        //MathNet.Numerics.LinearAlgebra.Matrix<double> resolutionH = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.Dense(numPixelPadded, numPixelPadded);
        double[][] resolutionH = new double[numPixelPadded][];
        double[][] hTrans = new double[numPixelPadded][];
        for (int i = 0; i < numPixelPadded; ++i)
        {
            resolutionH[i] = new double[numPixelPadded];
            hTrans[i] = new double[numPixelPadded];
        }

        logger.info("matrix allocated");

        foreach (int row in pixels)
        {
            double[] resolutionSpectrum = new double[pixels.Length];

            //logger.info("row {0} contruction started", row);
            if (pixelSigma2[row] != 0)
            {
                for (int i = 0; i < resolutionSpectrum.Length; ++i)
                {
                    double inner = -0.5 * Math.Pow((pixels[i] - row), 2) / pixelSigma2[row];
                    resolutionSpectrum[i] = Math.Exp(inner);
                }
            }
            else
            {
                for (int i = 0; i < resolutionSpectrum.Length; ++i)
                {
                    resolutionSpectrum[i] = 1;
                }
            }
            //logger.info("row {0} construction ended", row);

            double sum = resolutionSpectrum.Sum();

            for (int i = 0; i < resolutionSpectrum.Length; ++i)
            {
                resolutionH[row][i] = hTrans[i][row] = resolutionSpectrum[i] / sum;
            }

            //logger.info("row {0} fill ended", row);
        }
        logger.info("matrix packed");

        double[] origSpectrumPadded = spectrumPadded;
        double epsilon = 1e-5;
        double[] spectrumDeconvPadded = new double[origSpectrumPadded.Length];
        for (int i = 0; i < spectrumDeconvPadded.Length; ++i)
        {
            spectrumDeconvPadded[i] = Math.Max(origSpectrumPadded[i], epsilon);
        }


        for (int i = 0; i < maxIterations; ++i)
        {
            //var hTimesX = resolutionH * MathNet.Numerics.LinearAlgebra.Vector<double>.Build.DenseOfArray(spectrumDeconvPadded);
            double[] hTimesX = new double[numPixelPadded];
            for (int j = 0; j < numPixelPadded; ++j)
            {
                double sum = 0;

                for (int k = 0; k < numPixelPadded; ++k)
                {
                    sum += spectrumDeconvPadded[k] * resolutionH[j][k];
                }

                hTimesX[j] = sum;
            }


            //logger.info("iter {0} multiply complete", i);

            double[] yOverHTimesX = new double[hTimesX.Length];
            for (int j = 0; j < hTimesX.Length - 1; ++j)
            {
                yOverHTimesX[j + 1] = origSpectrumPadded[j] / hTimesX[j];
            }
            yOverHTimesX[0] = yOverHTimesX[1];

            double[] fullSum = new double[spectrumDeconvPadded.Length];


            for (int j = 0; j < spectrumDeconvPadded.Length; ++j)
            {
                double sum = 0;

                for (int k = 0; k < numPixelPadded; ++k)
                {
                    sum += yOverHTimesX[k] * hTrans[j][k];
                }
                fullSum[j] = sum;


                spectrumDeconvPadded[j] = spectrumDeconvPadded[j] * fullSum[j];
                spectrumDeconvPadded[j] = Math.Max(epsilon, spectrumDeconvPadded[j]);
            }

            //logger.info("iter {0} dotprods complete", i);
        }

        double[] spectrumDeconv = new double[spectrum.Length];
        for (int i = 0; i < spectrum.Length; ++i)
            spectrumDeconv[i] = spectrumDeconvPadded[i + padPixels];

        logger.info("decon complete");

        return spectrumDeconv;
    }

    public static string getLibraryFilenames()
    {
        return null; // YOU ARE HERE
    }
    public static string getLibraryPath()
    {
        return null;
    }

    // logger:  /storage/emulated/0/Android/data/com.wasatchphotonics.enlightenmaui/files/Documents/2024-04-30/enlighten-20240430-154219-290237-WP-01647.csv
    // PC: \Internal shared storage\Android\data\com.wasatchphotonics.enlightenmaui\files\Documents\2024-04-30
    public static string getSavePath()
    {
        return null;
    }

    public static string getUserLibraryPath()
    {
        return null;
    }

    public static string getConfigFilePath()
    {
        return null;
    }

    public static string getAutoSavePath(bool highLevelAutoSave)
    {
        return null;
    }
    public static List<string> getSubLibraries()
    {
        return null;
    }
    public async static Task<Dictionary<string, Measurement>> loadFiles(string root, Dictionary<string, Measurement> library, Dictionary<string, double[]> originalRaws, Dictionary<string, double[]> originalDarks, bool doDecon = true, string correctionFileName = "etalon_correction.json")
    {
        return null;
    }

    static bool writeable(string path)
    {
        return false;
    }


}

