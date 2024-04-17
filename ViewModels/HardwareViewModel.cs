using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;

using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels;

public class HardwareViewModel : INotifyPropertyChanged
{
    Spectrometer spec = Spectrometer.getInstance();
    EEPROM eeprom = EEPROM.getInstance();

    // HardwarePage binds to data through its ViewModel (this file).
    // However, ViewModels don't guarantee lengthy persistence; they
    // may roll in and out of memory. Persistent data is meant to be
    // stored in MODELS, so here we're just providing a pass-through
    // to the name-value field pairs actually stored in Models.EEPROM.
    ObservableCollection<ViewableSetting> eepromFields;

    Logger logger = Logger.getInstance();

    public event PropertyChangedEventHandler PropertyChanged;
    
    public HardwareViewModel()
    {
        logger.debug("HVM.ctor: start");

        logger.debug("HVM.ctor: providing pass-through to EEPROM.viewableSettings");
        eepromFields = new ObservableCollection<ViewableSetting>(eeprom.viewableSettings);

        logger.debug("HVM.ctor: subscribing to updates of BLEDevice descriptors");
        spec.bleDeviceInfo.PropertyChanged += _bleDeviceUpdate;
    }

    // the BluetoothView code-behind has registered some metadata, so update 
    // our display properties
    void _bleDeviceUpdate(object sender, PropertyChangedEventArgs e) =>
        refresh(e.PropertyName);

    ////////////////////////////////////////////////////////////////////////
    // BLE Device Info
    ////////////////////////////////////////////////////////////////////////

    public string deviceName       { get => spec.bleDeviceInfo.deviceName; }
    public string manufacturerName { get => spec.bleDeviceInfo.manufacturerName; }
    public string softwareRevision { get => spec.bleDeviceInfo.softwareRevision; }
    public string firmwareRevision { get => spec.bleDeviceInfo.firmwareRevision; }
    public string hardwareRevision { get => spec.bleDeviceInfo.hardwareRevision; }

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
            logger.debug($"HVM.refresh: refreshing eepromFields");
            // eepromFields = new ObservableCollection<ViewableSetting>(eeprom.viewableSettings);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(eepromFields)));

            logger.debug($"HVM.refresh: refreshing BLE properties");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(deviceName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(softwareRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(firmwareRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(hardwareRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(manufacturerName)));
        }
        logger.debug($"HVM.refresh: done");
    }
    protected void OnPropertyChanged([CallerMemberName] string caller = "")
    {
        logger.debug($"HVM.OnPropertyChanged[{caller}]");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(caller));
    }
}
