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
namespace EnlightenMAUI.Platforms;


internal class ModelInput
{
    [ColumnName("input_1")]
    [VectorType(1, 2376, 1)]
    public float[] spectrum { get; set; }
}

internal class Prediction
{
    [ColumnName("conv1d_18")]
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
    static string configurationPath;

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
            var pipeline = mlContext.Transforms.ApplyOnnxModel(modelFile: fullPath, outputColumnNames: new[] { "conv1d_18" }, inputColumnNames: new[] { "input_1" });
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

    public static double[] ProcessBackground(double[] wavenumbers, double[] counts, string serial)
    {
        try
        {
            if (correctionFactors != null && correctionFactors.ContainsKey(serial))
            {
                double[] corrections = correctionFactors[serial];
                for (int i = 0; i < counts.Length; i++)
                    counts[i] /= corrections[i];
            }

            double[] targetWavenum = new double[2376];
            for (int i = 0; i < targetWavenum.Length; i++)
            {
                targetWavenum[i] = i + 216;
            }

            double[] interpolatedCounts = Wavecal.mapWavenumbers(wavenumbers, counts, targetWavenum);
            double max = interpolatedCounts.Max();

            for (int i = 0; i < interpolatedCounts.Length; i++)
            {
                interpolatedCounts[i] = interpolatedCounts[i] / max;
            }

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

            int outputSize = p.spectrum.GetLength(0);
            double[] output = new double[outputSize];
            for (int i = 0; i < outputSize; ++i)
            {
                output[i] = p.spectrum[i] * max;
            }

            logger.debug("returning processed spectrum");
            return output;
        }
        catch (Exception e)
        {
            logger.debug("background process failed with exception {0}", e.Message);
            return null;
        }
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

    public static string getConfigFilePath()
    {
        if (configurationPath != null)
        {
            logger.debug($"getConfigFilePath: returning previous configPath {configurationPath}");
            return configurationPath;
        }

        var docDir = Android.App.Application.Context.GetExternalFilesDir(null).AbsolutePath;

        if (!writeable(docDir))
        {
            logger.error($"getuserLibraryPath: unable to write userLibDir {docDir}");
            return null;
        }

        logger.debug($"getuserLibraryPath: returning writeable userLibDir {docDir}");
        return configurationPath = docDir + "/configuration.json";
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
