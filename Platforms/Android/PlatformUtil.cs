using Android;
using Android.Content.Res;
using Xamarin.TensorFlow.Lite;

namespace EnlightenMAUI.Platforms;

internal class PlatformUtil
{
    static Logger logger = Logger.getInstance();
    static Interpreter interpreter = null;

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

    public static void loadTFModel(string path)
    {
        try
        {
            var fullPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, path);

            List<string> dirs = new List<string>(System.IO.Directory.EnumerateDirectories(FileSystem.AppDataDirectory));
            List<string> files = new List<string>(System.IO.Directory.EnumerateFiles(FileSystem.AppDataDirectory));

            var libDir = Android.App.Application.Context.DataDir;
            recursePath(libDir);

            libDir = Android.App.Application.Context.FilesDir;
            recursePath(libDir);

            libDir = Android.App.Application.Context.CacheDir;
            recursePath(libDir);

            libDir = Android.App.Application.Context.ObbDir;
            recursePath(libDir);

            libDir = Android.App.Application.Context.CodeCacheDir;
            recursePath(libDir);

            libDir = Android.App.Application.Context.ExternalCacheDir;
            recursePath(libDir);

            libDir = Android.App.Application.Context.NoBackupFilesDir;
            recursePath(libDir);
            //Java.IO.File[] fileObj = libDir.ListFiles();
            //Stream fileStream = await FileSystem.Current.OpenAppPackageFileAsync(path);

            Java.IO.File file = new Java.IO.File(fullPath);
            if (file.Exists())
            {
                logger.info("see file of size {0}", file.TotalSpace);
                interpreter = new Interpreter(file);
                logger.info("tf load succeeded");
            }
            else
            {
                logger.info("file does not seem to exist");

            }
        }
        catch (Exception e)
        {
            logger.info("tf load failed with exception {0}", e.Message);
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
