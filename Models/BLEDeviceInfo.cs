using System;
using System.ComponentModel;

namespace EnlightenMAUI.Models;

// This class provides a place to store string BLE characteristics found on
// the Device Info and Generic Access services.  We don't bother capturing
// all fields (currently ignoring Appearance and Preferred Connection Parameters)
public class BLEDeviceInfo : INotifyPropertyChanged
{
    // These get populated in BluetoothView code-behind from the Device Info
    // service (not Primary Service).
    public string deviceName { get; set; }
    public string manufacturerName { get; set; }
    public string softwareRevision { get; set; }
    public string firmwareRevision { get; set; }
    public string hardwareRevision { get; set; }

    Logger logger = Logger.getInstance();

    public event PropertyChangedEventHandler PropertyChanged;

    public bool add(string name, string value)
    {
        string property;

        switch(name)
        {
            case "Device Name": deviceName = value; property = nameof(deviceName); break;
            case "Manufacturer Name String": manufacturerName = value; property = nameof(manufacturerName); break;
            case "Software Revision String": softwareRevision = value; property = nameof(softwareRevision); break;
            case "Firmware Revision String": firmwareRevision = value; property = nameof(firmwareRevision); break;
            case "Hardware Revision String": hardwareRevision = value; property = nameof(hardwareRevision); break;
            default:
                logger.debug($"BLEDeviceInfo.add: unrecognized field: {name} = {value}");
                return false;
        }

        if (property != null)
        {
            logger.debug($"BLEDeviceInfo: {property} -> {value}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        return true;
    }

    public void dump()
    {
        logger.debug($"deviceName       = {deviceName}");
        logger.debug($"manufacturerName = {manufacturerName}");
        logger.debug($"softwareRevision = {softwareRevision}");
        logger.debug($"firmwareRevision = {firmwareRevision}");
        logger.debug($"hardwareRevision = {hardwareRevision}");
    }
}
