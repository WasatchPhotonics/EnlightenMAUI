using Android;
using Android.Content.Res;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Onnx;
using Telerik.Maui.Controls.Scheduler;
using Microsoft.ML.OnnxRuntime.Tensors;
using AndroidX.AppCompat.Widget;
using EnlightenMAUI.Models;
using Android.Content;
using Android.OS;
using Android.Webkit;
using AndroidX.DocumentFile.Provider;
using Java.IO;
using Newtonsoft.Json;
using NumSharp;
namespace EnlightenMAUI.Platforms; 


internal class ModelInput
{
    [ColumnName("serving_default_input_layer:0")]
    [VectorType(1, 2376, 1)]
    public float[] spectrum { get; set; }
}

internal class Prediction
{
    [ColumnName("StatefulPartitionedCall_1:0")]
    [VectorType(1, 2008, 1)]
    public float[] spectrum { get; set; }

}

internal class PlatformUtil
{
    static Logger logger = Logger.getInstance();
    static MLContext mlContext = new MLContext();
    static PredictionEngine<ModelInput, Prediction> engine;
    static Dictionary<string, double[]> correctionFactors = new Dictionary<string, double[]>();
    static ITransformer transformer;
    public static bool transformerLoaded = false;
    public static int REQUEST_TREE = 85;

    static string savePath;
    static string userLibraryPath;
    static string autoSavePath;

