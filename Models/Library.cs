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
using static Java.Util.Jar.Attributes;
using Deconvolution = DeconvolutionMAUI;
using Android.Renderscripts;
using EnlightenMAUI.Common;
using static Android.Widget.GridLayout;

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

    internal class Library
    {
        Deconvolution.DeconvolutionLibrary deconvolutionLibrary = new Deconvolution.DeconvolutionLibrary(new List<Deconvolution.Spectrum>());
        Dictionary<string, Measurement> library = new Dictionary<string, Measurement>();
        Dictionary<string, double[]> originalRaws = new Dictionary<string, double[]>();
        Dictionary<string, double[]> originalDarks = new Dictionary<string, double[]>();

        Logger logger = Logger.getInstance();
        Task libraryLoader;

        Wavecal wavecal;
        int roiStart = 0;
        int roiEnd = 0;

        public Library(string root, Spectrometer spec)
        {
            logger.debug($"instantiating Library from {root}");

            wavecal = new Wavecal(spec.pixels);
            wavecal.coeffs = spec.eeprom.wavecalCoeffs;
            wavecal.excitationNM = spec.laserExcitationNM;

            roiStart = spec.eeprom.ROIHorizStart;
            roiEnd = spec.eeprom.ROIHorizEnd;

            libraryLoader = loadFiles(root);

            logger.debug($"finished initializing library load from {root}");
        }

        async Task loadFiles(string root)
        {
            AssetManager assets = Platform.AppContext.Assets;

            string[] assetP = assets.List(root);

            Regex csvReg = new Regex(@".*\.csv$");
            Regex jsonReg = new Regex(@".*\.json$");

            foreach (string path in assetP)
            {
                if (jsonReg.IsMatch(path))
                    await loadJSON(root + "/" + path);
                else if (csvReg.IsMatch(path))
                    await loadCSV(root + "/" + path);
            }
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
            Deconvolution.Spectrum spec = new Deconvolution.Spectrum(parser.wavenumbers, parser.intensities);

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
            deconvolutionLibrary.library.Add(name, spec);

            logger.info("finish loading library file from {0}", path);
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

        public async Task<Tuple<string,double>> findMatch(Measurement spectrum)
        {
            logger.debug("Library.findMatch: trying to match spectrum");

            await libraryLoader;

            Dictionary<string, double> scores = new Dictionary<string, double>();
            List<Task> matchTasks = new List<Task>();

            foreach (string sample in library.Keys)
            {
                matchTasks.Add(Task.Run(() =>
                {
                    double score = Common.Util.pearsonLibraryMatch(spectrum, library[sample]);
                    scores[sample] = score;
                }));
            }

            foreach (Task t in matchTasks)
            {
                await t;
            }

            double maxScore = double.MinValue;
            string finalSample = "";
            foreach (string sample in scores.Keys)
            {
                //logger.info($"matched {sample} with score {scores[sample]:f4}");

                if (scores[sample] > maxScore)
                {
                    maxScore = scores[sample];
                    finalSample = sample;
                }
            }

            logger.info($"best match {finalSample} with score {maxScore}");

            return new Tuple<string, double>(finalSample, maxScore);
        }

        public async Task<DeconvolutionMAUI.Matches> findDeconvolutionMatches(Measurement spectrum)
        {
            List<double> intensities = new List<double>(spectrum.processed);
            List<double> wavenumbers = new List<double>(spectrum.wavenumbers);

            Deconvolution.Spectrum spec = new Deconvolution.Spectrum(wavenumbers, intensities);
            Deconvolution.Matches matches = null;

            await Task.Run(() => matches = deconvolutionLibrary.process(spec, 0.8));

            return matches;
        }

    }
}