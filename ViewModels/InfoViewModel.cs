using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels;
internal class InfoViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    Settings settings = Settings.getInstance();

    Logger logger = Logger.getInstance();

    ////////////////////////////////////////////////////////////////////////
    // Lifecycle
    ////////////////////////////////////////////////////////////////////////

    public InfoViewModel()
    {
        OpenWebCommand = new Command(async () => await Browser.OpenAsync(settings.companyURL));
    }

    ////////////////////////////////////////////////////////////////////////
    // Public Properties
    ////////////////////////////////////////////////////////////////////////

    public string version
    {
        get => String.Format("EnlightenMAUI version 0.0.4");
    }

    public ICommand OpenWebCommand { get; }
}
