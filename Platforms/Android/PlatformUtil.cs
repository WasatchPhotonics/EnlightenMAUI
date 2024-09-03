using Android;
using Android.Content.Res;
using System.Linq;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms.Onnx;
using Telerik.Maui.Controls.Scheduler;
namespace EnlightenMAUI.Platforms;


internal class ModelInput
{
    [VectorType(2376,1)]
    [ColumnName("serving_default_input_1:0")]
    public float[,] spectrum { get; set; }
}

internal class Prediction
{
    [VectorType(2008, 1)]
    [ColumnName("StatefulPartitionedCall:0")]
    public float[,] spectrum { get; set; }

}

internal class PlatformUtil
{
    static Logger logger = Logger.getInstance();
    static MLContext mlContext = new MLContext();
    static PredictionEngine<ModelInput, Prediction> engine;
    static ITransformer transformer;
    public static bool transformerLoaded = false;

    static string savePath;

    public static void recursePath(Java.IO.File directory)
    {
        if (directory.IsDirectory)
        {
            Java.IO.File[] paths = directory.ListFiles();
            foreach (Java.IO.File path in paths)
            {
                logger.info("going deeper down {0}", path.AbsolutePath);
                recursePath(path);
            }
        }
        else
        {
            logger.info("found endpoint at {0}", directory.AbsolutePath);
        }
    }

    public async static Task loadONNXModel(string path)
    {
        try
        {
            var fullPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, path);

            if (!File.Exists(fullPath))
            {
                logger.debug("copying asset into data folder");
                // Open the source file
                using Stream inputStream = await FileSystem.Current.OpenAppPackageFileAsync(path);

                // Create an output filename
                string targetFile = Path.Combine(FileSystem.Current.AppDataDirectory, path);

                // Copy the file to the AppDataDirectory
                using FileStream outputStream = File.Create(targetFile);
                await inputStream.CopyToAsync(outputStream);
                logger.debug("finished copying asset into data folder");
            }

            var data = mlContext.Data.LoadFromEnumerable(new List<ModelInput>());
            logger.debug("building pipeline");
            var pipeline = mlContext.Transforms.ApplyOnnxModel(modelFile: fullPath, outputColumnNames: new[] { "StatefulPartitionedCall:0" }, inputColumnNames: new[] { "serving_default_input_1:0" });
            logger.debug("building transformer");
            transformer = pipeline.Fit(data);
            logger.debug("creating engine");
            engine = mlContext.Model.CreatePredictionEngine<ModelInput, Prediction>(transformer);
            
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
            ModelInput modelInput = new ModelInput();
            modelInput.spectrum = new float[2376, 1];
            for (int i =  0; i < counts.Length; i++) 
                modelInput.spectrum[i,0] = (float)counts[i];

            Prediction p = new Prediction();
            p = engine.Predict(modelInput);

            int outputSize = p.spectrum.GetLength(0);
            double[] output = new double[outputSize];
            for (int i = 0; i < outputSize; ++i)
            {
                output[i] = p.spectrum[i,0];
            }

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
