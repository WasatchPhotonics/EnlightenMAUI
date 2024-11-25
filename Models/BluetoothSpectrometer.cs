using System.ComponentModel;
using Plugin.BLE.Abstractions.Contracts;

using EnlightenMAUI.Common;
using EnlightenMAUI.Platforms;
using static Android.Widget.GridLayout;
using Java.Util.Functions;
using Plugin.BLE.Abstractions.EventArgs;
using System.Diagnostics;
using Xamarin.Google.Crypto.Tink.Prf;

namespace EnlightenMAUI.Models;

// This more-or-less corresponds to WasatchNET.Spectrometer, or 
// SiGDemo.Spectrometer.  Spectrometer state and logic should be 
// encapsulated here.
public class BluetoothSpectrometer : Spectrometer
{ 
    const int BLE_SUCCESS = 0; // result of Characteristic.WriteAsync

    // Singleton
    static BluetoothSpectrometer instance = null;

    // BLE comms
    Dictionary<string, ICharacteristic> characteristicsByName;
    // WhereAmI whereAmI;

    public BLEDeviceInfo bleDeviceInfo = new BLEDeviceInfo();
    public BLEDevice bleDevice = null;

    ushort lastCRC;

    const int MAX_RETRIES = 5;
    const int THROWAWAY_SPECTRA = 9;

    const int AUTO_OPT_TARGET_RATIO = 32;
    const int AUTO_TAKING_DARK = 33;
    const int AUTO_LASER_WARNING_DELAY = 34;
    const int AUTO_LASER_WARMUP = 35;
    const int AUTO_TAKING_RAMAN = 36;


    uint totalPixelsToRead;
    uint totalPixelsRead;
    double[] spectrum;

    ////////////////////////////////////////////////////////////////////////
    // Lifecycle 
    ////////////////////////////////////////////////////////////////////////

    static public BluetoothSpectrometer getInstance()
    {
        if (instance is null)
            instance = new BluetoothSpectrometer();
        return instance;
    }

    BluetoothSpectrometer()
    {
        reset();
    }

    public override void disconnect()
    {
        logger.debug("Spectrometer.disconnect: start");
        laserEnabled = false;
        autoDarkEnabled = false;
        autoRamanEnabled = false;
        reset();
        logger.debug("Spectrometer.disconnect: done");
    }

    public override void reset()
    { 
        logger.debug("Spectrometer.reset: start");
        paired = false;

        // Provide some test defaults so we can play with the chart etc while
        // disconnected.  These will all be overwritten when we read an EEPROM.
        pixels = 1952;
        laserExcitationNM = 785.0f;
        wavelengths = new double[pixels];
        for (int i = 0; i < pixels; i++)
            wavelengths[i] = laserExcitationNM + 15 + i / 10.0;
        wavenumbers = Util.wavelengthsToWavenumbers(laserExcitationNM, wavelengths);
        generatePixelAxis();

        if (measurement is null)
            measurement = new Measurement();
        measurement.reset();
        measurement.reload(this);

        characteristicsByName = null;
        note = "your text here";
        acquiring = false;
        lastCRC = 0;
        scansToAverage = 1;

        battery = new Battery();
        logger.debug("Spectrometer.reset: done");
    }

