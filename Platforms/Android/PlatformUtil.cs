using Android;
using Android.Content.Res;
using System.Linq;
using System.Text;
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
namespace EnlightenMAUI.Platforms;


internal class ModelInput
{
    [ColumnName("serving_default_input_1:0")]
    [VectorType(1, 2376, 1)]
    public float[] spectrum { get; set; }
}

internal class Prediction
{
    [ColumnName("StatefulPartitionedCall:0")]
    [VectorType(1, 2008, 1)]
    public float[] spectrum { get; set; }

}

internal class PlatformUtil
{
    static Logger logger = Logger.getInstance();
    static MLContext mlContext = new MLContext();
    static PredictionEngine<ModelInput, Prediction> engine;
    static ITransformer transformer;
    public static bool transformerLoaded = false;
    public static int REQUEST_TREE = 85;

    static string savePath;

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

    public async static Task loadONNXModel(string path)
    {
        try
        {
            var fullPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, path);

            if (!System.IO.File.Exists(fullPath))
            {
                logger.debug("copying asset into data folder");
                // Open the source file
                using Stream inputStream = await FileSystem.Current.OpenAppPackageFileAsync(path);

                // Create an output filename
                string targetFile = Path.Combine(FileSystem.Current.AppDataDirectory, path);

                // Copy the file to the AppDataDirectory
                using FileStream outputStream = System.IO.File.Create(targetFile);
                await inputStream.CopyToAsync(outputStream);
                logger.debug("finished copying asset into data folder");
            }

            var data = mlContext.Data.LoadFromEnumerable(Enumerable.Empty<ModelInput>());
            logger.debug("building pipeline");
            var pipeline = mlContext.Transforms.ApplyOnnxModel(modelFile: fullPath, outputColumnNames: new[] { "StatefulPartitionedCall:0" }, inputColumnNames: new[] { "serving_default_input_1:0" });
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

    public static double[] ProcessBackground(double[] wavenumbers, double[] counts)
    {
        try
        {
            double[] targetWavenum = new double[2376];
            for (int i = 0; i < targetWavenum.Length; i++)
            {
                targetWavenum[i] = i + 216;
            }

            double[] interpolatedCounts = Wavecal.mapWavenumbers(wavenumbers, counts, targetWavenum);
            double max = interpolatedCounts.Max();
            interpolatedCounts = interpolatedCounts.Select(x => x / max).ToArray();

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
