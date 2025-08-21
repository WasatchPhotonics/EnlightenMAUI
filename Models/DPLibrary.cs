using Android.Content;
using Android.Content.Res;
using EnlightenMAUI.Platforms;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Microsoft.Maui.LifecycleEvents.AndroidLifecycle;
using Telerik.Windows.Documents.Spreadsheet.Expressions.Functions;
using static Android.Provider.DocumentsContract;
using static Java.Util.Jar.Attributes;
using Android.Renderscripts;
using System.Runtime.CompilerServices;

namespace EnlightenMAUI.Models
{
    internal struct dpSpectrum
    {
        public float xfirst;
        public float xstep;
        public int npoints;
        public float[] y;
    }

    internal partial class DPLibrary : WPLibrary
    {
        private int _lib = 0;
        private int maxYPoints = 0;
        const int matchThreadCount = 8;
        private byte[] _data = new byte[250000];
        public dpSpectrum spectrum = new dpSpectrum();
        public bool loaded = false;
        Logger logger = Logger.getInstance();
        Dictionary<string, string> libraryIDs = new Dictionary<string, string>();
        Dictionary<string, bool> activeLibraries = new Dictionary<string, bool>();

        List<string> defaultSublibs = new List<string>()
        {
            "Alcohols, Phenols",
            "Forensic",
            "Hazardous Chemicals",
            "Narcotics, Drugs, Controlled Substances Vol. 2 (customs)"
        };

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

            if (spec == null)
                unitSN = "WP-01486";

            isLoading = true;
            libraryLoader = loadFiles();
        }

