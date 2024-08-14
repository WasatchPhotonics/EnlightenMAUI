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
        bvm.notifyUser += notifyUserAsync;
    }

    // Step 3a: the user has explicitly selected a device from the listView,
    // so notify the ViewModel
    void listView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
    {
        logger.debug("BluetoothPage.listView_ItemSelected[Step 3a]: passing selected item to BluetoothViewModel");
        bvm.selectBLEDevice(listView.SelectedItem);
    }
    void listViewUSB_ItemSelected(object sender, SelectedItemChangedEventArgs e)
    {
        logger.debug("BluetoothPage.listView_ItemSelected[Step 3a]: passing selected item to BluetoothViewModel");
        bvm.selectUSBDevice(listView.SelectedItem);
    }

    // the BluetoothViewModel has raised a "notifyUser" event, so display it
    async void notifyUserAsync(string title, string message, string button) =>
        await DisplayAlert(title, message, button);

    // This currently comes up on the SECOND visit to the page, not the first,
    // but otherwise works. Use different Event? Add to About? Call from ctor and
    // skip the prompt?
    async void onAppearingAsync(object sender, EventArgs e)
    {
        base.OnAppearing();
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
            {
                logger.debug("BluetoothPage.onAppearingAsync: not confirmed");
            }
        }
        logger.debug("BluetoothPage.onAppearingAsync: done");
    }
}
