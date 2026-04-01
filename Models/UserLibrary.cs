using EnlightenMAUI.Platforms;
using System;
using System.Collections.Generic;
using System.Text;

namespace EnlightenMAUI.Models
{
    internal class UserLibrary
    {
        static UserLibrary instance = null;

        Settings settings = Settings.getInstance();
        Dictionary<string, string> userSpectra = new Dictionary<string, string>();
        public List<string> userSpectraKeys
        {
            get
            {
                return new List<string>(userSpectra.Keys);
            }
        }

        static public UserLibrary getInstance()
        {
            if (instance == null)
                instance = new UserLibrary();

            return instance;
        }

        public UserLibrary()
        {
            loadTodaySpectra();
        }

        public async Task loadTodaySpectra()
        {
            Dictionary<string, Measurement> library = new Dictionary<string, Measurement>();
            Dictionary<string, double[]> originalRaws = new Dictionary<string, double[]>();
            Dictionary<string, double[]> originalDarks = new Dictionary<string, double[]>();
            string path = PlatformUtil.getAutoSavePath(settings.highLevelAutoSave);
            await PlatformUtil.loadFiles(useAssets: false, path, library, originalRaws, originalDarks, true, null, skipSearch: true);

            foreach (string tag in library.Keys)
            {
                Measurement m = library[tag];
                string userTag = $"{m.timestamp.ToString("HH:mm")} {(m.declaredMatch != null && m.declaredMatch.Length > 0 ? m.declaredMatch[0] : "")} {(m.declaredScore.HasValue ? m.declaredScore.Value.ToString("f2") : "")}";
                Logger.getInstance().debug("adding {0} to user library as {1}", userTag, tag);
                userSpectra.Add(userTag, tag);
            }
        }

        public void addSpectrum(Measurement m, string tag)
        {
            string userTag = $"{m.timestamp.ToString("HH:mm")} {(m.declaredMatch != null && m.declaredMatch.Length > 0 ? m.declaredMatch[0] : "")} {(m.declaredScore.HasValue ? m.declaredScore.Value.ToString("f2") : "")}";
            if (!userSpectra.ContainsKey(userTag))
            {
                Logger.getInstance().debug("adding {0} to user library as {1}", userTag, tag);
                userSpectra.Add(userTag, tag);
            }
        }
    }
}
