using System.ComponentModel;
using System.Runtime.CompilerServices;
using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels;

// This class provides "transformation logic" to render the Model of the
// EEPROM's ObservableList entries.  
//
// Not really; the ObservableList natively uses ViewableSetting objects, and 
// this class does nothing except provide a "straight-through" copy of each 
// ViewableSetting as it is rendered into a Cell of the ListView.  
// 
// This is the kind of verbose-yet-useless class that makes people hate MVVM.  
// IF there's a way to obviate it, let me know.
public class HardwareViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    Spectrometer spec = Spectrometer.getInstance();
    Logger logger = Logger.getInstance();
    
    public string bleBtnTxt
    {
        get => _bleBtnTxt;
        set
        {
            _bleBtnTxt = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(bleBtnTxt)));
        }
    }
    string _bleBtnTxt = "Connect";

    public HardwareViewModel()
    {
        // as Bluetooth device meta-characteristics are parsed during connection,
        // catch updates so this view is pre-populated 
        spec.bleDeviceInfo.PropertyChanged += bleDeviceUpdate;
        updateBLEBtn();
    }

    public void updateBLEBtn()
    {
        Console.WriteLine("Calling ble btn update");
        if (spec.bleDevice != null)
        {
            bleBtnTxt = "Disconnect";
        }
        else
        {
            bleBtnTxt = "Connect";
        }
    }

    // the BluetoothView code-behind has registered some metadata, so update 
    // our display properties
    void bleDeviceUpdate(object sender, PropertyChangedEventArgs e) =>
        refresh(e.PropertyName);

    public string title
    {
        get => "Spectrometer Settings";
    }

    ////////////////////////////////////////////////////////////////////////
    // BLE Device Info
    ////////////////////////////////////////////////////////////////////////

    public string deviceName       { get => spec.bleDeviceInfo.deviceName; }
    public string manufacturerName { get => spec.bleDeviceInfo.manufacturerName; }
    public string softwareRevision { get => spec.bleDeviceInfo.softwareRevision; }
    public string firmwareRevision { get => spec.bleDeviceInfo.firmwareRevision; }
    public string hardwareRevision { get => spec.bleDeviceInfo.hardwareRevision; }

    ////////////////////////////////////////////////////////////////////////
    // EEPROM
    ////////////////////////////////////////////////////////////////////////

    public ViewableSetting ViewableSetting
    {
        get { return _viewableSetting; }
        set { _viewableSetting = value; }
    }
    ViewableSetting _viewableSetting;

    ////////////////////////////////////////////////////////////////////////
    // Util
    ////////////////////////////////////////////////////////////////////////

    // so we can update these from the HardwarePage code-behind
    // on display, after changing spectrometers.
    public void refresh(string name = null)
    {
        logger.debug($"refreshing HardwareViewModel ({name})");

        if (name != null)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        else
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(deviceName)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(softwareRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(firmwareRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(hardwareRevision)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(manufacturerName)));
        }
    }
    protected void OnPropertyChanged([CallerMemberName] string caller = "")
    {
        logger.debug("SSVM: OnPropertyChanged");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(caller));
    }
}
