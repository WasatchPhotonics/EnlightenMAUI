using System.ComponentModel;
using System.Windows.Input;

using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels;

public class AboutViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    Settings settings = Settings.getInstance();

    Logger logger = Logger.getInstance();

    ////////////////////////////////////////////////////////////////////////
    // Lifecycle
    ////////////////////////////////////////////////////////////////////////

    public AboutViewModel()
    {
        OpenWebCommand = new Command(async () => await Browser.OpenAsync(settings.companyURL));
    }

    ////////////////////////////////////////////////////////////////////////
    // Public Properties
    ////////////////////////////////////////////////////////////////////////

    public string version
    {
        get => String.Format("EnlightenMAUI version 1.0.0");
    }

    public ICommand OpenWebCommand { get; }
}
