using Android.Content.Res;
using Android.OS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.XPath;

namespace EnlightenMAUI.Models
{
    internal class Library
    {
        Dictionary<string, Measurement> library;
        Dictionary<string, double[]> originalRaws;
        Dictionary<string, double[]> originalDarks;

        Logger logger = Logger.getInstance();
        List<Task> loaders = new List<Task>();

        public Library(string root, Spectrometer spec)
        {
            logger.debug($"instantiating Library from {root}");
            AssetManager assets = Platform.AppContext.Assets;
            string[] assetP = assets.List(root);

            Regex csvReg = new Regex(@".*\.csv$");
            Regex jsonReg = new Regex(@".*\.json$");


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
            AssetManager assets = Platform.AppContext.Assets;
            Stream s = assets.Open(path);
            StreamReader sr = new StreamReader(s);
            string blob = await sr.ReadToEndAsync();
        }

        public string findMatch(Measurement spectrum)
        {
            logger.debug("Library.findMatch: trying to match spectrum");

            return null;
        }
    }
}
