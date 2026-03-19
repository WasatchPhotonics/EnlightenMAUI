using Android;
using Android.Content;
using Android.Content.Res;
using Android.Locations;
using Android.Nfc;
using Android.OS;
using Android.Webkit;
using AndroidX.AppCompat.Widget;
using EnlightenMAUI.Common;
using EnlightenMAUI.Models;
using Java.IO;
using Kotlin.Contracts;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Transforms.Onnx;
using Newtonsoft.Json;
using NumSharp;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Telerik.Maui.Controls.Scheduler;
using Xamarin.Google.Crypto.Tink.Subtle;
using static Android.Widget.GridLayout;
using static Microsoft.Maui.LifecycleEvents.AndroidLifecycle;
using AndrApp = Android.App;
using AndrContent = Android.Content;
using AndrNet = Android.Net;
using AndrOS = Android.OS;
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

internal class SimpleModelInput
{
    [ColumnName("serving_default_input_1:0")]
    [VectorType(1, 2376, 1)]
    public float[] spectrum { get; set; }
}

internal class SimplePrediction
{
    [ColumnName("StatefulPartitionedCall:0")]
    [VectorType(1, 2008, 1)]
    public float[] spectrum { get; set; }

}


public static class StorageHelper
{
    public const int RequestCode = 2296;
    private static TaskCompletionSource<bool>? GetPermissionTask { get; set; }