    public async Task<bool> initAsync(Dictionary<string, ICharacteristic> characteristicsByName)
    {
        logger.debug("Initializing Spectrometer");
        paired = false;

        this.characteristicsByName = characteristicsByName;

        ////////////////////////////////////////////////////////////////////
        // parse the EEPROM
        ////////////////////////////////////////////////////////////////////

        var pages = await readEEPROMAsync();
        if (pages is null)
        {
            logger.error("Spectrometer.initAsync: failed to read EEPROM");
            return false;
        }

        logger.debug("Spectrometer.initAsync: parsing EEPROM");
        if (!eeprom.parse(pages))
        {
            logger.error("Spectrometer.initAsync: failed to parse EEPROM");
            return false;
        }

        ////////////////////////////////////////////////////////////////////
        // post-process EEPROM
        ////////////////////////////////////////////////////////////////////

        logger.debug("Spectrometer.initAsync: post-processing EEPROM");
        pixels = eeprom.activePixelsHoriz;
        laserExcitationNM = eeprom.laserExcitationWavelengthNMFloat;

        logger.debug("Spectrometer.initAsync: computing wavecal");
        wavelengths = Util.generateWavelengths(pixels, eeprom.wavecalCoeffs);

        if (laserExcitationNM > 0)
            wavenumbers = Util.wavelengthsToWavenumbers(laserExcitationNM, wavelengths);
        else
            wavenumbers = null;

        logger.debug("Spectrometer.initAsync: generating pixel axis");
        generatePixelAxis();

        // set this early so battery and other BLE calls can progress
        paired = true;

        ////////////////////////////////////////////////////////////////////
        // finish initializing Spectrometer 
        ////////////////////////////////////////////////////////////////////

        raiseConnectionProgress(1);
        
        logger.debug("Spectrometer.initAsync: finishing spectrometer initialization");
        pixels = eeprom.activePixelsHoriz;

        //await updateBatteryAsync(); 

        // for now, ignore EEPROM configuration and hardcode
        // integrationTimeMS = (ushort)(eeprom.startupIntegrationTimeMS > 0 && eeprom.startupIntegrationTimeMS < 5000 ? eeprom.startupIntegrationTimeMS : 400);
        // gainDb = eeprom.detectorGain;
        integrationTimeMS = 400;
        await Task.Delay(10);
        gainDb = 8;
        await Task.Delay(10);

        verticalROIStartLine = eeprom.ROIVertRegionStart[0];
        verticalROIStopLine = eeprom.ROIVertRegionEnd[0];

        logger.info($"initialized {eeprom.serialNumber} {fullModelName}");
        logger.info($"  detector: {eeprom.detectorName}");
        logger.info($"  pixels: {pixels}");
        logger.info($"  integrationTimeMS: {integrationTimeMS}");
        logger.info($"  gainDb: {gainDb}");
        // logger.info($"  verticalROI: ({verticalROIStartLine}, {verticalROIStopLine})");
        logger.info($"  excitation: {laserExcitationNM:f3}nm");
        logger.info($"  wavelengths: ({wavelengths[0]:f2}, {wavelengths[pixels-1]:f2})");
        if (wavenumbers != null)
            logger.info($"  wavenumbers: ({wavenumbers[0]:f2}, {wavenumbers[pixels-1]:f2})");

        // I'm honestly not sure where we should initialize location, but it 
        // should probably happen after we've successfully connected to a
        // spectrometer and are ready to take measurements.  Significantly,
        // at this point we know the user has already granted location privs.
        //
        // whereAmI = WhereAmI.getInstance();

        //test set
        //dropFactor = 0.8f;

        logger.debug("Spectrometer.initAsync: done");
        return true;
    }

    protected override async Task<List<byte[]>> readEEPROMAsync()
    {
        logger.info("reading EEPROM");
        ICharacteristic eepromCmd;
        ICharacteristic eepromData;

        List<byte[]> pages = new List<byte[]>();
        EEPROMReadComplete = false;
        EEPROMBytesRead = 0;
        CurrentEEPROMPage = 0;
        EEPROMBuffer = new byte[EEPROM.PAGE_LENGTH * EEPROM.MAX_PAGES];

        while (!EEPROMReadComplete)
        {
            genericReturned = false;

            logger.debug($"Spectrometer.readEEPROMAsync: requestEEPROMSubpage: page {CurrentEEPROMPage}, offset {EEPROMBytesRead % EEPROM.PAGE_LENGTH}");
            byte[] request = { 0xff, 0x01, 0, (byte)CurrentEEPROMPage, (byte)(EEPROMBytesRead % EEPROM.PAGE_LENGTH) };
            bool ok = await writeGenericCharacteristic(request);
            if (!ok)
            {
                logger.error($"Spectrometer.readEEPROMAsync: failed to write eepromCmd({CurrentEEPROMPage}, {EEPROMBytesRead % EEPROM.PAGE_LENGTH})");
                return null;
            }

            while (!genericReturned)
                await Task.Delay(5);
        }

        for (int i = 0; i < EEPROM.MAX_PAGES; i++)
        {
            byte[] buf = new byte[EEPROM.PAGE_LENGTH];
            Array.Copy(EEPROMBuffer, i *  EEPROM.PAGE_LENGTH, buf, 0, EEPROM.PAGE_LENGTH);
            logger.hexdump(buf, $"adding page {i}: ");
            pages.Add(buf);
        }

        logger.debug($"Spectrometer.readEEPROMAsync: done");
        return pages;
    }

    ////////////////////////////////////////////////////////////////////////
    // scansToAverage
    ////////////////////////////////////////////////////////////////////////

    public override byte scansToAverage 
    { 
        get => _scansToAverage;
        set
        {
            byte[] data = { 0xff, 0x62, 0x00, value }; // send as little-endian ushort
            _ = writeGenericCharacteristic(data);
            _scansToAverage = value;
        }
    }
    byte _scansToAverage = 1;

    ////////////////////////////////////////////////////////////////////////
    // integrationTimeMS
    ////////////////////////////////////////////////////////////////////////

    public override uint integrationTimeMS
    {
        get => _nextIntegrationTimeMS;
        set 
        { 
            _nextIntegrationTimeMS = value;
            logger.debug($"Spectrometer.integrationTimeMS: next = {value}");
            _ = syncIntegrationTimeMSAsync();
            NotifyPropertyChanged();
        }
    }

