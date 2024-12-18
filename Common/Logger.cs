using System;
using System.Text;
using System.IO;
using System.Globalization;
using System.ComponentModel;
using EnlightenMAUI.Models;

namespace EnlightenMAUI
{
    public enum LogLevel { DEBUG, INFO, ERROR };

    // copied from WasatchNET
    public class Logger : INotifyPropertyChanged
    {
        ////////////////////////////////////////////////////////////////////////
        // Private attributes
        ////////////////////////////////////////////////////////////////////////

        static readonly Logger instance = new Logger();

        private StreamWriter outfile;

        const int AUTOSAVE_SIZE = 1 * 1024 * 1024; // 1MB
        bool saving;

        ////////////////////////////////////////////////////////////////////////
        // Public attributes
        ////////////////////////////////////////////////////////////////////////

        public LogLevel level { get; set; } = LogLevel.DEBUG;
        public bool loggingBLE;

        public bool liveUpdates { get; set; } = true;

        public event PropertyChangedEventHandler PropertyChanged;

        static public Logger getInstance()
        {
            return instance;
        }

        public void setPathname(string path)
        {
            try
            {
                outfile = new StreamWriter(path);
                debug("log path set to {0}", path);
            }
            catch (Exception e)
            {
                error("Can't set log pathname: {0}", e);
            }
        }

        public bool debugEnabled() => level <= LogLevel.DEBUG;
        
        public bool error(string fmt, params Object[] obj)
        {
            log(LogLevel.ERROR, fmt, obj);
            return false; // convenient for many cases
        }

        public void info(string fmt, params Object[] obj) => log(LogLevel.INFO, fmt, obj);

        public void debug(string fmt, params Object[] obj) => log(LogLevel.DEBUG, fmt, obj);

        // BluetoothViewModel uses this to hook into Plugin.BLE.Abstractions.Trace
        public void ble(string fmt, params Object[] obj) 
        {
            if (!loggingBLE)
                return;

            fmt = "[Plug.BLE] " + fmt;
            log(LogLevel.DEBUG, fmt, obj);
        }

        public void logString(LogLevel lvl, string msg) => log(lvl, msg);

        public string save(string pathname=null)
        {
            Console.WriteLine("Logger.save: starting");

            if (history is null)
            {
                Console.WriteLine("Can't save w/o history");
                return null;
            }
            
            if (pathname is null)
            {
                Settings settings = Settings.getInstance();
                var dir = settings.getSavePath();
                if (dir is null)
                {
                    Console.WriteLine("no path available to save log");
                    return null;
                }

                var filename = string.Format("EnlightenMobile-{0}.log", 
                    DateTime.Now.ToString("yyyyMMdd-HHmmss-ffffff"));

                pathname = $"{dir}/{filename}";
            }
           
            try
            {
                TextWriter tw = new StreamWriter(pathname);
                tw.Write(history);
                tw.Close();
                return pathname;
                // Util.toast($"saved {pathname}");
            }
            catch (Exception e)
            {
                Console.WriteLine("can't write {0}: {1}", pathname, e.Message);
            }

            return null;
        }

        public void hexdump(byte[] buf, string prefix = "", LogLevel lvl=LogLevel.DEBUG)
        {
            string line = "";
            for (int i = 0;  i < buf.Length; i++)
            {
                if (i % 16 == 0)
                {
                    if (i > 0)
                    {
                        log(lvl, "{0}0x{1}", prefix, line);
                        line = "";
                    }
                    line += String.Format("{0:x4}:", i);
                }
                line += String.Format(" {0:x2}", buf[i]);
            }
            if (line.Length > 0)
                log(lvl, "{0}0x{1}", prefix, line);
        }

        // log the first n elements of a labeled array 
        public void logArray(string label, double[] a, int n=5) 
        {
            StringBuilder s = new StringBuilder();
            if (a != null && a.Length > 0)
            {
                s.Append(string.Format("{0:f2}", a[0]));
                for (int i = 1; i < n; i++)
                    s.Append(string.Format(", {0:f2}", a[i]));
            }
            debug($"{label} [len {a.Length}]: {s}");
        }

        public void update() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(history)));

        public StringBuilder history = new StringBuilder("Log data");

        ////////////////////////////////////////////////////////////////////////
        // Private methods
        ////////////////////////////////////////////////////////////////////////

        private Logger()
        {
        }

        string getTimestamp()
        {
            // drop date, as Android phones have narrow screens
            return DateTime.Now.ToString("HH:mm:ss.fff: ", CultureInfo.InvariantCulture);
        }

        void log(LogLevel lvl, string fmt, params Object[] obj)
        {
            // check whether we're logging this level of message
            if (lvl < level || saving)
                return;

            string msg = "";
            if (obj == null)
            {
                msg = getTimestamp() + lvl + ": " + fmt;
            }
            else
            {
                try
                {
                    msg = getTimestamp() + lvl + ": " + String.Format(fmt, obj);
                }
                catch
                {
                    msg = getTimestamp() + lvl + ": [NO ARGS] " + fmt;
                }
            }

            // Console gets littered with a lot of [DOTNET] messages, so mark ours
            Console.WriteLine("[Wasatch] " + msg);

            lock (instance)
            {
                if (outfile != null)
                {
                    outfile.WriteLine(msg);
                    outfile.Flush();
                }

                if (history != null)
                {
                    if (history.Length > AUTOSAVE_SIZE && !saving)
                    {
                        saving = true;
                        history.Append("[autosaving log]\n");
                        save();
                        history.Clear();
                        history.Append("[truncated log after autosave]\n");
                        saving = false;
                    }
                    history.Append(msg + "\n");
                    if (liveUpdates)
                        update();
                }
            }
        }
    }
}
