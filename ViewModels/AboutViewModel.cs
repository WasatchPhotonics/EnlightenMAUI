using System.ComponentModel;
using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels;

internal class AboutViewModel: INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    Logger logger = Logger.getInstance();
    Settings settings = Settings.getInstance();

    public AboutViewModel() 
    {
        logger.info("Hello: {0}", Logger.HELLO);
    }

    // AppInfo.Current.VersionString and VersionTracking.currentVersion seem interchangeable
    public string appVersion { get => $"EnlightenMobile {VersionTracking.CurrentVersion}"; }

    public string hostDescription { get => settings.hostDescriptionWrapped; }
}