    async Task<bool> syncIntegrationTimeMSAsync()
    {
        if (!paired || characteristicsByName is null)
            return false;

        if (_nextIntegrationTimeMS == _lastIntegrationTimeMS)
            return true;

        ushort value = Math.Min((ushort)5000, Math.Max((ushort)1, (ushort)Math.Round((decimal)_nextIntegrationTimeMS)));
        byte[] data = ToBLEData.convert(value, len: 3);

        byte[] request = { 0xb2, 0, 0, 0 };
        Array.Copy(data, 0, request, 1, data.Length);

        logger.info($"Spectrometer.syncIntegrationTimeMSAsync({value})");
        logger.hexdump(request, "data: ");

        if (!await sem.WaitAsync(100))
        {
            logger.error("Spectrometer.takeOneAveragedAsync: couldn't get semaphore");
            return false;
        }
        var ok = await writeGenericCharacteristic(request);
        sem.Release();
        if (ok)
        { 
            _lastIntegrationTimeMS = _nextIntegrationTimeMS;
            await pauseAsync("syncIntegrationTimeMSAsync");
        }
        else
            logger.error($"Failed to set integrationTimeMS {value}");

        return ok;
    }

    ////////////////////////////////////////////////////////////////////////
    // gainDb
    ////////////////////////////////////////////////////////////////////////

    // for documentation on the unsigned bfloat16 datatype used by gain, see
    // https://github.com/WasatchPhotonics/Wasatch.NET/blob/master/WasatchNET/FunkyFloat.cs

    public override float gainDb
    {
        get => _nextGainDb;
        set 
        { 
            if (0 <= value && value <= 72)
            {
                _nextGainDb = value;
                logger.debug($"Spectrometer.gainDb: next = {value}");
                _ = syncGainDbAsync();
                NotifyPropertyChanged();
            }
            else
            {
                logger.error($"ignoring out-of-range gainDb {value}");
            }
        }
    }

    async Task<bool> syncGainDbAsync()
    {
        if (!paired || characteristicsByName is null)
            return false;

        if (_nextGainDb == _lastGainDb)
            return true;
                        
        byte msb = (byte)Math.Floor(_nextGainDb);
        byte lsb = (byte)(((byte)Math.Round( (_nextGainDb - msb) * 256.0)) & 0xff);

        ushort value = (ushort)((msb << 8) | lsb);
        ushort len = 2;

        byte[] data = ToBLEData.convert(value, len: len);
        byte[] request = { 0xb7, 0, 0 };
        Array.Copy(data, 0, request, 1, data.Length);

        /*
        byte[] data = ToBLEData.convert(value, len: 4);

        byte[] request = { 0xb2, 0, 0, 0, 0 };
        Array.Copy(data, 0, request, 1, data.Length);

        logger.info($"Spectrometer.syncIntegrationTimeMSAsync({value})");
        logger.hexdump(request, "data: ");

        var ok = await writeGenericCharacteristic(request);
        */


        logger.debug($"converting gain {_nextGainDb:f4} to msb 0x{msb:x2}, lsb 0x{lsb:x2}, value 0x{value:x4}, request {request}");

        logger.info($"Spectrometer.syncGainDbAsync({_nextGainDb})"); 
        logger.hexdump(request, "data: ");

        if (!await sem.WaitAsync(100))
        {
            logger.error("Spectrometer.takeOneAveragedAsync: couldn't get semaphore");
            return false;
        }
        var ok = await writeGenericCharacteristic(request);
        sem.Release();
        if (ok)
        {
            _lastGainDb = _nextGainDb;
            await pauseAsync("syncGainDbAsync");
        }
        else
            logger.error($"Failed to set gainDb {value:x4}");

        // kludge
        if (!ok)
        {
            logger.error("KLUDGE: ignoring gainDb failure");
            ok = true;
            _lastGainDb = _nextGainDb;
        }

        return ok;
    }

    ////////////////////////////////////////////////////////////////////////
    // Vertical ROI Start/Stop
    ////////////////////////////////////////////////////////////////////////

    public override ushort verticalROIStartLine
    {
        get => _nextVerticalROIStartLine;
        set 
        { 
            if (value > 0 && value < eeprom.activePixelsVert)
            {
                _nextVerticalROIStartLine = value;
                logger.debug($"Spectrometer.verticalROIStartLine -> {value}");

                byte[] data = ToBLEData.convert(value, len: 2);
                byte[] dataToSend = { 0xff, 0x21, 0, 0 };
                Array.Copy(data, 0, dataToSend, 2, data.Length);

                _ = writeGenericCharacteristic(dataToSend);

                NotifyPropertyChanged(nameof(verticalROIStartLine));
            }
            else
            {
                logger.error($"ignoring out-of-range start line {value}");
            }
        }
    }

