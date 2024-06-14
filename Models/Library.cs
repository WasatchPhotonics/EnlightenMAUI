using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.XPath;

namespace EnlightenMAUI.Models
{
    internal class Library
    {
        // @todo: take from EEPROM? Lookup per Model?
        public const int START_WAVENUMBER = 400;
        public const int END_WAVENUMBER = 2400;

        Dictionary<string, Spectrum> library;

        Logger logger = Logger.getInstance();

        public Library(string pathname)
        {
            logger.debug($"instantiating Library from {pathname}");

            string[] filenames = Directory.GetFiles("/storage/3439-3532/DCIM/Camera", "*");
            // walk directory, find .csv files
            //   instantiate each into Spectrum
            //   take label from CSV
            //   store in library

            logger.debug($"loaded {library.Count} library spectra");
        }

        public string findMatch(Spectrum spectrum)
        {
            logger.debug("Library.findMatch: trying to match spectrum");

            return null;
        }
    }
}
