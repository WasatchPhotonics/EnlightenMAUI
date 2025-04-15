using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels;

public class HardwareViewModel : INotifyPropertyChanged
{
    Spectrometer spec = BluetoothSpectrometer.getInstance();
    EEPROM eeprom = EEPROM.getInstance();

    // HardwarePage binds to data through its ViewModel (this file).
    // However, ViewModels don't guarantee lengthy persistence; they
    // may roll in and out of memory. Persistent data is meant to be
    // stored in MODELS, so here we're providing a temporary copy of
    // the the name-value field pairs actually stored in Models.EEPROM.
    //
    // But we do instantiate the collection immediately, and never 
    // dispose or reassign the reference, because the Page seems to
    // bind to the object we have at construction.
    public ObservableCollection<ViewableSetting> eepromFields { get; set; }

    Logger logger = Logger.getInstance();

    public event PropertyChangedEventHandler PropertyChanged;

    public HardwareViewModel()
    {
        logger.debug("HVM.ctor: start");

        eepromFields = new ObservableCollection<ViewableSetting>(eeprom.viewableSettings);

        if (spec == null || !spec.paired)
            spec = API6BLESpectrometer.getInstance();
        if (spec == null || !spec.paired)
            spec = USBSpectrometer.getInstance();

        if (spec is BluetoothSpectrometer)
        {
            logger.debug("HVM.ctor: subscribing to updates of BLEDevice descriptors");
            (spec as BluetoothSpectrometer).bleDeviceInfo.PropertyChanged += _bleDeviceUpdate;
        }
        else if (spec is API6BLESpectrometer)
        {
            logger.debug("HVM.ctor: subscribing to updates of BLEDevice descriptors");
            (spec as API6BLESpectrometer).bleDeviceInfo.PropertyChanged += _bleDeviceUpdate;
        }

        Spectrometer.NewConnection += handleNewSpectrometer;
    }


    private void refreshEEPROMFields()
    {
        eepromFields = new ObservableCollection<ViewableSetting>(eeprom.viewableSettings);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(eepromFields)));
    }

    // the BluetoothView code-behind has registered some metadata, so update 
    // our display properties
    void _bleDeviceUpdate(object sender, PropertyChangedEventArgs e) =>
        refresh(e.PropertyName);

    ////////////////////////////////////////////////////////////////////////
    // Headline properties
    ////////////////////////////////////////////////////////////////////////

    public string serialNumber { get => eeprom?.serialNumber; }
    public string fullModelName { get => spec?.fullModelName; }

    ////////////////////////////////////////////////////////////////////////
    // BLE Device Info
    ////////////////////////////////////////////////////////////////////////

    public string deviceName       { get => spec?.bleDeviceInfo.deviceName; }
    public string manufacturerName { get => spec?.bleDeviceInfo.manufacturerName; }
    public string softwareRevision { get => spec?.bleDeviceInfo.softwareRevision; }
    public string firmwareRevision { get => spec?.bleDeviceInfo.firmwareRevision; }
    public string hardwareRevision { get => spec?.bleDeviceInfo.hardwareRevision; }


    void handleNewSpectrometer(object sender, Spectrometer e)
    {
        spec = e;
        refresh();
    }

    ////////////////////////////////////////////////////////////////////////
    // Util
    ////////////////////////////////////////////////////////////////////////

    // so we can update these from the HardwarePage code-behind
    // on display, after changing spectrometers.
    public void refresh(string name = null)
    {
        if (name != null)
        {
            logger.debug($"HVM.refresh: refreshing {name}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
        else
        {
            logger.debug($"HVM.refresh: refreshing BLE properties");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(deviceName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(softwareRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(firmwareRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(hardwareRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(manufacturerName)));

            refreshEEPROMFields();
        }
        logger.debug($"HVM.refresh: done");
    }
    protected void OnPropertyChanged([CallerMemberName] string caller = "")
    {
        logger.debug($"HVM.OnPropertyChanged[{caller}]");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(caller));
    }
}
