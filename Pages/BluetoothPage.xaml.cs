using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EnlightenMAUI.ViewModels;
using EnlightenMAUI.Models;

namespace EnlightenMAUI;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class BluetoothPage : ContentPage
{
    BluetoothViewModel bvm;

    Logger logger = Logger.getInstance();

    public BluetoothPage()
    {
        InitializeComponent();

        logger.debug("BluetoothPage.ctor: taking handle to BVM");
        bvm = (BluetoothViewModel)BindingContext;

        logger.debug("BluetoothPage.ctor: doing nothing with ListView");
        // the View's on-screen ListView displays objects from the 
        // ViewModel's List
        // listView.ItemsSource = bvm.bleDeviceList;

        logger.debug("BluetoothPage.ctor: doing nothing with Notifications");
        // render ViewModel notifications to the View
        // bvm.notifyUser += notifyUserAsync;

        logger.debug("BluetoothPage.ctor: not adding Appearing handler");
        // attempt to check for Bluetooth being active at first display
        // Appearing += onAppearingAsync;
    }

    // Step 3a: the user has explicitly selected a device from the listView,
    // so notify the ViewModel
    void listView_ItemSelected(object sender, SelectedItemChangedEventArgs e)
    {
        logger.debug("BluetoothPage.listView_ItemSelected: start");
        bvm?.selectBLEDevice(listView.SelectedItem);
    }

    // the BluetoothViewModel has raised a "notifyUser" event, so display it
    async void notifyUserAsync(string title, string message, string button)
    {
        logger.debug($"BluetoothPage.notifyUserAsync: start");
        logger.debug($"BluetoothPage.notifyUserAsync: title {title}, message {message}, button {button}");
        await DisplayAlert(title, message, button);
    }

    // this seems to come up as soon as the app is launched, NOT when the
    // tab page is changed...probably good enough for now?  Can always call
    // it from PageNav if needed.
    async void onAppearingAsync(object sender, System.EventArgs e)
    {
        logger.debug("BluetoothPage.onAppearingAsync: start");
        if (bvm != null && !bvm.bluetoothEnabled)
        {
            logger.debug("BluetoothPage.onAppearingAsync: BLE disabled, so prompt to enable");
            var confirmed = await DisplayAlert(
                "Bluetooth", 
                "ENLIGHTEN requires Bluetooth to communicate with the spectrometer. Turn it on automatically?",
                "Yes", 
                "No");

            if (confirmed)
            {
                logger.debug("BluetoothPage.onAppearingAsync: user requested to enable Bluetooth");
                _ = bvm.doResetAsync();
            }
        }
    }
}
