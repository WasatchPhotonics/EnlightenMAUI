using System.ComponentModel;

namespace EnlightenMAUI.ViewModels;

internal class AboutViewModel: INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    Logger logger = Logger.getInstance();

    public AboutViewModel() { }

    public string version { get => $"EnlightenMAUI {AppInfo.Current.VersionString}"; }
}
