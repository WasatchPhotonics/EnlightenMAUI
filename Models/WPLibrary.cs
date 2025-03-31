using Android.Content.Res;
using Android.OS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Xml.XPath;
using Newtonsoft.Json;
using Common = EnlightenMAUI.Common;
using EnlightenMAUI.Platforms;
using System.Reflection.Metadata;

#if USE_DECON
using Deconvolution = DeconvolutionMAUI;
#endif
using EnlightenMAUI.Common;

namespace EnlightenMAUI.Models
{
    public class SimpleCSVParser
    {
        int colWavenumber = 0;
        int colIntensity = 1;
        int VIGNETTE_START = 3; // drop the first 3 pixels
        int VIGNETTE_COUNT = int.MaxValue; // For now don't vignette the end
        string state;

        public List<double> wavenumbers = new List<double>();
        public List<double> intensities = new List<double>();
        public string name = null;
        public ErrorTypes errorType = ErrorTypes.SUCCESS;
        int linecount = 0;
        Logger logger = Logger.getInstance();

        public SimpleCSVParser()
        {
        }

        private bool isNum(string line)
        {
            if (line.Length == 0)
            {
                return false;
            }
            char c = line[0];
            return ('0' <= c && c <= '9') || c == '-';
        }

        private void readHeader(List<string> tok)
        {
            for (int i = 0; i < tok.Count; i++)
            {
                string s = tok[i].ToLower();
                if (s == "wavenumber")
                {
                    colWavenumber = i;
                }
                else if (Regex.Match(s, "processed|spectrum|spectra|intensity").Success)
                {
                    colIntensity = i;
                }
            }
        }

        void readValues(List<string> tok)
        {
            int len = tok.Count;
            if ((len < colWavenumber + 1) || (len < colIntensity + 1)) { return; }
            if (VIGNETTE_COUNT > 0)
            {
                if (intensities.Count >= VIGNETTE_COUNT)
                    return;

                if (linecount++ < VIGNETTE_START)
                    return;
            }

            double wavenumber = Convert.ToDouble(tok[colWavenumber]);
            double intensity = Convert.ToDouble(tok[colIntensity]);

            wavenumbers.Add(wavenumber);
            intensities.Add(intensity);
        }

        public async Task<bool> parseFile(string pathname)
        {
            var assembly = IntrospectionExtensions.GetTypeInfo(typeof(SimpleCSVParser)).Assembly;
            Stream stream = assembly.GetManifestResourceStream(pathname);
            if (stream is null)
            {
                errorType = ErrorTypes.NULL_STREAM;
                return false;
            }

            return await parseStream(stream);
        }

        public async Task<bool> parseStream(Stream stream)
        {
            state = "READING_METADATA";
            string line;
            using (StreamReader sr = new StreamReader(stream))
            {
                while ((line = await sr.ReadLineAsync()) != null)
                {
                    line.Trim();

                    // some files have "CSV blanks" (lines of nothing but commas)
                    if (Regex.Match(line, "^[, ]*$").Success)
                    {
                        line = "";
                    }

                    List<string> tok = line.Split(',').ToList();

                    if (state == "READING_METADATA")
                    {
                        if (isNum(line))
                        {
                            // We found a digit, so either this file doesn't have metadata, 
                            // or we're already past it.  Unfortunately, this probably means
                            // we don't know what the field ordering is, so assume defaults.
                            state = "READING_DATA";
                            readValues(tok);
                        }
                        else if (line.Length == 0)
                        {
                            // we found a blank, so assume next row is header
                            state = "READING_HEADER";
                        }
                        else if (tok.Count > 1)
                        {
                            // process metadata
                            string key = tok[0].Trim().ToLower();
                            string value = tok[1].Trim();

                            if (key == "label")
                                name = value;
                        }
                    }
                    else if (state == "READING_HEADER")
                    {
                        if (line.Length == 0)
                        {
                            // skip extra blank
                        }
                        else if (isNum(line))
                        {
                            state = "READING_DATA";
                            readValues(tok);
                        }
                        else
                        {
                            readHeader(tok);
                            state = "READING_DATA";
                        }
                    }
                    else if (state == "READING_DATA")
                    {
                        readValues(tok);
                    }
                    else
                    {
                        errorType = ErrorTypes.INVALID_STATE;
                        return false;
                    }
                }
            }
            return true;
        }

        public enum ErrorTypes { SUCCESS, NULL_STREAM, INVALID_STATE, NO_INTENSITIES };
    }


    public abstract class Library
    {
#if USE_DECON
        protected Deconvolution.DeconvolutionLibrary deconvolutionLibrary = new Deconvolution.DeconvolutionLibrary(new List<Deconvolution.Spectrum>());
#endif

        public delegate void MatchProgressNotification(double perc);
        public event MatchProgressNotification showMatchProgress;
        public void raiseMatchProgress(double arg)
        {
            showMatchProgress(arg);
        }

        protected Dictionary<string, Measurement> library = new Dictionary<string, Measurement>();
        protected List<string> userCompounds = new List<string>();

