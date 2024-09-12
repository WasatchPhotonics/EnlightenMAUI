using System.Collections.ObjectModel;
using System.ComponentModel;

using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using Plugin.BLE.Abstractions.Exceptions;
using Plugin.BLE;

using EnlightenMAUI.Models;
using EnlightenMAUI.Common;

//using Android.Hardware.Usb;
//using Android.Content;
using LibUsbDotNet.Main;
using Android.Content;
using Android.Hardware.Usb;
using Android.App;
using Android.Nfc;
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui;
using Xamarin.Google.Crypto.Tink.Subtle;

namespace EnlightenMAUI.ViewModels;

public class BluetoothViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    
    List<BLEDevice> source = new List<BLEDevice>();
    public ObservableCollection<BLEDevice> bleDeviceList { get; private set; }
    public ObservableCollection<USBViewDevice> usbDeviceList { get; private set; }
    BLEDevice bleDevice;
    Android.Hardware.Usb.UsbDevice acc;

    public Command scanCmd { get; }
    public Command scanUSBCmd { get; }
    public Command connectCmd { get; }
    public Command connectUSBCmd { get; }

    IBluetoothLE ble;
    IAdapter adapter;
    private static string ACTION_USB_PERMISSION = "com.android.example.USB_PERMISSION";
    
    /*private BroadcastReceiver mUsbReceiver = new BroadcastReceiver()
    {

    public void onReceive(Context context, Intent intent)
    {
        string action = intent.Action;
        if (ACTION_USB_PERMISSION == action)
        {
            UsbDevice device = (UsbDevice)intent.GetParcelableExtra(UsbManager.ExtraDevice);

            if (intent.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false))
            {
                if (device != null)
                {
                    //call method to set up device communication
                }
            }
            else
            {
                Log.d(TAG, "permission denied for device " + device);
            }
        }
    }
};*/
        
    

    PendingIntent usbIntent;

    IService service;

    Dictionary<string, Guid> guidByName = new Dictionary<string, Guid>();
    Dictionary<Guid, string> nameByGuid = new Dictionary<Guid, string>();
    Dictionary<string, ICharacteristic> characteristicsByName = new Dictionary<string, ICharacteristic>();

    Guid primaryServiceId;

    Spectrometer spec;
    Logger logger = Logger.getInstance();

    // so the ViewModel can float-up messages to the View for display
    public delegate void UserNotification(string title, string message, string button);
    public event UserNotification notifyUser;

    public BluetoothViewModel()
    {
        logger.debug("BVM.ctor: start");

        logger.debug("BVM.ctor: configuring BLE logger");
        Plugin.BLE.Abstractions.Trace.TraceImplementation = logger.ble;

        logger.debug("BVM.ctor: instantiating bleDeviceList");
        bleDeviceList = new ObservableCollection<BLEDevice>(source);
        usbDeviceList = new ObservableCollection<USBViewDevice>();

        // this crashed Xamarin on iOS if you don't follow add plist entries per
        // https://stackoverflow.com/a/59998233/11615696
        logger.debug("BVM.ctor: grabbing ble handle");
        ble = CrossBluetoothLE.Current;
        logger.debug("BVM.ctor: grabbing adapter handle");
        adapter = CrossBluetoothLE.Current.Adapter;

        logger.debug("BVM.ctor: adding DeviceDiscovered handler");
        adapter.DeviceDiscovered += _bleAdapterDeviceDiscovered;
        logger.debug("BVM.ctor: adding ScanTimeoutElapsed handler");
        adapter.ScanTimeoutElapsed += _bleAdapterStoppedScanning;

        logger.debug("BVM.ctor: creating primaryServiceId");
        primaryServiceId = _makeGuid("ff00");

        // characteristics
        logger.debug("BluetoothView: initializing characteristic GUIDs");

        // @see ENG-0120
        guidByName["integrationTimeMS"] = _makeGuid("ff01");
        guidByName["gainDb"]            = _makeGuid("ff02");
        guidByName["laserState"]        = _makeGuid("ff03");
        guidByName["acquireSpectrum"]   = _makeGuid("ff04");
        guidByName["spectrumRequest"]   = _makeGuid("ff05");
        guidByName["readSpectrum"]      = _makeGuid("ff06");
        guidByName["eepromCmd"]         = _makeGuid("ff07");
        guidByName["eepromData"]        = _makeGuid("ff08");
        guidByName["batteryStatus"]     = _makeGuid("ff09");
        guidByName["generic"]           = _makeGuid("ff0a"); // was ROI

        foreach (var pair in guidByName)
            nameByGuid[pair.Value] = pair.Key;

        scanCmd = new Command(() => { _ = doScanAsync(); });
        scanUSBCmd = new Command(() => { _ = doUSBScanAsync(); });
        connectCmd = new Command(() => { _ = doConnectOrDisconnectAsync(); });
        connectUSBCmd = new Command(() => { _ = doConnectOrDisconnectUSBAsync(); });

        logger.debug("BVM.ctor: initial disconnection");
        Task<bool> task = doDisconnectAsync(true);
        task.Wait();
        logger.debug("BVM.ctor: back from initial disconnection");

        // as the Spectrometer connection proceeds, allow it to flow updates
        // through this ViewModel for visualization to the user
        spec.showConnectionProgress += showSpectrometerConnectionProgress;

        logger.debug("BVM.ctor: done");
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
    // Bluetooth Enabled
    ////////////////////////////////////////////////////////////////////////

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
    
    public bool usbEnabled 
    { 
        get => _usbEnabled;
        private set 
        {
            logger.info($"BVM.bluetoothEnabled: setting {value}");
            _usbEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(usbEnabled)));
        }
    }
    bool _usbEnabled = true; //Util.bluetoothEnabled(); // initialize from phone state at launch

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
        BLEDevice.paired = false;
        buttonConnectEnabled = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));
        
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));
    }

    public string scanButtonBackgroundColor
    {
        get
        { 
            // string color = "#ff0000";
            string color = ble.Adapter.IsScanning ? "#ba0a0a" : "#515151";
            return color;
        }
    }

    public string scanButtonTextColor
    {
        get
        {
            string color = ble.Adapter.IsScanning ? "#eee" : "#ccc";
            return color;
        }
    }

    public string connectButtonBackgroundColor
    {
        get
        {
            if (BLEDevice.paired)
                return "#ba0a0a";
            // else if (ble.Adapter.IsScanning)
            //     return "#eee";
            // else if (!buttonConnectEnabled)
            //     return "#ccc";
            else
                return "#515151";
        }
    }

    /// <summary>
    /// Step 1: user clicked "Scan"
    /// </summary> 
    /// 

    public bool usingBluetooth
    {
        get { return _useBluetooth; }
        set
        {
            if (value == _useBluetooth)
                return;
            _useBluetooth = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(useBluetooth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(usingBluetooth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(usingUSB)));
        }
    }
    public bool usingUSB
    {
        get { return !_useBluetooth; }
        set
        {
            if (value != _useBluetooth)
                return;
            _useBluetooth = !value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(useBluetooth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(usingBluetooth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(usingUSB)));
        }
    }

    public bool useBluetooth
    {
        get { return _useBluetooth; }
        set 
        { 
            _useBluetooth = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(useBluetooth)));
        }

    }
    bool _useBluetooth = true;

    private async Task<bool> doScanAsync()
    {
        if (useBluetooth)
            return await doBluetoothScanAsync();
        else
            return await doUSBScanAsync();
    }

    private async Task<bool> doBluetoothScanAsync()
    {
        logger.debug("BVM.doScanAsync[Step 1]: start");

        if (BLEDevice.paired)
        {
            logger.debug("BVM.doScanAsync: paired so disconnecting");
            await doDisconnectAsync(true);
            logger.debug("BVM.doScanAsync: done disconnecting"); 
        }

        logger.debug("BVM.doScanAsync: clearing list"); 
        bleDeviceList.Clear();
        buttonConnectEnabled = false;
        
        try
        {
            logger.debug("BVM.doScanAsync: requesting permissions");
            var success = await _requestPermissionsAsync();
            if (!success)
            {
                logger.error("BVM.doScanAsync: can't obtain Location permission");
                notifyUser("Permissions", "Can't obtain Location permission", "Ok");
                return false;
            }
            logger.debug("BVM.doScanAsync: all permissions granted");

            if (!ble.Adapter.IsScanning)
            {
                logger.debug("BVM.doScanAsync[Step 2]: starting scan");
                _ = adapter.StartScanningForDevicesAsync();

                // Step 2: As each device is added to the list, the 
                //         adapter.DeviceDiscovered event will call 
                //         _bleAdapterDeviceDiscovered and add to listView.
            }
        }
        catch (Exception ex)
        {
            logger.error($"caught exception during scan button event: {ex.Message}");
            notifyUser("EnlightenMAUI", "Caught exception during BLE scan: " + ex.Message, "Ok");
        }

        updateScanButtonProperties();

        logger.debug("BVM.doScanAsync: scan change complete");
        return true;
    }
    
    private async Task<bool> doUSBScanAsync()
    {
        /*
        //UsbManager manager = ContextWrapper.
        Context con = Android.App.Application.Context;
        UsbManager manager = (UsbManager)con.GetSystemService(Context.UsbService);

        foreach (UsbDevice acc in manager.DeviceList.Values)
        {
            //acc.
            acc.

        }
        */
        logger.info("Looking for usb devices via Android services");
        try
        {
            Context con = Android.App.Application.Context;
            UsbManager manager = (UsbManager)con.GetSystemService(Context.UsbService);

            var features = con.PackageManager.GetSystemAvailableFeatures();
            foreach ( var feature in features ) 
            {
                logger.info("{0} feature available", feature.Name);
            }

            if (manager.DeviceList.Count == 0)
            {
                logger.info("No USB devices found");
            }

            foreach (Android.Hardware.Usb.UsbDevice acc in manager.DeviceList.Values)
            {
                this.acc = acc;

                String desc = String.Format("Vid:0x{0:x4} Pid:0x{1:x4} (rev:{2}) - {3}",
                    acc.VendorId,
                    acc.ProductId,
                    acc.Version,
                    acc.DeviceName);

                logger.info("found usb device {0}", desc);

                if (acc.VendorId == 0x24aa)
                {
                    USBViewDevice uvd = new USBViewDevice(acc.DeviceName, acc.VendorId.ToString("x4"), acc.ProductId.ToString("x4"));
                    usbDeviceList.Add(uvd);

                    usbIntent = PendingIntent.GetBroadcast(con, 0, new Intent(ACTION_USB_PERMISSION), PendingIntentFlags.Immutable);
                     
                    //LibUsbDotNet.UsbDevice usbDevice = LibUsbDotNet.UsbDevice.OpenUsbDevice(d => d.Pid == acc.ProductId);
                    manager.RequestPermission(acc, usbIntent);
                    
                }

            }
        }
        catch (Exception ex)
        {
            logger.info("USB grab failed with error {0}", ex.Message);
        }

        /*
        try
        {
            UsbRegDeviceList deviceRegistries = UsbDevice.AllDevices;
            if (deviceRegistries == null)
            {
                logger.info("No USB devices found");
            }
            else if (deviceRegistries.Count == 0)
            {
                logger.info("No USB devices found");
            }

            else
            {
                foreach (UsbRegistry usbRegistry in deviceRegistries)
                {
                    String desc = String.Format("Vid:0x{0:x4} Pid:0x{1:x4} (rev:{2}) - {3}",
                        usbRegistry.Vid,
                        usbRegistry.Pid,
                        (ushort)usbRegistry.Rev,
                        usbRegistry[SPDRP.DeviceDesc]);

                    logger.info("attempting to open {0}", desc);

                    USBViewDevice uvd = new USBViewDevice("Test", usbRegistry.Vid.ToString("x4"), usbRegistry.Pid.ToString("x4"));
                    usbDeviceList.Add(uvd);
                }
            }
        }
        catch (Exception ex)
        {
            logger.info("USB grab failed with error {0}", ex.Message);
        }
        */

        return true;
        
    }

    async Task<bool> _requestPermissionsAsync()
    {
        ////////////////////////////////////////////////////////////////////////
        // Mandatory Permissions
        ////////////////////////////////////////////////////////////////////////

        // Historically, Location permission is required to use BLE.
        PermissionStatus status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            logger.error("ENLIGHTEN requires LocationWhenInUse permission to use Bluetooth.");
            return false;
        }

        logger.debug("Verifying Bluetooth permissions..");
        var permissionResult = await Permissions.CheckStatusAsync<Permissions.Bluetooth>();
        if (permissionResult != PermissionStatus.Granted)
        {
            permissionResult = await Permissions.RequestAsync<Permissions.Bluetooth>();
        }
        logger.debug($"Result of requesting Bluetooth permissions: '{permissionResult}'");
        if (permissionResult != PermissionStatus.Granted)
        {
            logger.debug("Permissions not available, direct user to settings screen.");
            AppInfo.ShowSettingsUI();
            return false;
        }

        ////////////////////////////////////////////////////////////////////////
        // Optional Permissions
        ////////////////////////////////////////////////////////////////////////

        status = await Permissions.RequestAsync<Permissions.StorageWrite>();
        if (status != PermissionStatus.Granted)
            logger.debug("ENLIGHTEN requires StorageWrite permission to save spectra.");

        status = await Permissions.RequestAsync<Permissions.StorageRead>();
        if (status != PermissionStatus.Granted)
            logger.debug("ENLIGHTEN requires StorageRead permission to load spectra.");

        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // Connect Button
    ////////////////////////////////////////////////////////////////////////

    public string buttonConnectText { get => BLEDevice.paired ? "Disconnect" : "Connect"; }

    public bool buttonConnectEnabled 
    { 
        get => _buttonConnectEnabled;
        private set
        {
            logger.debug($"BVM: buttonConnectEnabled -> {value}");
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
    /// 
    private async Task<bool> doConnectOrDisconnectAsync()
    {
        if (useBluetooth)
            return await doConnectOrDisconnectBluetoothAsync();
        else
            return await doConnectOrDisconnectUSBAsync();
    }

    private async Task<bool> doConnectOrDisconnectBluetoothAsync()
    {
        logger.debug("BVM.doConnectOrDisconnect[Step 4]: start");
        if (BLEDevice.paired)
        {
            logger.debug("BVM.doConnectOrDisconnect: disconnecting");
            await doDisconnectAsync(true);
        }
        else
        {
            logger.debug("BVM.doConnectOrDisconnect: connecting");
            await doConnectAsync(true);
            if (BLEDevice.paired)
            {
                logger.debug("BVM.doConnectOrDisconnect: calling Shell.Current.GoToAsync");
                await Shell.Current.GoToAsync("//ScopePage");
            }
        }
        logger.debug("BVM.doConnectOrDisconnect: done");
        connectionProgress = 0;
        return true;
    }
    
    private async Task<bool> doConnectOrDisconnectUSBAsync()
    {
        if (spec == null || (spec is BluetoothSpectrometer))
        {
            try
            {
                Context con = Android.App.Application.Context;
                UsbManager manager = (UsbManager)con.GetSystemService(Context.UsbService);
                int interfaces = acc.InterfaceCount;

                logger.info("usb device has {0} interfaces", interfaces);
                for (int i = 0; i < interfaces; i++)
                {
                    logger.info("interface {0} has {1} endpoints", i, acc.GetInterface(i).EndpointCount);
                }

                UsbDeviceConnection udc = manager.OpenDevice(acc);
                logger.info("device has {0} configurations", acc.ConfigurationCount);
                if (udc != null)
                {
                    logger.info("successfully opened device");
                    USBSpectrometer usbSpectrometer = new USBSpectrometer(udc, acc);
                    spec = usbSpectrometer;
                    bool ok = await (spec as USBSpectrometer).initAsync();
                    USBSpectrometer.setInstance(usbSpectrometer);
                    USBViewDevice.paired = true;
                    return ok;
                }
                else
                {
                    logger.info("failed to open device");
                    return false;
                }
            }
            catch (Exception ex)
            {
                logger.error("USB connect failed with exception {0}", ex.Message);
                return false;
            }

        }
        else
        {
            logger.info("already initialized as usb spec, reconnecting");

            try
            {
                if ((spec as USBSpectrometer).paired)
                    spec.disconnect();
                else
                {
                    (spec as USBSpectrometer).connect();
                    return await (spec as USBSpectrometer).initAsync();
                }
                return true;
            }
            catch (Exception ex)
            {
                logger.error("USB toggle failed with exception {0}", ex.Message);
                return false;
            }
        }
    }

    async Task<bool> doDisconnectAsync(bool isBluetooth)
    {
        logger.debug("BVM.doDisconnectAsync: attempting to disconnect");
        if (spec == null)
            spec = BluetoothSpectrometer.getInstance();

        spec.disconnect();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));

        if (isBluetooth)
        {
            if (bleDevice is null && (spec as BluetoothSpectrometer).bleDevice is null)
            {
                logger.error("BVM.doDisconnectAsync: attempt to disconnect without bleDevice");
                BLEDevice.paired = false;
                return false;
            }

            try
            {
                if (!((spec as BluetoothSpectrometer).bleDevice is null))
                {
                    // See https://github.com/xabre/xamarin-bluetooth-le/issues/311
                    // I'm getting an exception but looking at the rpi it works
                    // Using await hangs infinitely here though
                    logger.debug("BVM.doDisconnectAsync: attempting to disconnect spec.bleDevice.device");
                    var device = (spec as BluetoothSpectrometer).bleDevice.device;
                    (spec as BluetoothSpectrometer).bleDevice = null;
                    adapter.DisconnectDeviceAsync(device).Start();
                }
                else
                {
                    logger.debug("BVM.doDisconnectAsync: attempting to disconnect bleDevice.device");
                    await adapter.DisconnectDeviceAsync(bleDevice.device);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"BVM.doDisconnectAsync: caught exception while disconnecting: {ex.Message}");
            }

            logger.debug("BVM.doDisconnectAsync: done");
            BLEDevice.paired = false;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(buttonConnectText)));
        return true;
    }

    /*
     *  I personally think this should probably live in a model class since nothing going on here needs to be displayed
     */
    async Task<bool> doConnectAsync(bool isBluetooth)
    {
        logger.debug("BVM.doConnectAsync: start");
        buttonConnectEnabled = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));

        connectionProgress = 0;

        if (isBluetooth)
        {
            spec = BluetoothSpectrometer.getInstance();

            if (bleDevice is null)
            {
                logger.error("BVM.doConnectAsync: must select a device before connecting");
                return false;
            }

            // recommended to help reduce GattCallback error 133
            if (ble.Adapter.IsScanning)
            {
                logger.debug("BVM.doConnectAsync: stopping scan");
                await adapter.StopScanningForDevicesAsync();
                updateScanButtonProperties();
                logger.debug("BVM.doConnectAsync: done stopping scan");
            }

            logger.debug($"BVM.doConnectAsync: attempting connection to {bleDevice.name}");
            var success = false;
            try
            {
                // Step 5: actually try to connect
                logger.debug($"BVM.doConnectAsync[Step 5]: calling adapter.ConnectToDeviceAsync");
                await adapter.ConnectToDeviceAsync(bleDevice.device);

                // Step 5a: verify connection
                logger.debug($"BVM.doConnectAsync[Step 5a]: verifying connection");
                foreach (var d in adapter.ConnectedDevices)
                {
                    if (d == bleDevice.device)
                    {
                        logger.debug($"BVM.doConnectAsync: verified!");
                        success = true;
                        break;
                    }
                }
                logger.debug($"BVM.doConnectAsync: never verified :-(");
            }
            catch (DeviceConnectionException ex)
            {
                logger.error($"BVM.doConnectAsync: exception connecting to device ({ex.Message})");

                // kick off the reset WHILE the alert message is running
                logger.error("BVM.doConnectAsync: resetting");
                _ = doResetAsync();

                notifyUser("Bluetooth",
                           ex.Message + "\nAutomatically resetting Bluetooth adapter. Click \"Ok\" to re-scan and try again.",
                           "Ok");
                return false;
            }

            if (!success)
                return logger.error($"BVM.doConnectAsync: failed connection to {bleDevice.name}");

            logger.info($"BVM.doConnectAsync: successfully connected to {bleDevice.name}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));
            connectionProgress = 0.05;

            // Step 6: connect to primary service
            await bleDevice.device.RequestMtuAsync(256);
            logger.debug($"BVM.doConnectAsync[Step 6]: connecting to primary service {primaryServiceId}");
            service = await bleDevice.device.GetServiceAsync(primaryServiceId);
            if (service is null)
            {
                return logger.error($"BVM.doConnectAsync: did not find primary service {primaryServiceId}");
            }

            logger.debug($"BVM.doConnectAsync: found primary service {service}");

            // Step 7: read characteristics
            logger.debug($"BVM.doConnectAsync[Step 7]: reading characteristics of service {service.Name} ({service.Id})");
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
                    logger.error($"BVM.doConnectAsync: ignoring unrecognized characteristic {c.Uuid}");
                    continue;
                }

                // store it by friendly name
                characteristicsByName.Add(name, c);
            }

            logger.debug("BVM.doConnectAsync: Registered characteristics:");
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

            logger.debug("BVM.doConnectAsync: polling device for other services");
            var allServices = await bleDevice.device.GetServicesAsync();
            foreach (var thisService in allServices)
            {
                logger.debug($"BVM.doConnectAsync: examining service {thisService.Name} (ID {thisService.Id})");
                if (thisService.Id == primaryServiceId)
                {
                    logger.debug("BVM.doConnectAsync: skipping primary service");
                    continue;
                }

                var characteristics = await thisService.GetCharacteristicsAsync();
                foreach (var c in characteristics)
                {
                    logger.debug($"BVM.doConnectAsync: reading {c.Name}");
                    // This line is required because for some reason attempting to read
                    // the Service Changed service cause the program to get blocked here
                    if (c.Name != "Service Changed")
                    {
                        var response = await c.ReadAsync();
                        if (response.data is null)
                        {
                            logger.error($"BVM.doConnectAsync: can't read {c.Uuid} ({c.Name})");
                        }
                        else
                        {
                            logger.hexdump(response.data, prefix: $"  {c.Uuid}: {c.Name} = ");
                            (spec as BluetoothSpectrometer).bleDeviceInfo.add(c.Name, Util.toASCII(response.data));
                        }
                    }
                }
            }

            // populate Spectrometer
            logger.debug("BVM.doConnectAsync: initializing spectrometer");
            await (spec as BluetoothSpectrometer).initAsync(characteristicsByName);

            //subscribeToUpdates();
            // start notifications
            
            foreach (var pair in characteristicsByName)
            {
                var name = pair.Key;
                var c = pair.Value;

                // disabled until I can troubleshoot with Nic
                if (c.CanUpdate && (name == "batteryStatus"))
                {
                    logger.debug($"BVM.doConnectAsync: starting notification updates on {name}");
                    //c.ValueUpdated -= _characteristicUpdated;
                    c.ValueUpdated += _characteristicUpdated;

                    // don't see a need to await this?
                    await c.StartUpdatesAsync();
                }
            }
            

            ////////////////////////////////////////////////////////////////////
            // all done
            ////////////////////////////////////////////////////////////////////

            (spec as BluetoothSpectrometer).bleDevice = bleDevice;
            BLEDevice.paired = true;
        }
        else
        {

        }

        // switch button to "disconnect"
        buttonConnectEnabled = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(buttonConnectText)));

        logger.debug("BVM.doConnectAsync: done");
        return true;
    }


    async Task subscribeToUpdates()
    {
        await Task.Delay(5000);

        // start notifications
        foreach (var pair in characteristicsByName)
        {
            var name = pair.Key;
            var c = pair.Value;

            // disabled until I can troubleshoot with Nic
            if (c.CanUpdate && name == "batteryStatus")
            {
                logger.debug($"BVM.doConnectAsync: starting notification updates on {name}");
                //c.ValueUpdated -= _characteristicUpdated;
                c.ValueUpdated += _characteristicUpdated;

                // don't see a need to await this?
                await c.StartUpdatesAsync();
            }
        }
    }

    ////////////////////////////////////////////////////////////////////////
    // Utility methods
    ////////////////////////////////////////////////////////////////////////

    // @todo test
    private void _characteristicUpdated(
            object sender, 
            CharacteristicUpdatedEventArgs characteristicUpdatedEventArgs)
    {
        logger.debug($"BVM._characteristicUpdated: start");
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

        //characteristicUpdatedEventArgs.Characteristic.
        logger.debug($"BVM._characteristicUpdated: done");
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
        // logger.debug($"BVM._bleAdapterDeviceDiscovered: start");
        var device = e.Device; // an IDevice

        // ignore anything without a name
        if (device.Name is null || device.Name.Length == 0)
            return;

        // ignore anything that doesn't have "WP" or "SiG" in the name
        var nameLC = device.Name.ToLower();
        if (!nameLC.Contains("wp") && !nameLC.Contains("sig"))
        {
            if (!ignoredNames.Contains(device.Name))
            {
                ignoredNames.Add(device.Name);
                logger.debug($"BVM._bleAdapterDeviceDiscovered: ignoring {device.Name}");
            }
            return;
        }

        BLEDevice bd = new BLEDevice(device);
        logger.debug($"BVM._bleAdapterDeviceDiscovered[Step 2a]: discovered {bd.name} (RSSI {bd.rssi} Id {device.Id})");
        bleDeviceList.Add(bd);
    }
    private SortedSet<string> ignoredNames = new SortedSet<string>();

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
        logger.debug($"BVM.selectBLEDevice: start");

        var selectedBLEDevice = obj as BLEDevice;
        if (selectedBLEDevice is null)
        {
            bleDevice = null;
            service = null;
            buttonConnectEnabled = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));
            return;
        }

        bleDevice = selectedBLEDevice;
        logger.debug($"BVM.selectBLEDevice[Step 3b]: selected device {bleDevice.name}");

        // let devices know which is selected, so they can advertise an 
        // appropriate row color
        foreach (var dev in bleDeviceList)
            dev.selected = dev.device.Id == bleDevice.device.Id;

        buttonConnectEnabled = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));

        logger.debug($"BVM.selectBLEDevice: done");
    }

    public void selectUSBDevice(object obj)
    {
        logger.debug($"BVM.selectUSBDevice: start");

        foreach (var dev in bleDeviceList)
            dev.selected = true;

        buttonConnectEnabled = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(connectButtonBackgroundColor)));

        logger.debug($"BVM.selectBLEDevice: done");
    }
}
