using EnlightenMAUI.ViewModels;

namespace EnlightenMAUI;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class HardwarePage : ContentPage
{
    HardwareViewModel hvm;
    Logger logger = Logger.getInstance();

    public HardwarePage()
    {
        InitializeComponent();
        logger.debug("HardwarePage.ctor: start");
        hvm = (HardwareViewModel)BindingContext;
    }

    // so the latest BLEDeviceInfo fields will be displayed
    protected override void OnAppearing()
    {
        base.OnAppearing();
        logger.debug("HardwarePage.OnAppearing: start");
        if (hvm != null)
        {
            logger.debug("HardwarePage.OnAppearing: calling HardwareViewModel.refresh");
            hvm.refresh();
        }
        logger.debug("HardwarePage.OnAppearing: done");
    }
}
