using EnlightenMAUI.Platforms;
using System;
using System.Collections.Generic;
using System.Text;

namespace EnlightenMAUI.Models
{
    internal class UserLibrary
    {
        Settings settings = Settings.getInstance();

        public async Task loadTodaySpectra()
        {

            Dictionary<string, Measurement> library = new Dictionary<string, Measurement>();
            Dictionary<string, double[]> originalRaws = new Dictionary<string, double[]>();
            Dictionary<string, double[]> originalDarks = new Dictionary<string, double[]>();
            string path = PlatformUtil.getAutoSavePath(settings.highLevelAutoSave);
            await PlatformUtil.loadFiles(useAssets: false, path, library, originalRaws, originalDarks, true, null);

        }

    }
}
