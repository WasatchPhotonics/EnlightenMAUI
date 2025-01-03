using EnlightenMAUI.Platforms;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Models
{
    internal struct dpSpectrum
    {
        public float xfirst;
        public float xstep;
        public int npoints;
        public float[] y;
    }

    internal class DPLibrary : Library
    {
        private int _lib = 0;
        private byte[] _data = new byte[250000];
        public dpSpectrum spectrum = new dpSpectrum();
        public bool loaded;
        Logger logger = Logger.getInstance();

        public DPLibrary(string root, Spectrometer spec) : base(root, spec)
        {
            try
            {
                _dpLIBInit(Encoding.UTF8.GetBytes(spec.eeprom.serialNumber + '\0'));

                if (_lib != 0)
                    _dpLIBClose(_lib);
                _lib = _dpLIBOpen(Encoding.UTF8.GetBytes(root + '\0'));
                if (_lib == 0)
                {
                    var cacheDirs = Platform.AppContext.GetExternalFilesDirs(null);
                    string fullPath = "";
                    bool fullPathFound = false;
                    foreach (var cDir in cacheDirs)
                    {
                        var subs = cDir.ListFiles();
                        foreach (var sub in subs)
                        {
                            if (sub.AbsolutePath.Split('/').Last() == root)
                            {
                                fullPath = sub.AbsolutePath;    
                                fullPathFound = true;
                                break;
                            }
                        }
                    }

                    if (fullPathFound)
                        _lib = _dpLIBOpen(Encoding.UTF8.GetBytes(fullPath + '\0'));
                }


                loaded = _lib != 0;

                logger.info("dplibrary load finished with code {0}", _lib);

                if (loaded)
                {
                    int len = _dpLIBInfo(_lib, _data, _data.Length);
                    logger.info("dplibrary contains {0} items", len);
                    if (len > 0)
                    {
                        Dictionary<string, string> libraryDat = _todict(len);
                        foreach (string key in libraryDat.Keys)
                        {
                            logger.info("dplibrary item: {0} ; {1}", key, libraryDat[key]);
                        }
                    }
                }
                else
                {
                    logger.info("dplibrary failed to load");
                }
            }
            catch (Exception e)
            {
                logger.error("DPLibrary init failed out with issue: {0}", e.Message);
            }

        }

        public override async Task<Tuple<string, double>> findMatch(Measurement m)
        {
            dpSpectrum spectrum = convertMeasurement(m);

            return null;
        }

        public override Measurement getSample(string name)
        {
            return null;
        }

        public override void addSampleToLibrary(string name, Measurement sample) { }


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

        private dpSpectrum convertMeasurement(Measurement m)
        {
            dpSpectrum spec = new dpSpectrum();

            spec.xfirst = (float)m.wavenumbers[0];
            spec.xstep = 1;
            spec.npoints = m.processed.Length;
            spec.y = m.processed.Select(x => (float)x).ToArray();

            return spec;
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
