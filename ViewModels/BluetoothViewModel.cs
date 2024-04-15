using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Linq;

using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
using Plugin.BLE;

using Microsoft.Maui.ApplicationModel; // Permissions

using EnlightenMAUI.Models;
using EnlightenMAUI.Common;

namespace EnlightenMAUI.ViewModels;

public class BluetoothViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public ObservableCollection<BLEDevice> bleDeviceList = new ObservableCollection<BLEDevice>();

    public Command scanCmd { get; }
    public Command connectCmd { get; }

    IBluetoothLE ble;
    IAdapter adapter;

    BLEDevice bleDevice;

    IService service;

    Dictionary<string, Guid> guidByName = new Dictionary<string, Guid>();
    Dictionary<Guid, string> nameByGuid = new Dictionary<Guid, string>();
    Dictionary<string, ICharacteristic> characteristicsByName = new Dictionary<string, ICharacteristic>();

    Guid primaryServiceId;

    Spectrometer spec = Spectrometer.getInstance();
    Logger logger = Logger.getInstance();

    // so the ViewModel can float-up messages to the View for display
    public delegate void UserNotification(string title, string message, string button);
    public event UserNotification notifyUser;

    public BluetoothViewModel()
    {
        Plugin.BLE.Abstractions.Trace.TraceImplementation = logger.ble;

        // this crashes on iOS if you don't follow add plist entries per
        // https://stackoverflow.com/a/59998233/11615696
        ble = CrossBluetoothLE.Current;
        adapter = CrossBluetoothLE.Current.Adapter;

        adapter.DeviceDiscovered += _bleAdapterDeviceDiscovered;
        adapter.ScanTimeoutElapsed += _bleAdapterStoppedScanning;

        primaryServiceId = _makeGuid("ff00");

        // characteristics
        logger.debug("BluetoothView: initializing characteristic GUIDs");

        // ENG-0120
        guidByName["integrationTimeMS"] = _makeGuid("ff01");
        guidByName["gainDb"]            = _makeGuid("ff02");
        guidByName["laserState"]        = _makeGuid("ff03");
        guidByName["acquireSpectrum"]   = _makeGuid("ff04");
        guidByName["spectrumRequest"]   = _makeGuid("ff05");
        guidByName["readSpectrum"]      = _makeGuid("ff06");
        guidByName["eepromCmd"]         = _makeGuid("ff07");
        guidByName["eepromData"]        = _makeGuid("ff08");
        guidByName["batteryStatus"]     = _makeGuid("ff09");
        guidByName["roi"]               = _makeGuid("ff0a");

        foreach (var pair in guidByName)
            nameByGuid[pair.Value] = pair.Key;

        scanCmd = new Command(() => { _ = doScanAsync(); });
        connectCmd = new Command(() => { _ = doConnectOrDisconnectAsync(); });
        Task<bool> task = doDisconnectAsync();
        task.Wait();

        spec.showConnectionProgress += showSpectrometerConnectionProgress;
}

    private void _bleAdapterStoppedScanning(object sender, EventArgs e)
    {
        logger.debug("BLE Adapter stopped scanning");
        updateScanButtonProperties();
    }

    ////////////////////////////////////////////////////////////////////////
    // Public Properties
    ////////////////////////////////////////////////////////////////////////

    public string title
    {
        get => "Bluetooth Pairing";
    }

    ////////////////////////////////////////////////////////////////////////
    // connectionProgress
    ////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Relay connection progress from the Spectrometer Model back to the 
    /// Bluetooth View.
    /// </summary>
    private void showSpectrometerConnectionProgress(double perc) =>
        connectionProgress = perc;

    public double connectionProgress
    {
        get => _connectionProgress;
        set 
        {
            _connectionProgress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectionProgress)));
        }
    }
    double _connectionProgress = 0;

    ////////////////////////////////////////////////////////////////////////
    // paired
    ////////////////////////////////////////////////////////////////////////

    public bool paired
    {
        get => _paired;
        set
        {
            _paired = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(buttonConnectText)));
        }
    }
    bool _paired = false;

    public bool bluetoothEnabled 
    { 
        get => _bluetoothEnabled;
        private set 
        {
            logger.info($"BVM.bluetoothEnabled: setting {value}");
            _bluetoothEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(bluetoothEnabled)));
        }
    }
    bool _bluetoothEnabled = Util.bluetoothEnabled(); // initialize from phone state at launch


    ////////////////////////////////////////////////////////////////////////
    // Reset (no longer a Command)
    ////////////////////////////////////////////////////////////////////////

    // ideally, we should probably add some kind of callback hook to an
    // Android "onBluetoothEnabled" event, inside the PlatformService, and
    // float that update back here somehow, but...this will work for now
    public async Task<bool> doResetAsync()
    {
        logger.debug("BVM.doResetAsync: attempting to disable Bluetooth");

        bleDeviceList.Clear();
        paired = false;
        buttonConnectEnabled = false;
        
        if (!Util.enableBluetooth(false))
            logger.error("BVM.doResetAsync: Unable to disable Bluetooth");

        bluetoothEnabled = false;

        logger.debug("BVM.doResetAsync: sleeping during Bluetooth restart");
        await Task.Delay(1000);

        logger.debug("BVM.doResetAsync: attempting to re-enable Bluetooth");
        var ok = Util.enableBluetooth(true);
        if (!ok)
        {
            logger.error("BVM.doResetAsync: Unable to re-enable Bluetooth");
            return false;
        }

        logger.debug("BVM.doResetAsync: inferring successful Bluetooth enable...sleeping");
        await Task.Delay(2000);

        logger.debug("BVM.doResetAsync: storing new Bluetooth enable status");
        bluetoothEnabled = true;

        logger.info("BVM.doResetAsync: Bluetooth successfully reset");
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // Scan Command
    ////////////////////////////////////////////////////////////////////////

    void updateScanButtonProperties()
    {
        logger.debug("updating scan button properties");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(scanButtonTextColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(scanButtonBackgroundColor)));
    }

    public string scanButtonBackgroundColor
    {
        get
        { 
            string color = "#ff0000";
            // color = ble.Adapter.IsScanning ? "#ba0a0a" : "#ccc";
            return color;
        }
    }

    public string scanButtonTextColor
    {
        get
        {
            string color = "#ffcc00";
            // color = ble.Adapter.IsScanning ? "#fff" : "#333";
            return color;
        }

    }

    /// <summary>
    /// Step 1: user clicked "Scan"
    /// </summary> 
    private async Task<bool> doScanAsync()
    {
        if (paired)
            await doDisconnectAsync();

        bleDeviceList.Clear();
        buttonConnectEnabled = false;
        
        try
        {
            // resolve Location permission
            var success = await _requestPermissionsAsync();
            if (!success)
            {
                logger.error("can't obtain Location permission");
                notifyUser("Permissions", "Can't obtain Location permission", "Ok");
                return false;
            }

            if (!ble.Adapter.IsScanning)
            {
                logger.debug("starting scan");
                _ = adapter.StartScanningForDevicesAsync();

                // Step 2: As each device is added to the list, the 
                //         adapter.DeviceDiscovered event will call 
                //         _bleAdapterDeviceDiscovered and add to listView.
            }
        }
        catch (Exception ex)
        {
            logger.error("caught exception during scan button event: {0}", ex.Message);
            notifyUser("EnlightenMobile", "Caught exception during BLE scan: " + ex.Message, "Ok");
        }

        updateScanButtonProperties();

        logger.debug("scan change complete");
        return true;
    }

    async Task<bool> _requestPermissionsAsync()
    {
        // Recent versions of Android require either coarse- or fine-grained 
        // Location access in order to perform a BLE scan.

        /*
        if (!await _requestPermissionAsync<LocationPermission>(Permission.Location, "Location",
            "Bluetooth requires access to the 'Location' service in order to function."))
            return false;

        if (!await _requestPermissionAsync<StoragePermission>(Permission.Storage, "Storage",
            "App needs to be able to save spectra to local filesystem."))
            return false;
        */
        return false;

        return true;
    }

    /*
     
    // @todo figure out why generic CheckPermissionStatusAsync doesn't compile
    async Task<bool> _requestPermissionAsync<T>(Permission perm, string name, string reason)
        where T : BasePermission
    {
        try
        {
            // do we already have permission?

            // WHY DOESN'T THIS WORK?!?
            //
            // I almost feel like I'm working from a cached version of the 
            // Plugin.Permissions package...like if I fully flushed and 
            // reinstalled that package, this would be okay.
            //
            // var status = await CrossPermissions.Current.CheckPermissionStatusAsync<T>(); 
            var status = await CrossPermissions.Current.CheckPermissionStatusAsync(perm);

            // if not, prompt the user to authorize it
            if (status != PermissionStatus.Granted)
            {
                if (await CrossPermissions.Current.ShouldShowRequestPermissionRationaleAsync(perm))
                    notifyUser("Permissions", 
                               "ENLIGHTEN Mobile requires Location permission to use Bluetooth: " + reason,
                               "Ok");

                var result = await CrossPermissions.Current.RequestPermissionsAsync(perm);
                status = result[perm];
            }

            // do we have it now?
            if (status == PermissionStatus.Granted)
            {
                logger.debug($"{name} permission granted");
                return true;
            }
            else if (status != PermissionStatus.Unknown)
            {
                return logger.error($"{name} permission denied");
            }
        }
        catch (Exception ex)
        {
            return logger.error($"Exception obtaining {name} permission: {ex}");
        }
        return false;
    }

    */

    ////////////////////////////////////////////////////////////////////////
    // Connect Button
    ////////////////////////////////////////////////////////////////////////

    public string buttonConnectText { get => paired ? "Disconnect" : "Connect"; }

    public bool buttonConnectEnabled 
    { 
        get => _buttonConnectEnabled;
        private set
        {
            _buttonConnectEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(buttonConnectEnabled)));
        }
    }
    bool _buttonConnectEnabled = false;

    ////////////////////////////////////////////////////////////////////////
    // Connect Command
    ////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Step 4: the user clicked the "Connect" / "Disconnect" button 
    /// </summary>
    private async Task<bool> doConnectOrDisconnectAsync()
    {
        if (paired)
        {
            await doDisconnectAsync();
        }
        else
        {
            paired = await doConnectAsync();
            if (paired)
            {
                await Shell.Current.GoToAsync("..");
            }
        }
        connectionProgress = 0;
        return true;
    }

    async Task<bool> doDisconnectAsync()
    {
        logger.debug("attempting to disconnect");
        spec.disconnect();

        if (bleDevice is null && spec.bleDevice is null)
        {
            logger.error("attempt to disconnect without bleDevice");
            paired = false;
            return false;
        }

        try { 
            if(!(spec.bleDevice is null)) {
                // See https://github.com/xabre/xamarin-bluetooth-le/issues/311
                // I'm getting an exception but looking at the rpi it works
                // Using await hangs infinitely here though
                var device = spec.bleDevice.device;
                spec.bleDevice = null;
                adapter.DisconnectDeviceAsync(device).Start();
            }
            else {
                Console.WriteLine("Not disconnect through spec");
                await adapter.DisconnectDeviceAsync(bleDevice.device);
            } 
        }
        catch {
            Console.WriteLine("Issue with disconnect call");
        }
       
        paired = false;
        return true;
    }

    async Task<bool> doConnectAsync()
    {
        logger.debug("attempting to connect");
        buttonConnectEnabled = false;

        connectionProgress = 0;
        if (bleDevice is null)
        {
            logger.error("must select a device before connecting");
            return false;
        }

        // recommended to help reduce GattCallback error 133
        if (ble.Adapter.IsScanning)
        {
            logger.debug("stopping scan");
            await adapter.StopScanningForDevicesAsync();
            updateScanButtonProperties();
        }

        logger.debug($"attempting connection to {bleDevice.name}");
        var success = false;
        try
        {
            // Step 5: actually try to connect
            await adapter.ConnectToDeviceAsync(bleDevice.device);

            // Step 5a: verify connection
            foreach (var d in adapter.ConnectedDevices)
            {
                if (d == bleDevice.device)
                {
                    success = true;
                    break;
                }
            }
        }
        catch (DeviceConnectionException ex)
        {
            logger.error("exception connecting to device ({0})", ex.Message);

            // kick off the reset WHILE the alert message is running
            _ = doResetAsync();

            notifyUser("Bluetooth", 
                       ex.Message + 
                           "\nAutomatically resetting Bluetooth adapter. " +
                           "Click \"Ok\" to re-scan and try again.",
                       "Ok");
            return false;
        }

        if (!success)
            return logger.error($"failed connection to {bleDevice.name}");

        logger.info($"successfully connected to {bleDevice.name}");
        connectionProgress = 0.05;

        // Step 6: connect to primary service
        await bleDevice.device.RequestMtuAsync(256);
        logger.debug($"connecting to primary service {primaryServiceId}");
        service = await bleDevice.device.GetServiceAsync(primaryServiceId);
        if (service is null)
        {
            return logger.error($"did not find primary service {primaryServiceId}");
        }

        logger.debug($"found primary service {service}");

        // Step 7: read characteristics
        logger.debug("reading characteristics of service {0} ({1})", service.Name, service.Id);
        characteristicsByName = new Dictionary<string, ICharacteristic>();
        var list = await service.GetCharacteristicsAsync();
        foreach (var c in list)
        {
            // match it with an "expected" UUID
            string name = null;
            foreach (var pair in guidByName)
            {
                if (pair.Value == new Guid(c.Uuid))
                {
                    name = pair.Key;
                    break;
                }
            }

            if (name is null)
            {
                logger.error($"ignoring unrecognized characteristic {c.Uuid}");
                continue;
            }

            // store it by friendly name
            characteristicsByName.Add(name, c);
        }

        logger.debug("Registered characteristics:");
        foreach (var pair in characteristicsByName)
        {
            var name = pair.Key;
            var c = pair.Value;

            logger.debug($"  {c.Uuid} {name}");

            if (c.CanUpdate)
                logger.debug("    (supports notifications)");

            // Step 7a: read characteristic descriptors
            // logger.debug($"    WriteType = {c.WriteType}");
            // var descriptors = await c.GetDescriptorsAsync();
            // foreach (var d in descriptors)
            //     logger.debug($"    descriptor {d.Name} = {d.Value}");
        }
        connectionProgress = 0.15;

        logger.debug("polling device for other services");
        var allServices = await bleDevice.device.GetServicesAsync();
        foreach (var thisService in allServices)
        {
            logger.debug($"examining service {thisService.Name} (ID {thisService.Id})");
            if (thisService.Id == primaryServiceId)
            {
                logger.debug("skipping primary service");
                continue;
            }

            var characteristics = await thisService.GetCharacteristicsAsync();
            foreach (var c in characteristics)
            {
                logger.debug($"reading {c.Name}");
                // This line is required because for some reason attempting to read
                // the Service Changed service cause the program to get blocked here
                if(c.Name != "Service Changed")
                {
                    var response = await c.ReadAsync();
                    if (response.data is null)
                    {
                        logger.error($"can't read {c.Uuid} ({c.Name})");
                    }
                    else
                    {
                        logger.hexdump(response.data, prefix: $"  {c.Uuid}: {c.Name} = ");
                        spec.bleDeviceInfo.add(c.Name, Util.toASCII(response.data));
                    }
                }
            }
        }

        // populate Spectrometer
        logger.debug("initializing spectrometer");
        await spec.initAsync(characteristicsByName);

        // start notifications
        foreach (var pair in characteristicsByName)
        {
            var name = pair.Key;
            var c = pair.Value;

            // disabled until I can troubleshoot with Nic
            if (false && c.CanUpdate && (name == "batteryStatus" || name == "laserState"))
            {
                logger.debug($"starting notification updates on {name}");
                c.ValueUpdated -= _characteristicUpdated;
                c.ValueUpdated += _characteristicUpdated;

                // don't see a need to await this?
                _ = c.StartUpdatesAsync();
            }
        }

        ////////////////////////////////////////////////////////////////////
        // all done
        ////////////////////////////////////////////////////////////////////

        logger.debug("btnConnect_clicked done");
        spec.bleDevice = bleDevice;

        // allow disconnect
        buttonConnectEnabled = true;

        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // Utility methods
    ////////////////////////////////////////////////////////////////////////

    // @todo test
    private void _characteristicUpdated(
            object sender, 
            CharacteristicUpdatedEventArgs characteristicUpdatedEventArgs)
    {
        var c = characteristicUpdatedEventArgs.Characteristic;

        // faster way to do this using nameByGuid?
        string name = null;
        foreach (var pair in characteristicsByName)
        {
            if (pair.Value.Uuid == c.Uuid)
            {
                name = pair.Key;
                break;
            }
        }

        if (name is null)
        {
            logger.error($"BVM._characteristicUpdated: Received notification from unknown characteristic ({c.Uuid})");
            return;
        }

        logger.info($"BVM._characteristicUpdated:: received BLE notification from characteristic {name}");

        if (name == "batteryStatus")
            spec.processBatteryNotification(c.Value);
        else if (name == "laserState")
            spec.processLaserStateNotificationAsync(c.Value);
        else
            logger.error($"no registered processor for {name} notifications");
    }

    // per EE, all Wasatch Photonics SiG Characteristic UUIDs follow this 
    // format
    Guid _makeGuid(string id)
    {
        const string prefix = "D1A7";
        const string suffix = "-AF78-4449-A34F-4DA1AFAF51BC";
        return new Guid(string.Format($"{prefix}{id}{suffix}"));
    }

    /// <summary>
    /// Step 2a: during a Scan, we've discovered a new BLE device, so add it 
    /// to the listView (by adding it to the deviceList serving as the listView's
    /// Model).
    /// </summary>
    void _bleAdapterDeviceDiscovered(object sender, DeviceEventArgs e)
    {
        var device = e.Device; // an IDevice

        // ignore anything without a name
        if (device.Name is null || device.Name.Length == 0)
            return;

        // ignore anything that doesn't have "WP" or "SiG" in the name
        var nameLC = device.Name.ToLower();
        if (!nameLC.Contains("wp") && !nameLC.Contains("sig"))
            return;

        BLEDevice bd = new BLEDevice(device);
        logger.debug($"discovered {bd.name} (RSSI {bd.rssi} Id {device.Id})");
        bleDeviceList.Add(bd);
    }

    ////////////////////////////////////////////////////////////////////////
    // View code-behind callbacks
    ////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Step 3b: the user clicked a BLE device in the list, raising an event
    /// in the View code-behind (step 3a), which sent the selection here.
    /// </summary>
    ///
    /// <remarks>
    /// The selection is passed as an uncast object because Views ideally
    /// shouldn't have contact or knowledge of Models, so we do the casting
    /// here.
    /// </remarks>
    public void selectBLEDevice(object obj)
    {
        var selectedBLEDevice = obj as BLEDevice;
        if (selectedBLEDevice is null)
        {
            bleDevice = null;
            service = null;
            buttonConnectEnabled = false;
            return;
        }

        bleDevice = selectedBLEDevice;
        logger.debug($"selected device {bleDevice.name}");

        // let devices know which is selected, so they can advertise an 
        // appropriate row color
        foreach (var dev in bleDeviceList)
            dev.selected = dev.device.Id == bleDevice.device.Id;

        buttonConnectEnabled = true;
    }
}