        protected Dictionary<string, double[]> originalRaws = new Dictionary<string, double[]>();
        protected Dictionary<string, double[]> originalDarks = new Dictionary<string, double[]>();

        public string mostRecentCompound;
        public double mostRecentScore;
        public Measurement mostRecentMeasurement;
        public event EventHandler<Library> LoadFinished;
        public List<string> samples => library.Keys.ToList();
        public List<string> userSamples => new List<string>(userCompounds);

        public Library(string root, Spectrometer spec) { }

        public abstract Task<Tuple<string, double>> findMatch(Measurement spectrum);
        public abstract Measurement getSample(string name);
        public abstract void addSampleToLibrary(string name, Measurement sample);

        protected void InvokeLoadFinished()
        {
            LoadFinished?.Invoke(this, this);
        }


#if USE_DECON
        public abstract Task<DeconvolutionMAUI.Matches> findDeconvolutionMatches(Measurement spectrum);
#endif
    }


    internal class WPLibrary : Library
    {
        Logger logger = Logger.getInstance();
        protected Task libraryLoader;
        protected Task userLoader;

        protected Wavecal wavecal;
        protected int roiStart = 0;
        protected int roiEnd = 0;

        public WPLibrary(string root, Spectrometer spec, bool doLoad = true) : base(root, spec) 
        {
            logger.debug($"instantiating Library from {root}");

            if (spec != null)
            {
                wavecal = new Wavecal(spec.pixels);
                wavecal.coeffs = spec.eeprom.wavecalCoeffs;
                wavecal.excitationNM = spec.laserExcitationNM;

                roiStart = spec.eeprom.ROIHorizStart;
                roiEnd = spec.eeprom.ROIHorizEnd;
            }

            if (doLoad)
                libraryLoader = loadFiles(root);

            userLoader = loadFiles("User Library", false);

            logger.debug($"finished initializing library load from {root}");
        }

        void exploreDataFiles()
        {
            /*
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
            */

            /*
            string dataPath = FileSystem.Current.AppDataDirectory;
            logger.debug("looking for files in {0}", dataPath);
            var files = Directory.GetFiles(dataPath);
            foreach (var file in files)
            {
                logger.debug("found file {0}", file);
            }
            */

            //ContentResolver cr = Platform.AppContext.ContentResolver;
            //cr.OpenInputStream();
            //actiona
            //Android.Content.Intent intent = new Android.Content.Intent(Android.Content.Intent.ActionOpenDocument);
            //intent.

        }

        public override Measurement getSample(string name)
        {
            if (library.ContainsKey(name))
                return library[name];
            else 
                return null;
        }

        public override void addSampleToLibrary(string name, Measurement sample)
        {
            Measurement adjusted = new Measurement();
            
            if (sample.wavenumbers[0] == 400 && sample.wavenumbers.Length == 2008 && sample.wavenumbers.Last() == 2407)
            {
                adjusted = sample.copy();
                adjusted.dark = null;
                adjusted.raw = sample.postProcessed;
            }
            else
            {
                double[] wavenumbers = Enumerable.Range(400, 2008).Select(x => (double)x).ToArray();
                double[] newIntensities = Wavecal.mapWavenumbers(sample.wavenumbers, sample.processed, wavenumbers);
                adjusted.wavenumbers = wavenumbers;
                adjusted.dark = null;
                adjusted.raw = newIntensities;
            }

            library[name] = adjusted;
        }

        async Task loadFiles(string root, bool doDecon = true, string correctionFileName = "etalon_correction.json")
        {
            var cacheDirs = Platform.AppContext.GetExternalFilesDirs(null);
            Java.IO.File libraryFolder = null;
            foreach (var cDir in cacheDirs)
            {
                var subs = cDir.ListFiles();
                foreach(var sub in subs)
                {
                    if (sub.AbsolutePath.Split('/').Last() == root)
                    {
                        libraryFolder = sub;
                        break;
                    }
                }

            }

            if (libraryFolder == null)
                return;

            Regex csvReg = new Regex(@".*\.csv$");
            Regex jsonReg = new Regex(@".*\.json$");

            var libraryFiles = libraryFolder.ListFiles();

            foreach (var libraryFile in libraryFiles)
            {
                if (jsonReg.IsMatch(libraryFile.AbsolutePath))
                {
                    try
                    {
                        await loadJSON(libraryFile, isUserFile: root == "User Library");
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
                        await loadCSV(libraryFile, isUserFile: root == "User Library");
                    }
                    catch (Exception e)
                    {
                        logger.debug("loading {0} failed with exception {1}", libraryFile.AbsolutePath, e.Message);
                    }
                }
            }
            logger.debug("finished loading library files");
            InvokeLoadFinished();
            logger.debug("prepping data for decon");

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
        }
        async Task loadCSV(string path)
        {
            string name = path.Split('/').Last().Split('.').First();

            SimpleCSVParser parser = new SimpleCSVParser();
            AssetManager assets = Platform.AppContext.Assets;
            Stream s = assets.Open(path);
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

            Measurement updated = wavecal.crossMapWavenumberData(m.wavenumbers, m.raw);
            double airPLSLambda = 10000;
            int airPLSMaxIter = 100;
            double[] array = AirPLS.smooth(updated.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)roiStart, (int)roiEnd);
            double[] shortened = new double[updated.processed.Length];
            Array.Copy(array, 0, shortened, roiStart, array.Length);
            updated.raw = shortened;
            updated.dark = null;

