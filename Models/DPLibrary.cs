using Android.Content;
using Android.Content.Res;
using EnlightenMAUI.Platforms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Microsoft.Maui.LifecycleEvents.AndroidLifecycle;
using Telerik.Windows.Documents.Spreadsheet.Expressions.Functions;
using static Android.Provider.DocumentsContract;
using static Java.Util.Jar.Attributes;

namespace EnlightenMAUI.Models
{
    internal struct dpSpectrum
    {
        public float xfirst;
        public float xstep;
        public int npoints;
        public float[] y;
    }

    internal class DPLibrary : WPLibrary
    {
        private int _lib = 0;
        private byte[] _data = new byte[250000];
        public dpSpectrum spectrum = new dpSpectrum();
        public bool loaded = false;
        public bool isLoading = false;
        Logger logger = Logger.getInstance();

        public class DPCR : Android.Content.ContentResolver
        {
            public DPCR(Context context) : base(context)
            {

            }
        }

        public DPLibrary(string root, Spectrometer spec) : base(root, spec, false)
        {
            spectrum.y = new float[0];
            spectrum.xfirst = 200.0F;
            spectrum.xstep = 2.0F;
            spectrum.npoints = 0;

            isLoading = true;
            libraryLoader = loadFiles();
        }

        async Task loadFiles()
        {
            try
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
                        if (file.Split('.').Last() == "idex")
                            finalFullPath = fullPath;
                    }
                }

                if (_lib != 0)
                    _dpLIBClose(_lib);


                if (finalFullPath.Length == 0)
                {
                    logger.info("dplibrary failed to load");
                    isLoading = false;
                    InvokeLoadFinished();
                    return;
                }

                if (_lib == 0)
                {
                    _lib = _dpLIBOpen(Encoding.UTF8.GetBytes(finalFullPath + '\0'));
                }

                loaded = _lib != 0;

                logger.info("dplibrary load finished with code {0}", _lib);

                if (loaded)
                {
                    spectrum.y = new float[_dpLIBMxNPoints(_lib)];

                    int len = _dpLIBInfo(_lib, _data, _data.Length);
                    logger.info("dplibrary contains {0} items", len);
                    if (len > 0)
                    {
                        Dictionary<string, string> libraryDat = _todict(len);
                        foreach (string key in libraryDat.Keys)
                        {
                            logger.info("dplibrary info: {0} : {1}", key, libraryDat[key]);
                        }
                    }

                    len = _dpLIBActiveLibIDs(_lib, _data, _data.Length);
                    if (len > 0)
                    {
                        string libraryID = _tostring(len);
                        logger.info("active lib id: {0}", libraryID);
                    }

                    len = _dpLIBActiveLibs(_lib, _data, _data.Length);
                    if (len > 0)
                    {
                        Dictionary<string, string> libs = _todict(len);
                        foreach (string key in libs.Keys)
                        {
                            logger.info("dplibrary item: {0} : {1}", key, libs[key]);
                        }
                    }

                    int numSpec = _dpLIBNumSpectra(_lib);
                    logger.info("library contains {0} items", numSpec);

                    for (int i = 0; i < numSpec; i++)
                    {
                        len = _dpLIBGetSpectrumData(_lib, i, _data, _data.Length);
                        Dictionary<string, string> info = _todict(len);
                        logger.info("item {0}", i);
                        foreach (KeyValuePair<string, string> kvp in info)
                            logger.info("{0} = {1}", kvp.Key, kvp.Value);

                        if (getSpectrum(i)) // get the spectrum
                        {
                            Measurement m = new Measurement();
                            m.wavenumbers = new double[spectrum.npoints];
                            m.raw = new double[spectrum.npoints];
                            m.excitationNM = Double.Parse(info["RamanExci"]);

                            for (int j = 0; j < spectrum.npoints; j++)
                            {
                                m.wavenumbers[j] = spectrum.xfirst + spectrum.xstep * j;
                                m.raw[j] = spectrum.y[j];
                            }

                            m.postProcess();

                            double[] wavenumbers = Enumerable.Range(400, 2008).Select(x => (double)x).ToArray();
                            double[] newIntensities = Wavecal.mapWavenumbers(m.wavenumbers, m.processed, wavenumbers);

                            Measurement updated = new Measurement();
                            updated.wavenumbers = wavenumbers;
                            updated.raw = newIntensities;

                            if (!library.ContainsKey(info["Name"].ToLower()))
                            library.Add(info["Name"].ToLower(), updated);
                        }
                    }

                    logger.info("library loaded successfully");
                    isLoading = false;
                    InvokeLoadFinished();
                }
                else
                {
                    logger.info("dplibrary failed to load");
                    isLoading = false;
                    InvokeLoadFinished();
                }
            }
            catch (Exception e)
            {
                logger.error("DPLibrary init failed out with issue: {0}", e.Message);
                isLoading = false;
                InvokeLoadFinished();
            }

            isLoading = false;
        }

        public async Task<bool> isLoaded()
        {
            await libraryLoader;
            return loaded;
        }

        bool getSpectrum(int index)
        {
            byte[] xfirst = new byte[4];
            byte[] xstep = new byte[4];
            byte[] npoints = new byte[4];

            bool res = _dpLIBGetSpectrum(_lib, index, xfirst, xstep, npoints, spectrum.y.Length, spectrum.y);

            if (res)
            {
                spectrum.xfirst = BitConverter.ToSingle(xfirst);
                spectrum.xstep = BitConverter.ToSingle(xstep);
                spectrum.npoints = BitConverter.ToInt32(npoints);
            }

            return res;
        }