    public static async Task<bool> GetManageAllFilesPermission()
    {
        if (!AndrOS.Environment.IsExternalStorageManager)
        {
            try
            {
                AndrNet.Uri uri = AndrNet.Uri.Parse("package:" + Platform.CurrentActivity.ApplicationInfo.PackageName);

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
        GetPermissionTask?.SetResult(AndrOS.Environment.IsExternalStorageManager);
    }
}

internal class PlatformUtil
{
    static Logger logger = Logger.getInstance();
    static MLContext mlContext = new MLContext();
    static Dictionary<string, InferenceSession> correctionSessions = new Dictionary<string, InferenceSession>();
    static Dictionary<string, double[]> correctionFactors = new Dictionary<string, double[]>();
    static ITransformer transformer;
    public static bool transformerLoaded = false;
    public static bool simpleTransformerLoaded = false;
    public static bool aggressiveTransformerLoaded = false;
    public static int REQUEST_TREE = 85;

    static string savePath;
    static string userLibraryPath;
    static string configurationPath;
    static string autoSavePath;

    public static void RequestSelectLogFolder()
    {
        var current_activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var intent = new AndrContent.Intent(AndrContent.Intent.ActionOpenDocumentTree);
        intent.AddFlags(AndrContent.ActivityFlags.GrantReadUriPermission |
                        AndrContent.ActivityFlags.GrantWriteUriPermission |
                        AndrContent.ActivityFlags.GrantPersistableUriPermission |
                        AndrContent.ActivityFlags.GrantPrefixUriPermission);
        current_activity.StartActivityForResult(intent, REQUEST_TREE);
    }

    public static void OpenLogFileForWriting(string file_name, string file_contents)
    {
        var current_activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;

        List<UriPermission> permissions = current_activity.ContentResolver.PersistedUriPermissions.ToList();
        if (permissions != null && permissions.Count > 0)
        {
            //DocumentFile log_folder = DocumentFile.FromTreeUri(current_activity, permissions[0].Uri);
            //DocumentFile log_file = log_folder.CreateFile(MimeTypeMap.Singleton.GetMimeTypeFromExtension("csv"), file_name);
            //ParcelFileDescriptor pfd = current_activity.ContentResolver.OpenFileDescriptor(log_file.Uri, "w");
            //FileOutputStream file_output_stream = new FileOutputStream(pfd.FileDescriptor);
            //file_output_stream.Write(Encoding.UTF8.GetBytes(file_contents));
            //file_output_stream.Close();
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
    
    public static List<string> recurseAndFindPaths(Java.IO.File directory, Regex regex, bool isPrime, List<string> findsSoFar)
    {
        if (directory.IsDirectory)
        {
            Java.IO.File[] paths = directory.ListFiles();

            if (paths != null)
            {
                foreach (Java.IO.File path in paths)
                {
                    logger.info("going deeper down {0}", path.AbsolutePath);
                    recurseAndFindPaths(path, regex, false, findsSoFar);
                }
            }

            if (isPrime)
                return findsSoFar;
            else
                return null;
        }
        else
        {
            logger.info("found endpoint at {0}", directory.AbsolutePath);
            //Java.IO. directory.AbsolutePath
            if (System.IO.File.Exists(directory.AbsolutePath) && regex.IsMatch(directory.AbsolutePath.Split('/').Last()))
            {
                findsSoFar.Add(directory.AbsolutePath);
            }

            return null;
        }
    }

    public async static Task loadONNXModel(string root, string extension, string correctionPath)
    {
        try
        {
            //
            // To move to Assets, rewrite from here to line 364
            //

            AssetManager assets = Platform.AppContext.Assets;

            string[] assetP = assets.List(root);

            string fullPath = null;

            foreach (string path in assetP)            {
                if (path == correctionPath)
                    fullPath = Path.Join(root, path);
            }

            if (fullPath != null)
            {
                await loadCorrections(fullPath);
                fullPath = null;
            }

            Regex extensionReg = new Regex(@".*\." + extension + @"$");
            List<string> fullPaths = new List<string>();
            foreach (string path in assetP)
            {
                if (extensionReg.IsMatch(path))
                    fullPaths.Add(Path.Join(root, path));
            }

            if (fullPaths == null || fullPaths.Count == 0)
                return;

            List<byte> aggressiveBuffer = new List<byte>();
            List<byte> simpleBuffer = new List<byte>();

            foreach (string path in fullPaths)
            {
                List<byte> tempBuffer = new List<byte>();
                bool loadSucceeded = false; 

                using Stream fs = await FileSystem.Current.OpenAppPackageFileAsync(path);
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    var bytesRead = 0;

                    int bufferSize = 1024;
                    byte[] bytes;
                    var buffer = new byte[bufferSize];
                    using (fs)
                    {
                        do
                        {
                            buffer = reader.ReadBytes(bufferSize);
                            bytesRead = buffer.Count();
                            tempBuffer.AddRange(buffer);
                            logger.info("model file {0} read {1} bytes", path, bytesRead);
                        }

                        while (bytesRead > 0);

                    }
                }

                if (path.Contains("light") && tempBuffer.Count > 0)
                {
                    simpleBuffer = tempBuffer;
                }
                else if (tempBuffer.Count > 0)
                {
                    aggressiveBuffer = tempBuffer;
                }

            }

            if (simpleBuffer.Count > 0)
            {
                try
                {
                    var session = new Microsoft.ML.OnnxRuntime.InferenceSession(simpleBuffer.ToArray());
                    transformerLoaded = simpleTransformerLoaded = true;
                    correctionSessions.Add("simple", session);
                }
                catch (Exception ex)
                {
                    logger.info("onnx session load failed with exception {0}", ex.Message);
                }
            }

            if (aggressiveBuffer.Count > 0)
            {
                try
                {
                    var session = new Microsoft.ML.OnnxRuntime.InferenceSession(aggressiveBuffer.ToArray());
                    transformerLoaded = aggressiveTransformerLoaded = true;
                    correctionSessions.Add("aggressive", session);
                }
                catch (Exception ex)
                {
                    logger.info("onnx session load failed with exception {0}", ex.Message);
                }
            }

        }
        catch (Exception e)
        {
            logger.info("onnx load failed with exception {0}", e.Message);
        }
    }



    public static async Task<Dictionary<string, Measurement>> findUserFiles(Spectrometer spec)
    {
        var cacheDirs = Platform.AppContext.GetExternalFilesDirs(null);
        Java.IO.File libraryFolder = null;

        Dictionary<string, Measurement> temp = new Dictionary<string, Measurement>();

        foreach (var cDir in cacheDirs)
        {
            var subs = await cDir.ListFilesAsync();
            foreach (var sub in subs)
            {
                if (sub.AbsolutePath.Split('/').Last() == "Documents")
                {
                    libraryFolder = sub;
                    break;
                }
            }
        }

        if (libraryFolder == null)
            return null;

        Regex csvReg = new Regex(@".*\.csv$");

        var libraryFiles = libraryFolder.ListFiles();

        foreach (var libraryFile in libraryFiles)
        {
            if (libraryFile.IsDirectory)
            {
                await findUserFilesDeeper(libraryFile, spec, temp);
            }
            else if (csvReg.IsMatch(libraryFile.AbsolutePath))
            {
                try
                {
                    await addUserFile(libraryFile, spec, temp);
                }
                catch (Exception e)
                {
                    logger.debug("loading {0} failed with exception {1}", libraryFile.AbsolutePath, e.Message);
                }
            }
        }

        return temp;
    }

    async static Task findUserFilesDeeper(Java.IO.File folder, Spectrometer spec, Dictionary<string, Measurement> dict)
    {
        var libraryFiles = folder.ListFiles();

        foreach (var libraryFile in libraryFiles)
        {
            if (libraryFile.IsDirectory)
            {
                await findUserFilesDeeper(libraryFile, spec, dict);
            }
            else
            {
                await addUserFile(libraryFile, spec, dict);
            }
        }
    }

    async static Task addUserFile(Java.IO.File file, Spectrometer spec, Dictionary<string, Measurement> dict)
    {
        string name = file.AbsolutePath.Split('/').Last().Split('.').First();
        await loadCSV(file, spec, dict);
    }

    async static Task loadCSV(Java.IO.File file, Spectrometer spec, Dictionary<string, Measurement> dict)
    {
        logger.info("start loading library file from {0}", file.AbsolutePath);

        string name = file.AbsolutePath.Split('/').Last().Split('.').First();

        SimpleCSVParser parser = new SimpleCSVParser();
        Stream s = System.IO.File.OpenRead(file.AbsolutePath);
        StreamReader sr = new StreamReader(s);
        await parser.parseStream(s);

        Measurement m = new Measurement();
        m.wavenumbers = parser.wavenumbers.ToArray();
        m.raw = parser.intensities.ToArray();
        m.excitationNM = 785;
        Wavecal wavecal = new Wavecal(spec.pixels);
        wavecal.coeffs = spec.eeprom.wavecalCoeffs;
        wavecal.excitationNM = spec.laserExcitationNM;

        Measurement mOrig = m.copy();

        if (transformerLoaded)
        {
            double[] smoothed = ProcessBackground(m.wavenumbers, m.processed, spec.eeprom.serialNumber, spec.eeprom.avgResolution, spec.eeprom.ROIHorizStart);
            double[] wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
            Measurement updated = new Measurement();
            updated.wavenumbers = wavenumbers;
            updated.raw = smoothed;
            dict.Add(name, updated);
        }

        else
        {
            Measurement updated = wavecal.crossMapWavenumberData(m.wavenumbers, m.raw);
            double airPLSLambda = 10000;
            int airPLSMaxIter = 100;
            double[] array = AirPLS.smooth(updated.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)spec.eeprom.ROIHorizStart, (int)spec.eeprom.ROIHorizEnd);
            double[] shortened = new double[updated.processed.Length];
            Array.Copy(array, 0, shortened, spec.eeprom.ROIHorizStart, array.Length);
            updated.raw = shortened;
            updated.dark = null;
            dict.Add(name, updated);
        }

        logger.info("finish loading library file from {0}", file.AbsolutePath);
    }

    static async Task loadCorrections(string file)
    {
        logger.info("start loading correction factors from {0}", file);

        SimpleCSVParser parser = new SimpleCSVParser();
        Stream s = await FileSystem.Current.OpenAppPackageFileAsync(file);
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
        try
        {
            //logger.logArray("pre-processed wavenum", wavenumbers);
            //logger.logArray("pre-processed counts", counts);

            double[] local = new double[counts.Length];

            if (correctionFactors != null && correctionFactors.ContainsKey(serial))
            {
                double[] corrections = correctionFactors[serial];
                for (int i = 0; i < counts.Length; i++)
                    local[i] = counts[i] / corrections[i];
            }
            else
            {
                Array.Copy(counts, local, counts.Length);
            }

            //logger.logArray("etalon-corrected counts", counts);

            double[] targetWavenum = new double[2376];
            for (int i = 0; i < targetWavenum.Length; i++)
            {
                targetWavenum[i] = i + 216;
            }

            List<double> trimmedWN = new List<double>();
            List<double> trimmedCounts = new List<double>();

            for (int i = roiStart; i < wavenumbers.Length; i++)
            {
                trimmedWN.Add(wavenumbers[i]);
                trimmedCounts.Add(local[i]);
            }

            double[] interpolatedCounts = Wavecal.mapWavenumbers(trimmedWN.ToArray(), trimmedCounts.ToArray(), targetWavenum);

            double max = interpolatedCounts.Max();

            if (useSimple)
            {
                for (int i = 0; i < interpolatedCounts.Length; i++)
                {
                    interpolatedCounts[i] = interpolatedCounts[i] / max;
                }

                SimpleModelInput modelInput = new SimpleModelInput();
                modelInput.spectrum = new float[2376];
                for (int i = 0; i < interpolatedCounts.Length; i++)
                    modelInput.spectrum[i] = (float)interpolatedCounts[i];

                var sessionInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(correctionSessions["simple"].InputNames[0], new DenseTensor<float>(modelInput.spectrum, new int[] { 1, 2376, 1 }))
                };

                var inValue = OrtValue.CreateTensorValueFromMemory(modelInput.spectrum, new long[] { 1, 2376, 1 });

                var sessionOutput = correctionSessions["simple"].Run(sessionInputs);

                var sessionInput1 = new Dictionary<string, OrtValue>
                {
                    { correctionSessions["simple"].InputNames[0], inValue }
                };

                var runOptions = new RunOptions();
                var sessionOutput2 = correctionSessions["simple"].Run(runOptions, sessionInput1, correctionSessions["simple"].OutputNames.ToArray());
                var shape = sessionOutput2[0].GetTensorTypeAndShape();
                //var shape = sessionOutput2[0].GetTensorDataAsSpan

                int outputSize = sessionOutput2[0].GetTensorDataAsSpan<float>().Length;
                float[] sessionData = sessionOutput2[0].GetTensorDataAsSpan<float>().ToArray();

                double[] output = new double[outputSize];
                for (int i = 0; i < outputSize; ++i)
                {
                    output[i] = sessionData[i] * max;
                }
                double min = output.Min();
                for (int i = 0; i < outputSize; ++i)
                {
                    output[i] = output[i] - min; // * max;
                }

                //logger.logArray("rebased counts", output);

                output = customDeconvoluteSpectrum(targetWavenum, output, fwhm);
                ////logger.logArray("deconvoluted counts", output);

                logger.debug("returning processed spectrum");
                return output;

            }

            else
            {
                ModelInput modelInput = new ModelInput();
                modelInput.spectrum = new float[2376];
                for (int i = 0; i < interpolatedCounts.Length; i++)
                    modelInput.spectrum[i] = (float)interpolatedCounts[i];

                var sessionInputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(correctionSessions["aggressive"].InputNames[0], new DenseTensor<float>(modelInput.spectrum, new int[] { 1, 2376, 1 }))     
                    //NamedOnnxValue.CreateFromTensor(correctionSessions["aggressive"].InputNames[0], new DenseTensor<float>(modelInput.spectrum, correctionSessions["aggressive"].InputMetadata[correctionSessions["aggressive"].InputNames[0]].Dimensions))

                };

                var inValue = OrtValue.CreateTensorValueFromMemory(modelInput.spectrum, new long[] { 1, 2376, 1 });

                var sessionOutput = correctionSessions["aggressive"].Run(sessionInputs);

                var sessionInput1 = new Dictionary<string, OrtValue>
                {
                    { correctionSessions["aggressive"].InputNames[0], inValue }
                };

                var runOptions = new RunOptions();
                var sessionOutput2 = correctionSessions["aggressive"].Run(runOptions, sessionInput1, correctionSessions["aggressive"].OutputNames.ToArray());
                var shape = sessionOutput2[0].GetTensorTypeAndShape();
                //var shape = sessionOutput2[0].GetTensorDataAsSpan

                int outputSize = sessionOutput2[0].GetTensorDataAsSpan<float>().Length;
                float[] sessionData = sessionOutput2[0].GetTensorDataAsSpan<float>().ToArray();

                double[] output = new double[outputSize];
                for (int i = 0; i < outputSize; ++i)
                {
                    output[i] = sessionData[i] * max;
                }
                double min = output.Min();
                for (int i = 0; i < outputSize; ++i)
                {
                    output[i] = output[i] - min; // * max;
                }

                //logger.logArray("rebased counts", output);

                output = customDeconvoluteSpectrum(targetWavenum, output, fwhm);
                //logger.logArray("deconvoluted counts", output);

                logger.debug("returning processed spectrum");
                return output;
            }
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
        var docDir = AndrApp.Application.Context.GetExternalFilesDir(AndrOS.Environment.DirectoryDocuments);
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

        var docDir = AndrApp.Application.Context.GetExternalFilesDir(AndrOS.Environment.DirectoryDocuments);
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

        var docDir = AndrApp.Application.Context.GetExternalFilesDir(null);
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

    public static string getConfigFilePath()
    {
        if (configurationPath != null)
        {
            logger.debug($"getConfigFilePath: returning previous configPath {configurationPath}");
            return configurationPath;
        }

        var docDir = AndrApp.Application.Context.GetExternalFilesDir(null).AbsolutePath;

        if (!writeable(docDir))
        {
            logger.error($"getuserLibraryPath: unable to write userLibDir {docDir}");
            return null;
        }

        logger.debug($"getuserLibraryPath: returning writeable userLibDir {docDir}");
        return configurationPath = docDir + "/configuration.json";
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

    public static List<string> getSubLibraries()
    {
        List<string> compLibrary = new List<string>();

        var cacheDirs = Platform.AppContext.GetExternalFilesDirs(null);
        foreach (var cDir in cacheDirs)
        {
            var subs = cDir.ListFiles();
            foreach (var sub in subs)
            {
                if (sub.AbsolutePath.Split('/').Last() == "library")
                {
                    if (sub.IsDirectory)
                    {
                        var subLibs = sub.ListFiles();
                        foreach (var subLib in subLibs)
                        {
                            if (subLib.IsDirectory && !compLibrary.Contains(subLib.AbsolutePath.Split('/').Last()))
                                compLibrary.Add(subLib.AbsolutePath.Split('/').Last());
                        }
                    }
                }
            }
        }

        return compLibrary;
    }

    public async static Task<Dictionary<string, Measurement>> loadFiles(bool useAssets, string root, Dictionary<string, Measurement> library, Dictionary<string, double[]> originalRaws, Dictionary<string, double[]> originalDarks, bool doDecon = true, string correctionFileName = "etalon_correction.json")
    {
        if (useAssets)
        {
            AssetManager assets = Platform.AppContext.Assets;

            string[] assetP = assets.List(root);

            Regex csvReg = new Regex(@".*\.csv$");
            Regex jsonReg = new Regex(@".*\.json$");


            foreach (string path in assetP)
            {
                if (jsonReg.IsMatch(path))
                {
                    try
                    {
                        await loadJSON(root + "/" + path, originalRaws, originalDarks, library);
                    }
                    catch (Exception e)
                    {
                        logger.debug("loading {0} failed with exception {1}", path, e.Message);
                    }
                }
                else if (csvReg.IsMatch(path))
                {
                    try
                    {
                        await loadCSV(root + "/" + path, originalRaws, library);
                    }
                    catch (Exception e)
                    {
                        logger.debug("loading {0} failed with exception {1}", path, e.Message);
                    }
                }
            }
        }
        else
        {
            var cacheDirs = Platform.AppContext.GetExternalFilesDirs(null);
            Java.IO.File libraryFolder = null;
            string[] rootPath = root.Split('/');
            int depth = rootPath.Length;

            foreach (var cDir in cacheDirs)
            {
                libraryFolder = traverseDown(rootPath, cDir);
                if (libraryFolder != null)
                    break;

            }

            if (libraryFolder == null)
            {
                /*
                if (library.Count > 0)
                    loadSucceeded = true;
                isLoading = false;
                InvokeLoadFinished();
                return;
                */
                return library;
            }

            Regex csvReg = new Regex(@".*\.csv$");
            Regex jsonReg = new Regex(@".*\.json$");

            var libraryFiles = libraryFolder.ListFiles();

            foreach (var libraryFile in libraryFiles)
            {
                if (jsonReg.IsMatch(libraryFile.AbsolutePath))
                {
                    try
                    {
                        await loadJSON(libraryFile, originalRaws, originalDarks, library);
                    }
                    catch (Exception e)
                    {
                        logger.debug("loading {0} failed with exception {1}", libraryFile.AbsolutePath, e.Message);
                    }
                }
                else if (csvReg.IsMatch(libraryFile.AbsolutePath))
                {
                    try
                    {
                        await loadCSV(libraryFile, originalRaws, library);
                    }
                    catch (Exception e)
                    {
                        logger.debug("loading {0} failed with exception {1}", libraryFile.AbsolutePath, e.Message);
                    }
                }
            }
        }

        logger.debug("finished loading library files");


        if (doDecon)
        {
#if USE_DECON
                if (PlatformUtil.transformerLoaded)
                {
                    double[] wavenumbers = Enumerable.Range(400, library.Values.First().processed.Length).Select(x => (double)x).ToArray();
                    await deconvolutionLibrary.setWavenumberAxis(new List<double>(wavenumbers));
                }
                else
                    await deconvolutionLibrary.setWavenumberAxis(new List<double>(wavecal.wavenumbers));
#endif
        }

        logger.debug("finished prepping data for decon");
        return library;
    }
    public async static Task<string> getBulkLibraryPath()
    {
        string finalFullPath = "";

        var dir = Platform.AppContext.GetExternalFilesDir(null);

        Java.IO.File[] paths = await dir.ListFilesAsync();
        foreach (Java.IO.File path in paths)
        {
            string file = path.AbsolutePath.Split('/').Last();

            if (file != null && file.Length > 0)
            {
                string fullPath = dir + "/" + file;
                if (file.Split('.').Last().ToLower() == "idex")
                    finalFullPath = fullPath;
            }
        }

        return finalFullPath;
    }
    static Java.IO.File traverseDown(string[] rootPath, Java.IO.File dir)
    {
        var subs = dir.ListFiles();
        Java.IO.File libraryFolder = null;

        foreach (var sub in subs)
        {
            if (sub.AbsolutePath.Split('/').Last() == rootPath[0])
            {
                if (rootPath.Length == 1)
                {
                    libraryFolder = sub;
                    break;
                }

                else if (sub.IsDirectory)
                {
                    string[] shortenedPath = new string[rootPath.Length - 1];
                    Array.Copy(rootPath, 1, shortenedPath, 0, rootPath.Length - 1);
                    return traverseDown(shortenedPath, sub);
                }
            }
        }

        return libraryFolder;
    }

    static async Task loadCSV(Java.IO.File file, Dictionary<string, double[]> originalRaws, Dictionary<string, Measurement> library)
    {
        logger.info("start loading library file from {0}", file.AbsolutePath);

        string name = file.AbsolutePath.Split('/').Last().Split('.').First();

        SimpleCSVParser parser = new SimpleCSVParser();
        Stream s = System.IO.File.OpenRead(file.AbsolutePath);
        StreamReader sr = new StreamReader(s);
        await parser.parseStream(s);

        Measurement m = new Measurement();
        m.wavenumbers = parser.wavenumbers.ToArray();
        m.raw = parser.intensities.ToArray();
        m.excitationNM = 785;


#if USE_DECON
            Deconvolution.Spectrum spec = new Deconvolution.Spectrum(parser.wavenumbers, parser.intensities);
#endif

        Measurement mOrig = m.copy();
        originalRaws.Add(name, mOrig.raw);

        /*
        double[] smoothedSpec = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed);
        while (smoothedSpec == null || smoothedSpec.Length == 0)
        {
            smoothedSpec = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed);
            await Task.Delay(50);
        }
        */

        if (false)
        {
            double[] smoothed = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed, "", 14, 200);
            double[] wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
            Measurement updated = new Measurement();
            updated.wavenumbers = wavenumbers;
            updated.raw = smoothed;
            library.Add(name, updated);

#if USE_DECON
                Deconvolution.Spectrum upSpec = new Deconvolution.Spectrum(new List<double>(wavenumbers), new List<double>(smoothed));
                deconvolutionLibrary.library.Add(name, upSpec);
#endif
        }

        else
        {
            double[] wavenumbers = Enumerable.Range(400, 2008).Select(x => (double)x).ToArray();
            double[] newIntensities = Wavecal.mapWavenumbers(m.wavenumbers, m.processed, wavenumbers);

            Measurement updated = new Measurement();
            updated.wavenumbers = wavenumbers;
            updated.raw = newIntensities;
            //double airPLSLambda = 10000;
            //int airPLSMaxIter = 100;
            //double[] array = AirPLS.smooth(updated.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)roiStart, (int)roiEnd);
            //double[] shortened = new double[updated.processed.Length];
            //Array.Copy(array, 0, shortened, roiStart, array.Length);
            //updated.raw = shortened;
            //updated.dark = null;

            library.Add(name, updated);

#if USE_DECON
                Deconvolution.Spectrum upSpec = new Deconvolution.Spectrum(new List<double>(updated.wavenumbers), new List<double>(updated.processed));
                deconvolutionLibrary.library.Add(name, upSpec);
#endif
        }

        logger.info("finish loading library file from {0}", file.AbsolutePath);
    }

    static async Task loadCSV(string file, Dictionary<string, double[]> originalRaws, Dictionary<string, Measurement> library)
    {
        logger.info("start loading library file from {0}", file);

        string name = file.Split('/').Last().Split('.').First();


        SimpleCSVParser parser = new SimpleCSVParser();
        Stream s = await FileSystem.Current.OpenAppPackageFileAsync(file); //System.IO.File.OpenRead(file);
        StreamReader sr = new StreamReader(s);
        await parser.parseStream(s);

        Measurement m = new Measurement();
        m.wavenumbers = parser.wavenumbers.ToArray();
        m.raw = parser.intensities.ToArray();
        m.excitationNM = 785;


#if USE_DECON
            Deconvolution.Spectrum spec = new Deconvolution.Spectrum(parser.wavenumbers, parser.intensities);
#endif

        Measurement mOrig = m.copy();
        originalRaws.Add(name, mOrig.raw);

        /*
        double[] smoothedSpec = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed);
        while (smoothedSpec == null || smoothedSpec.Length == 0)
        {
            smoothedSpec = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed);
            await Task.Delay(50);
        }
        */

        if (false)
        {
            double[] smoothed = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed, "", 14, 200);
            double[] wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
            Measurement updated = new Measurement();
            updated.wavenumbers = wavenumbers;
            updated.raw = smoothed;
            library.Add(name, updated);

#if USE_DECON
                Deconvolution.Spectrum upSpec = new Deconvolution.Spectrum(new List<double>(wavenumbers), new List<double>(smoothed));
                deconvolutionLibrary.library.Add(name, upSpec);
#endif
        }

        else
        {
            double[] wavenumbers = Enumerable.Range(400, 2008).Select(x => (double)x).ToArray();
            double[] newIntensities = Wavecal.mapWavenumbers(m.wavenumbers, m.processed, wavenumbers);

            Measurement updated = new Measurement();
            updated.wavenumbers = wavenumbers;
            updated.raw = newIntensities;
            //double airPLSLambda = 10000;
            //int airPLSMaxIter = 100;
            //double[] array = AirPLS.smooth(updated.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)roiStart, (int)roiEnd);
            //double[] shortened = new double[updated.processed.Length];
            //Array.Copy(array, 0, shortened, roiStart, array.Length);
            //updated.raw = shortened;
            //updated.dark = null;

            library.Add(name, updated);

#if USE_DECON
                Deconvolution.Spectrum upSpec = new Deconvolution.Spectrum(new List<double>(updated.wavenumbers), new List<double>(updated.processed));
                deconvolutionLibrary.library.Add(name, upSpec);
#endif
        }

        logger.info("finish loading library file from {0}", file);
    }
    static async Task loadJSON(Java.IO.File file, Dictionary<string, double[]> originalRaws, Dictionary<string, double[]> originalDarks, Dictionary<string, Measurement> library)
    {
        logger.info("start loading library file from {0}", file.AbsolutePath);

        string name = file.AbsolutePath.Split('/').Last().Split('.').First();

        SimpleCSVParser parser = new SimpleCSVParser();
        Stream s = System.IO.File.OpenRead(file.AbsolutePath);
        StreamReader sr = new StreamReader(s);
        string blob = await sr.ReadToEndAsync();

        spectrumJSON json = JsonConvert.DeserializeObject<spectrumJSON>(blob);
        if (json.tag != null && json.tag.Length > 0)
        {
            name = json.tag;
        }

        Measurement m = new Measurement(json);
        Wavecal otherCal = new Wavecal(m.pixels);
        otherCal.coeffs = m.wavecalCoeffs;
        otherCal.excitationNM = m.excitationNM;

        Measurement mOrig = m.copy();
        originalRaws.Add(name, mOrig.raw);
        originalDarks.Add(name, mOrig.dark);


        if (PlatformUtil.transformerLoaded)
        {
            double[] smoothed = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed, "", 14, 200);
            double[] wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
            Measurement updated = new Measurement();
            updated.wavenumbers = wavenumbers;
            updated.raw = smoothed;
            library.Add(name, updated);

#if USE_DECON
                Deconvolution.Spectrum upSpec = new Deconvolution.Spectrum(new List<double>(wavenumbers), new List<double>(smoothed));
                deconvolutionLibrary.library.Add(name, upSpec);
#endif
        }
        else
        {
            /*
            Measurement updated = wavecal.crossMapIntensityWavenumber(otherCal, m);
            double airPLSLambda = 10000;
            int airPLSMaxIter = 100;
            double[] array = AirPLS.smooth(updated.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)roiStart, (int)roiEnd);
            double[] shortened = new double[updated.processed.Length];
            Array.Copy(array, 0, shortened, roiStart, array.Length);
            updated.raw = shortened;
            updated.dark = null;
            */

            library.Add(name, mOrig);
        }

        logger.info("finish loading library file from {0}", file.AbsolutePath);
    }
    static async Task loadJSON(string file, Dictionary<string, double[]> originalRaws, Dictionary<string, double[]> originalDarks, Dictionary<string, Measurement> library)
    {
        logger.info("start loading library file from {0}", file);

        string name = file.Split('/').Last().Split('.').First();

        SimpleCSVParser parser = new SimpleCSVParser();
        Stream s = System.IO.File.OpenRead(file);
        StreamReader sr = new StreamReader(s);
        string blob = await sr.ReadToEndAsync();

        spectrumJSON json = JsonConvert.DeserializeObject<spectrumJSON>(blob);
        if (json.tag != null && json.tag.Length > 0)
        {
            name = json.tag;
        }

        Measurement m = new Measurement(json);
        Wavecal otherCal = new Wavecal(m.pixels);
        otherCal.coeffs = m.wavecalCoeffs;
        otherCal.excitationNM = m.excitationNM;

        Measurement mOrig = m.copy();
        originalRaws.Add(name, mOrig.raw);
        originalDarks.Add(name, mOrig.dark);


        if (PlatformUtil.transformerLoaded)
        {
            double[] smoothed = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed, "", 14, 200);
            double[] wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
            Measurement updated = new Measurement();
            updated.wavenumbers = wavenumbers;
            updated.raw = smoothed;
            library.Add(name, updated);

#if USE_DECON
                Deconvolution.Spectrum upSpec = new Deconvolution.Spectrum(new List<double>(wavenumbers), new List<double>(smoothed));
                deconvolutionLibrary.library.Add(name, upSpec);
#endif
        }
        else
        {
            /*
            Measurement updated = wavecal.crossMapIntensityWavenumber(otherCal, m);
            double airPLSLambda = 10000;
            int airPLSMaxIter = 100;
            double[] array = AirPLS.smooth(updated.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)roiStart, (int)roiEnd);
            double[] shortened = new double[updated.processed.Length];
            Array.Copy(array, 0, shortened, roiStart, array.Length);
            updated.raw = shortened;
            updated.dark = null;
            */

            library.Add(name, mOrig);
        }

        logger.info("finish loading library file from {0}", file);
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
