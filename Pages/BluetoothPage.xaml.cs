using EnlightenMAUI.ViewModels;

namespace EnlightenMAUI;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class BluetoothPage : ContentPage
{
    BluetoothViewModel bvm;
    Logger logger = Logger.getInstance();

    public BluetoothPage()
    {
        InitializeComponent();

        bvm = (BluetoothViewModel)BindingContext;

        Appearing += onAppearingAsync;
    }

    // This currently comes up on the SECOND visit to the page, not the first,
    // but otherwise works. Use different Event? Add to About? Call from ctor and
    // skip the prompt?
    async void onAppearingAsync(object sender, System.EventArgs e)
    {
        logger.debug("BluetoothPage.onAppearingAsync: start");
        if (!bvm.bluetoothEnabled)
        {
            logger.debug("BluetoothPage.onAppearingAsync: prompting");
            var confirmed = await DisplayAlert(
                "Bluetooth",
                "ENLIGHTEN requires Bluetooth to communicate with the spectrometer. Turn it on automatically?",
                "Yes",
                "No");
            if (confirmed)
            {
                logger.debug("BluetoothPage.onAppearingAsync: calling doResetAsync");
                _ = bvm.doResetAsync();
            }
            else
                logger.debug("BluetoothPage.onAppearingAsync: not confirmed");
        }
        logger.debug("BluetoothPage.onAppearingAsync: done");
    }
}