            library.Add(name, updated);

#if USE_DECON
            deconvolutionLibrary.library.Add(name, spec);
#endif

            logger.info("finish loading library file from {0}", path);
        }
        async Task loadCSV(Java.IO.File file, bool isUserFile = false)
        {
            logger.info("start loading library file from {0}", file.AbsolutePath);

            string name = file.AbsolutePath.Split('/').Last().Split('.').First();

            SimpleCSVParser parser = new SimpleCSVParser();
            Stream s = File.OpenRead(file.AbsolutePath); 
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
                double[] smoothed = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed, "");
                double[]  wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
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
                if (isUserFile && !userCompounds.Contains(name))
                    userCompounds.Add(name);

#if USE_DECON
                Deconvolution.Spectrum upSpec = new Deconvolution.Spectrum(new List<double>(updated.wavenumbers), new List<double>(updated.processed));
                deconvolutionLibrary.library.Add(name, upSpec);
#endif
            }

            logger.info("finish loading library file from {0}", file.AbsolutePath);
        }
        async Task loadJSON(string path)
        {
            logger.info("start loading library file from {0}", path);



            string name = path.Split('/').Last().Split('.').First();

            AssetManager assets = Platform.AppContext.Assets;
            Stream s = assets.Open(path);
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

            Measurement updated = wavecal.crossMapIntensityWavenumber(otherCal, m);
            double airPLSLambda = 10000;
            int airPLSMaxIter = 100;
            double[] array = AirPLS.smooth(updated.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)roiStart, (int)roiEnd);
            double[] shortened = new double[updated.processed.Length];
            Array.Copy(array, 0, shortened, roiStart, array.Length);
            updated.raw = shortened;
            updated.dark = null;

            library.Add(name, updated);
            logger.info("finish loading library file from {0}", path);
        }
        async Task loadJSON(Java.IO.File file, bool isUserFile = false)
        {
            logger.info("start loading library file from {0}", file.AbsolutePath);

            string name = file.AbsolutePath.Split('/').Last().Split('.').First();

            SimpleCSVParser parser = new SimpleCSVParser();
            Stream s = File.OpenRead(file.AbsolutePath);
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
                double[] smoothed = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed, "");
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
                Measurement updated = wavecal.crossMapIntensityWavenumber(otherCal, m);
                double airPLSLambda = 10000;
                int airPLSMaxIter = 100;
                double[] array = AirPLS.smooth(updated.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)roiStart, (int)roiEnd);
                double[] shortened = new double[updated.processed.Length];
                Array.Copy(array, 0, shortened, roiStart, array.Length);
                updated.raw = shortened;
                updated.dark = null;

                library.Add(name, updated);
                if (isUserFile && !userCompounds.Contains(name))
                    userCompounds.Add(name);
            }

            logger.info("finish loading library file from {0}", file.AbsolutePath);
        }
        public override async Task<Tuple<string,double>> findMatch(Measurement spectrum)
        {
            logger.debug("Library.findMatch: trying to match spectrum");

            await libraryLoader;

            logger.debug("Library.findMatch: library is loaded");

            Dictionary<string, double> scores = new Dictionary<string, double>();
            List<Task> matchTasks = new List<Task>();

            foreach (string sample in library.Keys)
            {
                double score = Common.Util.pearsonLibraryMatch(spectrum, library[sample], smooth: !PlatformUtil.transformerLoaded);
                logger.info($"{sample} score: {score}");
                scores[sample] = score;
                /*
                logger.info($"trying to match {sample}");
                matchTasks.Add(Task.Run(() =>
                {
                }));
                */
            }

            /*
            logger.info("waiting for matches");
            int i = 0;
            foreach (Task t in matchTasks)
            {
                logger.debug("waiting for match {0}", i + 1);
                await t;
                ++i;
            }
            */
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
            mostRecentMeasurement = library[finalSample];

            if (finalSample != "")
                return new Tuple<string, double>(finalSample, maxScore);
            else 
                return null;
        }

#if USE_DECON
        public override async Task<DeconvolutionMAUI.Matches> findDeconvolutionMatches(Measurement spectrum)
        {
            List<double> intensities = new List<double>(spectrum.processed);
            List<double> wavenumbers = new List<double>(spectrum.wavenumbers);

            Deconvolution.Spectrum spec = new Deconvolution.Spectrum(wavenumbers, intensities);
            Deconvolution.Matches matches = null;

            matches = await deconvolutionLibrary.process(spec, 0.95);

            return matches;
        }
#endif

    }
}