#if USE_DECON
        public override Task<DeconvolutionMAUI.Matches> findDeconvolutionMatches(Measurement spectrum)
        {
            return null;
        }
#endif


        Dictionary<string, string> _todict(int len)
        {
            return Encoding.UTF8.GetString(_data, 0, len).Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('=')).ToDictionary(split => split[0], split => split[1]);
        }
        string _tostring(int len)
        {
            return Encoding.UTF8.GetString(_data, 0, len);
        }

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBInit", CallingConvention = CallingConvention.StdCall)]
        private static extern void _dpLIBInit(byte[] s);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBLibInfo", CallingConvention = CallingConvention.StdCall)]
        private static extern int _dpLIBLibInfo(byte[] file, ref int days);


        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBOpen", CallingConvention = CallingConvention.StdCall)]
        private static extern int _dpLIBOpen(byte[] f);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBInfo", CallingConvention = CallingConvention.StdCall)]
        private static extern int _dpLIBInfo(int handle, byte[] data, int len);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBClose", CallingConvention = CallingConvention.StdCall)]
        private static extern void _dpLIBClose(int handle);


        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBActiveLibIDs", CallingConvention = CallingConvention.StdCall)]
        private static extern int _dpLIBActiveLibIDs(int handle, byte[] data, int len);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBActiveLibs", CallingConvention = CallingConvention.StdCall)]
        private static extern int _dpLIBActiveLibs(int handle, byte[] data, int len);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBSetFilter", CallingConvention = CallingConvention.StdCall)]
        private static extern void _dpLIBSetFilter(int handle, byte[] data);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBResetFilter", CallingConvention = CallingConvention.StdCall)]
        private static extern void _dpLIBResetFilter(int handle);


        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBNumSpectra", CallingConvention = CallingConvention.StdCall)]
        private static extern int _dpLIBNumSpectra(int handle);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBMxNPoints", CallingConvention = CallingConvention.StdCall)]
        private static extern int _dpLIBMxNPoints(int handle);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBGetSpectrum", CallingConvention = CallingConvention.StdCall)]
        private static extern bool _dpLIBGetSpectrum(int handle, int i, byte[] xfirst, byte[] xstep, byte[] npoints, int yalloc, float[] y);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBGetSpectrumData", CallingConvention = CallingConvention.StdCall)]
        private static extern int _dpLIBGetSpectrumData(int handle, int i, byte[] data, int len);

        [DllImport(@"libdpSDK.so", EntryPoint = "dpLIBGetLibIDsForSpectrum", CallingConvention = CallingConvention.StdCall)]
        private static extern int _dpLIBGetLibIDsForSpectrum(int handle, int i, byte[] data, int len);

    }
}