        async Task loadFiles()
        {
            try
            {
                libraryIDs.Clear();
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

                if (_lib != 0)
                    _dpLIBClose(_lib);

                string serialString = $"SN={unitSN}\0";
                byte[] argument = Encoding.ASCII.GetBytes(serialString);
                _dpLIBInit(argument);

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
                    maxYPoints = _dpLIBMxNPoints(_lib);
                    spectrum.y = new float[maxYPoints];

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
                    string libIDs = "";
                    if (len > 0)
                    {
                        Dictionary<string, string> libs = _todict(len);
                        foreach (string key in libs.Keys)
                        {
                            logger.info("dplibrary item: {0} : {1}", key, libs[key]);
                            libraryIDs.Add(libs[key], key);
                            activeLibraries.Add(libs[key], defaultSublibs.Contains(libs[key]));

                            if (defaultSublibs.Contains(libs[key]))
                            {
                                libIDs += key;
                                libIDs += ";";
                            }
                        }
                    }
                    libIDs = libIDs.TrimEnd(';');

                    _dpLIBSetFilter(_lib, Encoding.UTF8.GetBytes(libIDs + '\0'));

                    
                    int numSpec = _dpLIBNumSpectra(_lib);
                    logger.info("library contains {0} items", numSpec);

                    /*
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
                    */

                    tag = "3rd Party";
                    logger.info("library loaded successfully");
                    loadSucceeded = true;
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

        public ObservableCollection<Tuple<string, bool>> LibraryOptions
        { 
            get
            {
                var temp = new ObservableCollection<Tuple<string, bool>>();

                foreach (var pair in activeLibraries)
                {
                    temp.Add(new Tuple<string, bool>(pair.Key, pair.Value));
                }
                return temp;
            }
        }

        public void setFilter(Dictionary<string, bool> selections = null)
        {
            string libIDs = "";
            if (selections != null)
                activeLibraries = selections;

            foreach (string key in activeLibraries.Keys)
            {
                if (activeLibraries[key])
                {
                    libIDs += libraryIDs[key];
                    libIDs += ";";
                }
            }

            libIDs = libIDs.TrimEnd(';');
            _dpLIBSetFilter(_lib, Encoding.UTF8.GetBytes(libIDs + '\0'));
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

        Measurement getFittedMeasurement(int i)
        {
            byte[] xfirst = new byte[4];
            byte[] xstep = new byte[4];
            byte[] npoints = new byte[4];

            if (i % 1000 == 0)
                logger.info("retrieving spectrum meta");
            int len = _dpLIBGetSpectrumData(_lib, i, _data, _data.Length);
            if (i % 1000 == 0)
                logger.info("retrieved spectrum meta");
            Dictionary<string, string> info = _todict(len);

            dpSpectrum spec = new dpSpectrum();
            spec.y = new float[maxYPoints];
            spec.xfirst = 200.0F;
            spec.xstep = 2.0F;
            spec.npoints = 0;

            if (i % 1000 == 0)
                logger.info("retrieving spectrum");
            bool res = _dpLIBGetSpectrum(_lib, i, xfirst, xstep, npoints, spec.y.Length, spec.y);
            if (i % 1000 == 0)
                logger.info("retrieved spectrum");

            if (res)
            {
                spec.xfirst = BitConverter.ToSingle(xfirst);
                spec.xstep = BitConverter.ToSingle(xstep);
                spec.npoints = BitConverter.ToInt32(npoints);

            }
            else
                return null;

            //logger.debug("{0} loaded", info["Name"]);

            Measurement m = new Measurement();
            m.wavenumbers = new double[2008];
            m.raw = new double[2008];
            if (info.ContainsKey("RamanExci"))
            {
                bool hasLetter = false;
                StringBuilder sb = new StringBuilder();
                foreach (char c in info["RamanExci"])
                {
                    if (char.IsLetter(c))
                    {
                        hasLetter = true;
                    }
                    else if (char.IsDigit(c))
                        sb.Append(c);
                }

                /*
                if (hasLetter)
                {
                    logger.info("excitation parse problem {0}", info["RamanExci"]);
                }
                */

                if (sb.Length > 0)
                    m.excitationNM = Double.Parse(sb.ToString());
                else
                    m.excitationNM = 600;
            }
            else
                m.excitationNM = 600;
            m.tag = info["Name"];

            int index = 0;
            while (spec.xfirst + spec.xstep * index < 400)
                ++index;


            if (i % 1000 == 0)
                logger.info("stretching {0} spectrum", m.tag);
            for (int j = index; j < spec.npoints; j++)
            {
                m.wavenumbers[(j * 2 - index * 2)] = spec.xfirst + spec.xstep * j;
                m.raw[(j * 2 - index * 2)] = spec.y[j];

                if ((j * 2 - index * 2) + 1 < 2008)
                {
                    m.wavenumbers[(j * 2 - index * 2) + 1] = spec.xfirst + spec.xstep * j + 0.5 * spec.xstep;
                    m.raw[(j * 2 - index * 2) + 1] = (spec.y[j] + spec.y[j + 1]) / 2;
                }
                else
                {
                    break;
                }

                if (spec.xfirst + spec.xstep * j == 2406)
                    break;
            }

            if (i % 1000 == 0)
                logger.info("processing {0} spectrum", m.tag);
            m.postProcess();
            if (i % 1000 == 0)
                logger.info("{0} stitched and processed", info["Name"]);

                return m;
        }

        public override async Task<Tuple<string, double>> findMatch(Measurement spec)
        {
            try
            {
                raiseMatchProgress(0);

                logger.debug("Library.findMatch: trying to match spectrum");

                await libraryLoader;

                logger.debug("Library.findMatch: library is loaded");

                Dictionary<string, double> scores = new Dictionary<string, double>();
                List<Task> matchTasks = new List<Task>();

                foreach (string sample in library.Keys)
                {
                    double score = 0;
                    await Task.Run(() => score = Common.Util.pearsonLibraryMatch(spec, library[sample], smooth: !PlatformUtil.transformerLoaded));
                    logger.info($"{sample} score: {score}");
                    scores[sample] = score;
                }


                int numSpec = _dpLIBNumSpectra(_lib);
                logger.info("library contains {0} items", numSpec);

                Dictionary<string, int> indexLookup = new Dictionary<string, int>();

                for (int i = 0; i < numSpec; i++)
                {
                    Measurement m = getFittedMeasurement(i);

                    if (m != null) // get the spectrum
                    {
                        if (i % 1000 == 0)
                            logger.info("starting Pearson for {0}", m.tag);

                        double score = 0;
                        if (i % 100 == 0)
                        {
                            await Task.Run(() => score = Common.Util.pearsonLibraryMatch(spec, m, smooth: !PlatformUtil.transformerLoaded));
                            raiseMatchProgress((float)i / numSpec);
                        }
                        else
                        {
                            score = Common.Util.pearsonLibraryMatch(spec, m, smooth: !PlatformUtil.transformerLoaded);
                        }
                        if (i % 1000 == 0)
                            logger.info("finished Pearson for {0}", m.tag);
                        //logger.debug("{0} matched", info["Name"]);
                        //logger.debug($"{m.tag} score: {score}");
                        scores[m.tag] = score;
                        indexLookup[m.tag] = i;
                    }
                }

                logger.info("matches complete");

                double maxScore = double.MinValue;
                string finalSample = "";
                foreach (string sample in scores.Keys)
                {
                    logger.info($"matched {sample} with score {scores[sample]:f4}");

                    if (scores[sample] > maxScore)
                    {
                        maxScore = scores[sample];
                        finalSample = sample;
                    }
                }

                logger.info($"best match {finalSample} with score {maxScore}");

                mostRecentCompound = finalSample;
                mostRecentScore = maxScore;
                if (library.ContainsKey(finalSample))
                    mostRecentMeasurement = library[finalSample];
                else
                    mostRecentMeasurement = getFittedMeasurement(indexLookup[finalSample]);

                if (finalSample != "")
                    return new Tuple<string, double>(finalSample, maxScore);
                else
                    return null;
            }
            catch (Exception e)
            {
                logger.info("match failed with issue {0}", e.Message);
                return null;
            }
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

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBInit")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial void _dpLIBInit(byte[] s);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBLibInfo")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int _dpLIBLibInfo(byte[] file, ref int days);


        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBOpen")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int _dpLIBOpen(byte[] f);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBInfo")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int _dpLIBInfo(int handle, byte[] data, int len);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBClose")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial void _dpLIBClose(int handle);


        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBActiveLibIDs")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int _dpLIBActiveLibIDs(int handle, byte[] data, int len);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBActiveLibs")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int _dpLIBActiveLibs(int handle, byte[] data, int len);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBSetFilter")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial void _dpLIBSetFilter(int handle, byte[] data);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBResetFilter")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial void _dpLIBResetFilter(int handle);


        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBNumSpectra")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int _dpLIBNumSpectra(int handle);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBMxNPoints")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int _dpLIBMxNPoints(int handle);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBGetSpectrum")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool _dpLIBGetSpectrum(int handle, int i, byte[] xfirst, byte[] xstep, byte[] npoints, int yalloc, float[] y);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBGetSpectrumData")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int _dpLIBGetSpectrumData(int handle, int i, byte[] data, int len);

        [LibraryImport(@"Stj.ProtectionSdk", EntryPoint = "dpLIBGetLibIDsForSpectrum")]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        private static partial int _dpLIBGetLibIDsForSpectrum(int handle, int i, byte[] data, int len);

    }
}
