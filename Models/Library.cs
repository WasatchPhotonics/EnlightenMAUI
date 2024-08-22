using Android.Content.Res;
using Android.OS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.XPath;
using Newtonsoft.Json;
using EnlightenMAUI.Common;

namespace EnlightenMAUI.Models
{
    internal class Library
    {
        Dictionary<string, Measurement> library = new Dictionary<string, Measurement>();
        Dictionary<string, double[]> originalRaws = new Dictionary<string, double[]>();
        Dictionary<string, double[]> originalDarks = new Dictionary<string, double[]>();

        Logger logger = Logger.getInstance();
        List<Task> loaders = new List<Task>();

        Wavecal wavecal;

        public Library(string root, Spectrometer spec)
        {
            logger.debug($"instantiating Library from {root}");
            AssetManager assets = Platform.AppContext.Assets;

            string[] assetP = assets.List("libraries/SiG-785");
            assetP = assets.List(root);

            Regex csvReg = new Regex(@".*\.csv$");
            Regex jsonReg = new Regex(@".*\.json$");

            wavecal = new Wavecal(spec.pixels);
            wavecal.coeffs = spec.eeprom.wavecalCoeffs;
            wavecal.excitationNM = spec.laserExcitationNM;

            foreach (string path in assetP)
            {
                if (jsonReg.IsMatch(path))
                    loaders.Add(loadJSON(root + "/" + path));
                else if (csvReg.IsMatch(path))
                    loaders.Add(loadCSV(root + "/" + path));
            }
        }

        async Task loadCSV(string path) 
        {
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
            library.Add(name, updated);
            logger.info("finish loading library file from {0}", path);
        }

        public async Task<string> findMatch(Measurement spectrum)
        {
            logger.debug("Library.findMatch: trying to match spectrum");

            foreach (Task loader in loaders)
            {
                if (!loader.IsCompleted)
                    await loader;
            }    

            Dictionary<string, double> scores = new Dictionary<string, double>();
            List<Task> matchTasks = new List<Task>();

            foreach (string sample in library.Keys)
            {
                matchTasks.Add(Task.Run(() =>
                {
                    double score = Util.pearsonLibraryMatch(spectrum, library[sample]);
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
                logger.info($"matched {sample} with score {scores[sample]:f4}");

                if (scores[sample] > maxScore)
                {
                    maxScore = scores[sample];
                    finalSample = sample;
                }
            }

            logger.info($"best match {finalSample} with score {maxScore}");

            return finalSample;
        }
    }
}
