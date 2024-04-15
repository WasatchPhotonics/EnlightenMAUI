using System;
using System.ComponentModel;
using System.Collections.Generic;
using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Contracts;

using EnlightenMAUI.Common;

namespace EnlightenMAUI.Models;

// Originally we didn't need this class, and just populated the BluetoothView's
// ListView with Plugin.BLE.Abstractions.IDevice objects directly.  However,
// we found a need to encapsulate additional state in a cross-platform object.
public class BLEDevice : INotifyPropertyChanged
{
    public IDevice device;
    
    Logger logger = Logger.getInstance();

    public event PropertyChangedEventHandler PropertyChanged;

    ////////////////////////////////////////////////////////////////////////
    // Lifecycle
    ////////////////////////////////////////////////////////////////////////

    public BLEDevice(IDevice device)
    {
        this.device = device;

        logger.debug("BLEDevice advertisements:");
        foreach (var rec in device.AdvertisementRecords)
        {                
            logger.hexdump(rec.Data, prefix: $"  {rec.Type}: ");

            // On iOS, this returns the 16-bit Primary Service UUID (0xff00),
            //         which is useless.
            // On Android, this returns the 128-bit Primary Service UUID
            //         d1a7ff00-af78-4449-a34f-4da1afaf51bc, equally useless.
            //
            // I do wonder if this could be fixed in firmware?
            //
            // if (rec.Type == AdvertisementRecordType.UuidsComplete128Bit)
            //    uuid = Util.formatUUID(rec.Data);
        }
    }

    public double rssi
    {
        get => device.Rssi;
    }

    public string name
    {
        get => device.Name;
    }

    // This is the little hex string displayed under the device name in the
    // BluetoothView allowing you to distinguish between multiple WP-SiG
    // devices in Bluetooth range.  Bizarrely, the value won't match in
    // content or format between iOS and Android :-(
    //
    // On iOS, this gives a 128-bit device UUID, e.g. fd7b9fca-3615-da68-03f2-6557e29e2be4
    // On Android, this gives a less-impressive 48-bit device UUID, e.g. f1e9a7ce0ac8
    public string id
    {
        get => device.Id.ToString();
    }

    ////////////////////////////////////////////////////////////////////////
    // GUI selection state
    ////////////////////////////////////////////////////////////////////////

    // would be less egregious to return a named color, where the XAML 
    // defined the names
    public string backgroundColor
    {
        get => selected ? "#555" : "#444";
    }

    public bool selected
    {
        get => _selected;
        set
        {
            if (_selected != value)
            { 
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(backgroundColor)));
            }
        }
    }
    bool _selected;
}
