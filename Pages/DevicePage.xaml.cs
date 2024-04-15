using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EnlightenMAUI.ViewModels;
using EnlightenMAUI.Models;

namespace EnlightenMAUI;

/// <summary>
/// Code-behind for DeviceView.xaml.
/// </summary>
/// <remarks>
/// This is the View for Device, which currently means the EEPROM.  
/// (FPGA Compilation Options, firmware revisions etc can be added later.)
///
/// Note this class owns and instantiates the actual ObservableCollection of 
/// ViewableSettings, injecting the (empty) collection into the EEPROM object
/// to populate.  
///
/// The XAML's listView is an implicit member of this class.  Here we set the
/// listView's .ItemSource to the ObservableCollection in the ctor.  Therefore,
/// when the listView tries to display items, it will draw them from the
/// viewableSettings collection.
///
/// However, the display of any one particular ViewableSetting is mediated
/// through the DeviceViewModel, which allows for any transforms
/// between the raw Model data (ViewableSetting) and the display version shown
/// on the View.  In this case, we're not applying any transforms or display
/// logic (ViewableSetting is internally stored as a string tuple, with no
/// transformations required), but this is the MVVM architecture.  
///
/// Note that the XAML directs the binding context to the DeviceViewModel,
/// but the XAML ListSettings can reference attributes directly within the ViewModel's
/// public ViewableSetting object.
/// </remarks>
/// <todo>
/// Make a new DeviceModel class, which "has" an EEPROM, but 
/// also FPGACompilationOptions, FirmwareRevisions (µC/FPGA ver), BatteryStatus
/// etc objects, and which populates the ObservableCollection from all of them.
/// </todo>
public partial class DevicePage : ContentPage
{
    ObservableCollection<ViewableSetting> viewableSettingsEEPROM;
    DeviceViewModel ssvm;
    Logger logger = Logger.getInstance();

    public DevicePage()
    {
        InitializeComponent();

        viewableSettingsEEPROM = new ObservableCollection<ViewableSetting>();

        EEPROM eeprom = EEPROM.getInstance();
        if (eeprom != null)
            eeprom.viewableSettings = viewableSettingsEEPROM;

        listViewEEPROM.ItemsSource = viewableSettingsEEPROM;

        ssvm = (DeviceViewModel)BindingContext;
    }

    /*
    private async void connectPage(object sender, EventArgs e)
    {
        await Navigation.PushAsync(new BluetoothView());
    }
    */

    // so the latest BLEDeviceInfo fields will be displayed
    // 
    // @todo why does this only fire on the SECOND (and following) times you
    //       visit this page?
    protected override void OnAppearing()
    {
        logger.debug("displaying DeviceView");
        base.OnAppearing();
        if (ssvm != null)
        {
            ssvm.updateBLEBtn();
            ssvm.refresh();
        }
    }
}