    public static void RequestSelectLogFolder()
    {
        var current_activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocumentTree);
        intent.AddFlags(Android.Content.ActivityFlags.GrantReadUriPermission |
                        Android.Content.ActivityFlags.GrantWriteUriPermission |
                        Android.Content.ActivityFlags.GrantPersistableUriPermission |
                        Android.Content.ActivityFlags.GrantPrefixUriPermission);
        current_activity.StartActivityForResult(intent, REQUEST_TREE);
    }

    public static void OpenLogFileForWriting(string file_name, string file_contents)
    {
        var current_activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

        List<UriPermission> permissions = current_activity.ContentResolver.PersistedUriPermissions.ToList();
        if (permissions != null && permissions.Count > 0)
        {
            DocumentFile log_folder = DocumentFile.FromTreeUri(current_activity, permissions[0].Uri);
            DocumentFile log_file = log_folder.CreateFile(MimeTypeMap.Singleton.GetMimeTypeFromExtension("csv"), file_name);
            ParcelFileDescriptor pfd = current_activity.ContentResolver.OpenFileDescriptor(log_file.Uri, "w");
            FileOutputStream file_output_stream = new FileOutputStream(pfd.FileDescriptor);
            file_output_stream.Write(Encoding.UTF8.GetBytes(file_contents));
            file_output_stream.Close();
        }
    }

    public static bool HasFolderBeenSelectedAndPermissionsGiven()
    {
        var current_activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

        List<UriPermission> permissions = current_activity.ContentResolver.PersistedUriPermissions.ToList();
        return (permissions != null && permissions.Count > 0);
    }


    public static void recursePath(Java.IO.File directory)
    {
        if (directory.IsDirectory)
        {
            Java.IO.File[] paths = directory.ListFiles();
            if (paths != null)
            {
                foreach (Java.IO.File path in paths)
                {
                    logger.info("going deeper down {0}", path.AbsolutePath);
                    recursePath(path);
                }
            }
        }
        else
        {
            logger.info("found endpoint at {0}", directory.AbsolutePath);
            //Java.IO. directory.AbsolutePath
            if (System.IO.File.Exists(directory.AbsolutePath))
            {
                using Stream inputStream = System.IO.File.OpenRead(directory.AbsolutePath);
                StreamReader sr = new StreamReader(inputStream);
                string blob = sr.ReadToEnd();

            }

        }
    }

    public static string recursePathAndOpen(Java.IO.File directory, string name)
    {
        if (directory.IsDirectory)
        {
            Java.IO.File[] paths = directory.ListFiles();

            string finalBlob = null;

            if (paths != null)
            {
                foreach (Java.IO.File path in paths)
                {
                    logger.info("going deeper down {0}", path.AbsolutePath);
                    string tempBlob = recursePathAndOpen(path, name);
                    if (tempBlob != null && finalBlob == null)
                        finalBlob = tempBlob;
                }

                return finalBlob;
            }

            return null;
        }
        else
        {
            logger.info("found endpoint at {0}", directory.AbsolutePath);
            //Java.IO. directory.AbsolutePath
            if (System.IO.File.Exists(directory.AbsolutePath) && directory.AbsolutePath.Split('/').Last() == name)
            {
                using Stream inputStream = System.IO.File.OpenRead(directory.AbsolutePath);
                StreamReader sr = new StreamReader(inputStream);
                string blob = sr.ReadToEnd();
                return blob;
            }

            return null;
        }
    }
    public static string recurseAndFindPath(Java.IO.File directory, string name)
    {
        if (directory.IsDirectory)
        {
            Java.IO.File[] paths = directory.ListFiles();

            string finalBlob = null;

            if (paths != null)
            {
                foreach (Java.IO.File path in paths)
                {
                    logger.info("going deeper down {0}", path.AbsolutePath);
                    string tempBlob = recurseAndFindPath(path, name);
                    if (tempBlob != null && finalBlob == null)
                        finalBlob = tempBlob;
                }

                return finalBlob;
            }

            return null;
        }
        else
        {
            logger.info("found endpoint at {0}", directory.AbsolutePath);
            //Java.IO. directory.AbsolutePath
            if (System.IO.File.Exists(directory.AbsolutePath) && directory.AbsolutePath.Split('/').Last() == name)
            {
                return directory.AbsolutePath;
            }

            return null;
        }
    }
    
    public static string recurseAndFindPath(Java.IO.File directory, Regex regex)
    {
        if (directory.IsDirectory)
        {
            Java.IO.File[] paths = directory.ListFiles();

            string finalBlob = null;

            if (paths != null)
            {
                foreach (Java.IO.File path in paths)
                {
                    logger.info("going deeper down {0}", path.AbsolutePath);
                    string tempBlob = recurseAndFindPath(path, regex);
                    if (tempBlob != null && finalBlob == null)
                        finalBlob = tempBlob;
                }

                return finalBlob;
            }

            return null;
        }
        else
        {
            logger.info("found endpoint at {0}", directory.AbsolutePath);
            //Java.IO. directory.AbsolutePath
            if (System.IO.File.Exists(directory.AbsolutePath) && regex.IsMatch(directory.AbsolutePath.Split('/').Last()))
            {
                return directory.AbsolutePath;
            }

            return null;
        }
    }

    public async static Task loadONNXModel(string extension, string correctionPath)
    {
        try
        {
            string fullPath = null;

            var cacheDirs = Platform.AppContext.GetExternalFilesDirs(null);
            foreach (var cDir in cacheDirs)
            {
                logger.debug("recursing down dir {0}", cDir.AbsolutePath);
                fullPath = recurseAndFindPath(cDir, correctionPath);
                if (fullPath != null)
                    break;
            }

            if (fullPath != null)
            {
                loadCorrections(fullPath);
                fullPath = null;
            }

            Regex extensionReg = new Regex(@".*\." + extension + @"$");
            cacheDirs = Platform.AppContext.GetExternalFilesDirs(null);
            foreach (var cDir in cacheDirs)
            {
                logger.debug("recursing down dir {0}", cDir.AbsolutePath);
                fullPath = recurseAndFindPath(cDir, extensionReg);
                if (fullPath != null)
                    break;
            }

            if (fullPath == null)
                return;

            var data = mlContext.Data.LoadFromEnumerable(Enumerable.Empty<ModelInput>());
            logger.debug("building pipeline");
            var pipeline = mlContext.Transforms.ApplyOnnxModel(modelFile: fullPath, outputColumnNames: new[] { "StatefulPartitionedCall_1:0" }, inputColumnNames: new[] { "serving_default_input_layer:0" });
            logger.debug("building transformer");
            transformer = pipeline.Fit(data);
            var transCope = transformer;
            logger.debug("creating engine");
            engine = mlContext.Model.CreatePredictionEngine<ModelInput, Prediction>(transformer);
            var engCop = engine;

            logger.debug("onnx model load complete");
            transformerLoaded = true;
        }
        catch (Exception e)
        {
            logger.info("onnx load failed with exception {0}", e.Message);
        }
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

    public static double[] ProcessBackground(double[] wavenumbers, double[] counts, string serial, double fwhm)
    {
        try
        {
            //logger.logArray("pre-processed wavenum", wavenumbers);
            //logger.logArray("pre-processed counts", counts);

            if (correctionFactors != null && correctionFactors.ContainsKey(serial))
            {
                double[] corrections = correctionFactors[serial];
                for (int i = 0; i < counts.Length; i++)
                    counts[i] /= corrections[i];
            }

            //logger.logArray("etalon-corrected counts", counts);

            double[] targetWavenum = new double[2376];
            for (int i = 0; i < targetWavenum.Length; i++)
            {
                targetWavenum[i] = i + 216;
            }

            double[] interpolatedCounts = Wavecal.mapWavenumbers(wavenumbers, counts, targetWavenum);
            double max = interpolatedCounts.Max();


            //logger.logArray("interpolated wavenum", targetWavenum);
            //logger.logArray("interpolated counts", interpolatedCounts);

            /*
            for (int i = 0; i < interpolatedCounts.Length; i++)
            {
                interpolatedCounts[i] = interpolatedCounts[i] / max;
            }
            */

            ModelInput modelInput = new ModelInput();
            modelInput.spectrum = new float[2376];
            for (int i =  0; i < interpolatedCounts.Length; i++) 
                modelInput.spectrum[i] = (float)interpolatedCounts[i];

            Prediction p = new Prediction();

            /*
            var res = engine.Predict(modelInput);

            var data = mlContext.Data.LoadFromEnumerable(new List<ModelInput> { modelInput });
            var pred = transformer.Transform(data);
            DataViewSchema columns = pred.Schema;
            var final = pred.GetColumn<float[,]>("StatefulPartitionedCall:0");
            

            int count = 0;
            bool isOk = final.TryGetNonEnumeratedCount(out count);
            */
            //transformer.Transform()

            logger.debug("making prediction");
            p = engine.Predict(modelInput);
            logger.debug("packing prediction");


            //logger.logArray("transformed counts", p.spectrum);

            int outputSize = p.spectrum.GetLength(0);
            double[] output = new double[outputSize];
            double min = p.spectrum.Min();
            for (int i = 0; i < outputSize; ++i)
            {
                output[i] = p.spectrum[i] - min; // * max;
            }

            //logger.logArray("rebased counts", output);
            /*
            for (int i = 0; i < output.Length / 200; ++i)
            {
                double[] waveSubset = new double[200]; 
                double[] outSubset = new double[200]; 
                for (int j = 0; j < 200; j++)
                {

                }

            }
            */

            output = customDeconvoluteSpectrum(targetWavenum, output, fwhm);
            //logger.logArray("deconvoluted counts", output);
            //output = numpyDecon(targetWavenum, output, fwhm);

            logger.debug("returning processed spectrum");
            return output;
        }
        catch (Exception e)
        {
            logger.debug("background process failed with exception {0}", e.Message);
            return null;
        }
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
        int padPixels = (int)Math.Ceiling(padWidth *  avgPixelFWHM);

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
        for(int i = 0;i < pixelFWHM.Length;++i)
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
            for (int i = 0;i < resolutionSpectrum.Length; ++i) 
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
        var docDir = Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryDocuments);
        return Path.Join(docDir.Path, "MatchingLibrary");
    }

    // logger:  /storage/emulated/0/Android/data/com.wasatchphotonics.enlightenmaui/files/Documents/2024-04-30/enlighten-20240430-154219-290237-WP-01647.csv
    // PC: \Internal shared storage\Android\data\com.wasatchphotonics.enlightenmaui\files\Documents\2024-04-30
    public static string getSavePath()
    {
        if (savePath != null)
        {
            logger.debug($"getSavePath: returning previous savePath {savePath}");
            return savePath;
        }

        var docDir = Android.App.Application.Context.GetExternalFilesDir(Android.OS.Environment.DirectoryDocuments);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var todayDir = Path.Join(docDir.Path, today);

        if (!writeable(todayDir))
        {
            logger.error($"getSavePath: unable to write todayDir {todayDir}");
            return null;
        }

        logger.debug($"getSavePath: returning writeable todayDir {todayDir}");
        return savePath = todayDir;
    }

    public static string getUserLibraryPath()
    {
        if (userLibraryPath != null)
        {
            logger.debug($"getuserLibraryPath: returning previous userLibraryPath {userLibraryPath}");
            return userLibraryPath;
        }

        var docDir = Android.App.Application.Context.GetExternalFilesDir(null);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var userLibDir = Path.Join(docDir.Path, "User Library");

        if (!writeable(userLibDir))
        {
            logger.error($"getuserLibraryPath: unable to write userLibDir {userLibDir}");
            return null;
        }

        logger.debug($"getuserLibraryPath: returning writeable userLibDir {userLibDir}");
        return userLibraryPath = userLibDir;
    }

    public static string getAutoSavePath(bool highLevelAutoSave)
    {
        if (autoSavePath != null)
        {
            logger.debug($"getAutoSavePath: returning previous {userLibraryPath}");
            return autoSavePath;
        }

        var docDir = getSavePath();

        if (highLevelAutoSave)
        {
            string temp = Path.Join("/storage/emulated/0", "EnlightenSpectra");
            if (writeable(temp)) 
                docDir = temp;
            else
                docDir = "/storage/emulated/0/Documents";
        }

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var todayDir = Path.Join(docDir, today);

        if (!writeable(todayDir))
        {
            logger.error($"getAutoSavePath: unable to write autoSaveDir {todayDir}");
            return null;
        }

        return autoSavePath = todayDir;
    }


    static bool writeable(string path)
    {
        var f = new Java.IO.File(path);
        logger.debug($"writeable: testing {path}");
        if (f.Exists())
        {
            logger.debug($"exists: {path}");
        }
        else
        {
            logger.debug($"calling Mkdirs({path})");
            f.Mkdirs();
            if (!f.Exists())
            {
                logger.error($"writeable: Mkdirs failed to create {path}");
                return false;
            }
        }

        if (!f.CanWrite())
        {
            logger.error($"writeable: can't write: {path}");
            return false;
        }

        return true;
    }

    // static public void writeFile(string pathname, string contents) { File.WriteAllText(pathname, contents); }
}