    public override ushort verticalROIStopLine
    {
        get => _nextVerticalROIStopLine;
        set 
        { 
            if (value > 0 && value < eeprom.activePixelsVert)
            {
                _nextVerticalROIStopLine = value;
                logger.debug($"Spectrometer.verticalROIStopLine -> {value}");
                
                byte[] data = ToBLEData.convert(value, len: 2);
                byte[] dataToSend = { 0xff, 0x23, 0, 0 };
                Array.Copy(data, 0, dataToSend, 2, data.Length);

                _ = writeGenericCharacteristic(dataToSend);

                NotifyPropertyChanged(nameof(verticalROIStopLine));
            }
            else
            {
                logger.error($"ignoring out-of-range stop line {value}");
            }
        }
    }

    ////////////////////////////////////////////////////////////////////////
    // laserWarningDelaySec
    ////////////////////////////////////////////////////////////////////////

    public override byte laserWarningDelaySec
    {
        get => _laserWarningDelaySec;
        set
        {
            byte[] data = { 0x8a, value };
            _ = writeGenericCharacteristic(data);
            _laserWarningDelaySec = value;
        }
    }

    ////////////////////////////////////////////////////////////////////////
    // Auto-Raman Parameters
    ////////////////////////////////////////////////////////////////////////

    public override ushort maxCollectionTimeMS
    {
        get => _maxCollectionTimeMS;
        set
        {
            if (value != _maxCollectionTimeMS)
            {
                _maxCollectionTimeMS = value;
                logger.debug($"Spectrometer.maxTimeMS -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    public override ushort startIntTimeMS
    {
        get => _startIntTimeMS;
        set
        {
            if (value != _startIntTimeMS)
            {
                _startIntTimeMS = value;
                logger.debug($"Spectrometer.startIntTimeMS -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    public override byte startGainDb
    {
        get => _startGainDB;
        set
        {
            if (0 <= value && value <= 72)
            {
                _startGainDB = value;
                logger.debug($"Spectrometer.startGainDb: next = {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
            else
            {
                logger.error($"ignoring out-of-range gainDb {value}");
            }
        }
    }

    public override ushort minIntTimeMS
    {
        get => _minIntTimeMS;
        set
        {
            if (value != _minIntTimeMS)
            {
                _minIntTimeMS = value;
                logger.debug($"Spectrometer.minIntTimeMS -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    public override ushort maxIntTimeMS
    {
        get => _maxIntTimeMS;
        set
        {
            if (value != _maxIntTimeMS)
            {
                _maxIntTimeMS = value;
                logger.debug($"Spectrometer.maxIntTimeMS -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    public override byte minGainDb
    {
        get => _minGainDb;
        set
        {
            if (0 <= value && value <= 72)
            {
                _minGainDb = value;
                logger.debug($"Spectrometer.minGainDb: next = {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
            else
            {
                logger.error($"ignoring out-of-range gainDb {value}");
            }
        }
    }

    public override byte maxGainDb
    {
        get => _maxGainDb;
        set
        {
            if (0 <= value && value <= 72)
            {
                _maxGainDb = value;
                logger.debug($"Spectrometer.maxGainDb: next = {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
            else
            {
                logger.error($"ignoring out-of-range gainDb {value}");
            }
        }
    }

    public override ushort targetCounts
    {
        get => _targetCounts;
        set
        {
            if (value != _targetCounts)
            {
                _targetCounts = value;
                logger.debug($"Spectrometer.targetCounts -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    public override ushort minCounts
    {
        get => _minCounts;
        set
        {
            if (value != _minCounts)
            {
                _minCounts = value;
                logger.debug($"Spectrometer.minCounts -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    public override ushort maxCounts
    {
        get => _maxCounts;
        set
        {
            if (value != _maxCounts)
            {
                _maxCounts = value;
                logger.debug($"Spectrometer.maxCounts -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    public override byte maxFactor
    {
        get => _maxFactor;
        set
        {
            if (value != _maxFactor)
            {
                _maxFactor = value;
                logger.debug($"Spectrometer.maxFactor -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    public override float dropFactor
    {
        get => _dropFactor;
        set
        {
            _dropFactor = value;
            logger.debug($"Spectrometer.dropFactor: next = {value}");
            _ = syncAutoRamanParameters();
            NotifyPropertyChanged();
        }
    }

    public override ushort saturationCounts
    {
        get => _saturationCounts;
        set
        {
            if (value != _saturationCounts)
            {
                _saturationCounts = value;
                logger.debug($"Spectrometer.saturationCounts -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    public override byte maxAverage
    {
        get => _maxAverage;
        set
        {
            if (value != _maxAverage)
            {
                _maxAverage = value;
                logger.debug($"Spectrometer.maxAverage -> {value}");
                _ = syncAutoRamanParameters();
                NotifyPropertyChanged();
            }
        }
    }

    async Task<bool> syncAutoRamanParameters()
    {
        if (!paired || characteristicsByName is null || holdAutoRamanParameterSet)
            return false;

        byte[] paramPack = packAutoRamanParameters();

        byte[] request = new byte[paramPack.Length + 2];
        request[0] = 0xff;    
        request[1] = 0xfd;    
        Array.Copy(paramPack, 0, request, 2, paramPack.Length);
        logger.hexdump(request, "data: ");

        if (!await sem.WaitAsync(100))
        {
            logger.error("Spectrometer.syncAutoRamanParameters: couldn't get semaphore");
            return false;
        }
        var ok = await writeGenericCharacteristic(request);
        sem.Release();
        if (ok)
        {
            await pauseAsync("syncAutoRamanParameters");
        }
        else
            logger.error($"Failed to set auto raman params");

        // kludge
        if (!ok)
        {
            logger.error("KLUDGE: ignoring auto params failure");
            ok = true;
        }

        return ok;
    }

    byte[] packAutoRamanParameters()
    {
        List<byte> data = new List<byte>();

        data.Add((byte)(maxCollectionTimeMS & 0xFF));
        data.Add((byte)((maxCollectionTimeMS >> 8) & 0xFF));
        data.Add((byte)(startIntTimeMS & 0xFF));
        data.Add((byte)((startIntTimeMS >> 8) & 0xFF));
        data.Add((byte)(startGainDb & 0xFF));
        data.Add((byte)(maxIntTimeMS & 0xFF));
        data.Add((byte)((maxIntTimeMS >> 8) & 0xFF));
        data.Add((byte)(minIntTimeMS & 0xFF));
        data.Add((byte)((minIntTimeMS >> 8) & 0xFF));
        data.Add((byte)(maxGainDb & 0xFF));
        data.Add((byte)(minGainDb & 0xFF));
        data.Add((byte)(targetCounts & 0xFF));
        data.Add((byte)((targetCounts >> 8) & 0xFF));
        data.Add((byte)(maxCounts & 0xFF));
        data.Add((byte)((maxCounts >> 8) & 0xFF));
        data.Add((byte)(minCounts & 0xFF));
        data.Add((byte)((minCounts >> 8) & 0xFF));
        data.Add((byte)(maxFactor & 0xFF));
        data.Add((byte)((int)dropFactor & 0xFF));
        data.Add((byte)((int)((dropFactor - (int)dropFactor) * 0x100) & 0xFF));
        data.Add((byte)(saturationCounts & 0xFF));
        data.Add((byte)((saturationCounts >> 8) & 0xFF));
        data.Add((byte)(maxAverage & 0xFF));


        byte[] serializedParams = data.ToArray();
        return serializedParams;
    }

    ////////////////////////////////////////////////////////////////////////
    // genericCharacteristic
    ////////////////////////////////////////////////////////////////////////

    byte genericSequence = 0;

    private async Task<bool> writeGenericCharacteristic(byte[] data)
    {
        if (characteristicsByName is null)
        {
            logger.error("writeGenericCharacteristic: no characteristics");
            return false;
        }

        var characteristic = characteristicsByName["generic"];
        if (characteristic is null)
        {
            logger.error("Generic characteristic not found");
            return false;
        }

        // prepend sequence byte
        byte[] dataToSend = new byte[data.Length + 1]; 
        dataToSend[0] = genericSequence++;
        Array.Copy(data, 0, dataToSend, 1, data.Length);

        logger.hexdump(dataToSend, "writeGenericCharacteristic: ");
        var ok = 0 == await characteristic.WriteAsync(dataToSend);
        if (ok)
            await pauseAsync("writeGenericCharacteristic");
        else
            logger.error($"Failed to write generic characteristic {dataToSend}");

        return ok;
    }

    public override bool autoDarkEnabled
    {
        get => acquisitionMode == AcquisitionMode.AUTO_DARK;
        set
        {
            if (value && acquisitionMode != AcquisitionMode.AUTO_DARK)
            {
                logger.debug($"Spectrometer.ramanModeEnabled: autoDark -> {value}");
                acquisitionMode = AcquisitionMode.AUTO_DARK;
                laserState.enabled = false;
                _ = syncLaserStateAsync();
                NotifyPropertyChanged(nameof(autoRamanEnabled));
                NotifyPropertyChanged(nameof(autoDarkEnabled));
            }
            else if (!value && !autoRamanEnabled)
            {
                logger.debug($"Spectrometer.ramanModeEnabled: autoDark -> {value}");
                acquisitionMode = AcquisitionMode.STANDARD;
                NotifyPropertyChanged(nameof(autoRamanEnabled));
                NotifyPropertyChanged(nameof(autoDarkEnabled));
            }
            else if (value)
                logger.debug($"Spectrometer.ramanModeEnabled: mode already {AcquisitionMode.AUTO_DARK}");
        }
    }

    public override bool autoRamanEnabled
    {
        get => acquisitionMode == AcquisitionMode.AUTO_RAMAN;
        set
        {
            if (value && acquisitionMode != AcquisitionMode.AUTO_RAMAN)
            {
                logger.debug($"Spectrometer.ramanModeEnabled: autoRaman -> {value}");
                acquisitionMode = AcquisitionMode.AUTO_RAMAN;
                laserState.enabled = false;
                _ = syncLaserStateAsync();
                NotifyPropertyChanged(nameof(autoRamanEnabled));
                NotifyPropertyChanged(nameof(autoDarkEnabled));
            }
            else if (!value && !autoDarkEnabled)
            {
                logger.debug($"Spectrometer.ramanModeEnabled: autoRaman -> {value}");
                acquisitionMode = AcquisitionMode.STANDARD;
                NotifyPropertyChanged(nameof(autoRamanEnabled));
                NotifyPropertyChanged(nameof(autoDarkEnabled));
            }
            else if (value)
                logger.debug($"Spectrometer.ramanModeEnabled: mode already {AcquisitionMode.AUTO_RAMAN}");
        }
    }

    public override byte laserWatchdogSec
    {
        get => laserState.watchdogSec;
        set
        {
            if (laserState.watchdogSec != value)
            {
                laserState.watchdogSec = value;
                _ = syncLaserStateAsync();
            }
            else
                logger.debug($"Spectrometer.laserWatchdogSec: already {value}");
        }
    }

    public override bool laserEnabled
    {
        get => laserState.enabled;
        set
        {
            logger.debug($"laserEnabled.set: setting {value}");
            if (laserState.enabled != value)
            {
                laserState.enabled = value;
                _ = syncLaserStateAsync();
                NotifyPropertyChanged();
            }
            else
                logger.debug($"Spectrometer.laserEnabled: already {value}");
            logger.debug("laserEnabled.set: done");
        }
    }

    public override ushort laserDelayMS
    {
        get => laserState.laserDelayMS;
        set
        {
            if (laserState.laserDelayMS != value)
            {
                laserState.laserDelayMS = value;
                _ = syncLaserStateAsync();
            }
            else
                logger.debug($"Spectrometer.laserDelayMS: already {value}");
        }
    }

    async Task<bool> syncLaserStateAsync()
    {
        logger.debug("Spectrometer.syncLaserStateAsync: start");
        if (!laserSyncEnabled)
        {
            logger.debug("Spectrometer.syncLaserStateAsync: skipping");
            return false;
        }

        laserState.dump();

        if (!paired || characteristicsByName is null)
            return false;

        ICharacteristic characteristic;
        characteristicsByName.TryGetValue("laserState", out characteristic);
        if (characteristic is null)
        {
            logger.error("Spectrometer.syncLaserState: laserState characteristic not found");
            return false;
        }

        byte[] request = laserState.serialize();
        logger.hexdump(request, "Spectrometer.syncLaserStateAsync: ");

        if (BLE_SUCCESS != await characteristic.WriteAsync(request))
        {
            logger.error($"Failed to set laserState");
            return false;
        }

        logger.debug("successfully wrote laserState");
        await pauseAsync("syncLaserStateAsync");
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // battery
    ////////////////////////////////////////////////////////////////////////

    // I used to call this at the END of an acquisition, and that worked; 
    // until it didn't.  Now I call it BEFORE each acquisition, and that
    // seems to work better?
    internal override async Task<bool> updateBatteryAsync()
    {
        logger.debug("Spectrometer.updateBatteryAsync: starting");

        if (!battery.isExpired)
        {
            logger.debug("Spectrometer.updateBatteryAsync: battery state still valid, skipping");
            return false;
        }

        if (!paired || characteristicsByName is null)
        {
            logger.debug($"updateBatteryAsync: skipping because paired = {paired} or characteristicsByName null");
            return false;
        }

        var characteristic = characteristicsByName["batteryStatus"];
        if (characteristic is null)
        {
            logger.error("Spectrometer.batteryUpdateAsync: can't find characteristic batteryStatus");
            return false;
        }

        logger.debug("Spectrometer.updateBatteryAsync: waiting on semaphore");
        if (!await sem.WaitAsync(50))
        {
            logger.error("Spectrometer.updateBatteryAsync: couldn't get semaphore");
            return false;
        }

        logger.info("Spectrometer.batteryUpdateAsync: reading battery status");
        var response = await characteristic.ReadAsync();
        if (response.data is null)
        {
            logger.error("Spectrometer.batteryUpdateAsync: failed reading battery");
            sem.Release();
            return false;
        }
        logger.hexdump(response.data, "batteryStatus: ");
        battery.parse(response.data);
        await pauseAsync("Spectrometer.updateBatteryAsync");

        logger.debug("Spectrometer.updateBatteryAsync: sending batteryStatus notification");
        NotifyPropertyChanged("batteryStatus");

        logger.debug("Spectrometer.updateBatteryAsync: done");
        sem.Release();
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // spectra
    ////////////////////////////////////////////////////////////////////////

    // responsible for taking one fully-averaged measurement
    public override async Task<bool> takeOneAveragedAsync()
    {
        if (!paired || characteristicsByName is null)
            return false;

        logger.debug("Spectrometer.takeOneAveragedAsync: -------------------------");
        logger.debug("Spectrometer.takeOneAveragedAsync: take one averaged reading");
        logger.debug("Spectrometer.takeOneAveragedAsync: -------------------------");

        // push-down any changed acquisition parameters
        logger.debug("Spectrometer.takeOneAveragedAsync: syncing integration time");
        if (! await syncIntegrationTimeMSAsync())
            return false;

        logger.debug("Spectrometer.takeOneAveragedAsync: syncing gain");
        if (! await syncGainDbAsync())
            return false;

        // update battery FIRST
        logger.debug("Spectrometer.takeOneAveragedAsync: updating battery");
        //await updateBatteryAsync();

        // for progress bar
        totalPixelsToRead = pixels; // * scansToAverage;
        totalPixelsRead = 0;
        acquiring = true;

        // TODO: integrate laserDelayMS into showProgress
        var swRamanMode = laserState.mode == LaserMode.AUTO_DARK && LaserState.SW_RAMAN_MODE;
        logger.debug($"Spectrometer.takeOneAveragedAsync: swRamanMode {swRamanMode}");
        if (swRamanMode)
        {
            const int MAX_SPECTRUM_READOUT_TIME_MS = 6000;
            var watchdogMS = (scansToAverage + 1) * integrationTimeMS + MAX_SPECTRUM_READOUT_TIME_MS;
            var watchdogSec = (byte)((Math.Max(MAX_SPECTRUM_READOUT_TIME_MS, watchdogMS) / 1000.0) * 2);
            logger.debug($"Spectrometer.takeOneAveragedAsync: setting laserWatchdogSec -> {watchdogSec}");

            // since we're going to sync the laser state immediately after to turn on the laser,
            // skip this sync
            laserSyncEnabled = false;
            laserWatchdogSec = watchdogSec;
            laserSyncEnabled = true;

            logger.debug("Spectrometer.takeOneAveragedAsync: setting laserEnabled = true");
            laserEnabled = true;

            logger.debug($"Spectrometer.takeOneAveragedAsync: waiting {laserState.laserDelayMS}ms");
            await Task.Delay(laserState.laserDelayMS);
        }

        logger.debug($"Spectrometer.takeOneAveragedAsync: integrationTimeMS {integrationTimeMS}, gainDb {gainDb}, scansToAverage {scansToAverage}, laserWatchdogSec {laserWatchdogSec}");

        bool disableLaserAfterFirstPacket = swRamanMode;

        if (!await sem.WaitAsync(100))
        {
            logger.error("Spectrometer.takeOneAveragedAsync: couldn't get semaphore");
            return false;                        
        }

        double[] spectrum = await takeOneAsync(disableLaserAfterFirstPacket);
        logger.debug("Spectrometer.takeOneAveragedAsync: back from takeOneAsync");

        sem.Release();

        if (spectrum is null)
        {
            logger.error("Spectrometer.takeOneAveragedAsync: spectrum is null");

            if (swRamanMode)
                laserEnabled = false;

            logger.error("Spectrometer.takeOneAveragedAsync: giving up");
            return acquiring = false;
        }

        ////////////////////////////////////////////////////////////////////////
        // Post-Processing
        ////////////////////////////////////////////////////////////////////////

        // Bin2x2
        apply2x2Binning(spectrum);

        // Raman Intensity Correction
        applyRamanIntensityCorrection(spectrum);


        if (PlatformUtil.transformerLoaded && useBackgroundRemoval && (dark != null || autoDarkEnabled || autoRamanEnabled))
        {
            logger.info("Performing background removal");
            for (int i = 0; i < spectrum.Length; ++i)
            {
                spectrum[i] -= dark[i];
            }

            double[] smoothed = PlatformUtil.ProcessBackground(wavenumbers, spectrum);
            measurement.wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
            stretchedDark = new double[smoothed.Length];
            spectrum = smoothed;
        }
        else
        {
            measurement.wavenumbers = wavenumbers;
        }

        ////////////////////////////////////////////////////////////////////////
        // Store Measurement
        ////////////////////////////////////////////////////////////////////////

        logger.debug("Spectrometer.takeOneAveragedAsync: storing lastSpectrum");
        lastSpectrum = spectrum;
   
        measurement.reset();
        measurement.reload(this);
        logger.info($"Spectrometer.takeOneAveragedAsync: acquired Measurement {measurement.measurementID}");

        logger.debug($"Spectrometer.takeOneAveragedAsync: at end, spec.measurement.processed is {0}", 
            measurement.processed == null ? "null" : "NOT NULL");
        if (measurement.processed != null)
        {
            logger.debug($"Spectrometer.takeOneAveragedAsync: at end, spec.measurement.processed mean is {0:f2}",
                measurement.processed.Average());
        }

        logger.debug("Spectrometer.takeOneAveragedAsync: done");
        acquiring = false;
        return true;
    }

    // Take one spectrum (of many, if doing scan averaging).  This is private,
    // callers are expected to use takeOneAveragedAsync().
    // 
    // There is no need to disable the laser if returning NULL, as the caller
    // will do so anyway.
    protected override async Task<double[]> takeOneAsync(bool disableLaserAfterFirstPacket)
    {
        if (!paired || characteristicsByName is null)
            return null;

        const int headerLen = 2;

        var acquireChar = characteristicsByName["acquireSpectrum"];
        if (acquireChar is null)
        {
            logger.error("can't find characteristic acquireSpectrum");
            return null;
        }

        spectrum = new double[pixels];
        totalPixelsRead = 0;
        totalPixelsToRead = pixels;

        // send acquire command
        logger.debug("takeOneAsync: sending SPECTRUM_ACQUIRE");
        byte[] request = new byte[] { (byte)acquisitionMode };
        if (0 != await acquireChar.WriteAsync(request))
        {
            logger.error("failed to send acquire");
            return null;
        }

        logger.debug("waiting for spectral data");
        bool ok = await monitorSpectrumAcquire();
        if (!ok)
        {
            logger.debug("spectrum collection timed out");
            return null;
        }

        // apply 2x2 binning
        if (eeprom.featureMask.bin2x2)
        {
            var smoothed = new double[spectrum.Length];
            for (int i = 0; i < spectrum.Length - 1; i++)
                smoothed[i] = (spectrum[i] + spectrum[i + 1]) / 2.0;
            smoothed[spectrum.Length - 1] = spectrum[spectrum.Length - 1];
            spectrum = smoothed;
        }

        logger.debug("Spectrometer.takeOneAsync: returning completed spectrum");
        return spectrum;
    }

    public async Task<bool> monitorSpectrumAcquire()
    {
        int bufferTime = 10000;

        int waitTime = (int)integrationTimeMS + bufferTime;
        if (laserState.mode == LaserMode.AUTO_DARK)
            waitTime = 2 * (int)integrationTimeMS * scansToAverage + (int)laserWarningDelaySec * 1000 + (int)eeprom.laserWarmupSec * 1000;

        int timeout = waitTime * 2;

        Stopwatch sw = Stopwatch.StartNew();
        sw.Start();

        while (sw.ElapsedMilliseconds <= timeout)
        {
            await Task.Delay(33);

            if (totalPixelsRead == totalPixelsToRead)
                return true;
        }

        return false;
    }


    public void receiveSpectralUpdate(
            object sender,
            CharacteristicUpdatedEventArgs characteristicUpdatedEventArgs)
    {
        logger.debug($"BVM.receiveSpectralUpdate: start");
        var c = characteristicUpdatedEventArgs.Characteristic;

        byte[] data = c.Value;

        if (data[0] == 0xff && data[1] == 0xff)
        {
            if (data[2] == AUTO_OPT_TARGET_RATIO)
            {
                logger.debug("auto-raman optimize progress at {0} of 255", data[3]);
            }
            else if (data[2] == AUTO_TAKING_DARK)
            {
                logger.debug("dark collection progress at {0} of {1}", data[3], data[4]);
            }
            else if (data[2] == AUTO_LASER_WARNING_DELAY)
            {
                logger.debug("laser warning progress at {0} of {1}", data[3], data[4]);
            }
            else if (data[2] == AUTO_LASER_WARMUP)
            {
                logger.debug("laser warmup progress at {0} of {1}", data[3], data[4]);
            }
            else if (data[2] == AUTO_TAKING_RAMAN)
            {
                logger.debug("raman collection progress at {0} of {1}", data[3], data[4]);
            }
        }
        else
        {
            int pixelsInPacket = (int)data.Length / 2 - 1;
            logger.debug("reading {0} pixels", pixelsInPacket);
            for (int i = 1; i <= pixelsInPacket; i++)
            {
                var offset = i * 2;
                ushort intensity = (ushort)((data[offset + 1] << 8) | data[offset]);

                logger.debug("reading bytes {0} and {1} as {2:X} and {3:X}", totalPixelsRead * 2, totalPixelsRead * 2 + 1, data[offset], data[offset + 1]);
                logger.debug("reading pixel {0} as {1}", totalPixelsRead, intensity);

                if (totalPixelsRead >= spectrum.Length)
                    logger.error("more received data than expected...");
                else
                    spectrum[totalPixelsRead] = intensity;
                totalPixelsRead += 1;
            }

            raiseAcquisitionProgress(((double)totalPixelsRead) / totalPixelsToRead);
            logger.debug($"BVM.receiveSpectralUpdate: total pixels read {totalPixelsRead} out of {totalPixelsToRead} expected");
        }
        //characteristicUpdatedEventArgs.Characteristic.
    }
}
