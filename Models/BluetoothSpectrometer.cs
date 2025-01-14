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

    public BLEDevice bleDevice { get; set; } = null;

    ushort lastCRC;

    const int MAX_RETRIES = 5;
    const int THROWAWAY_SPECTRA = 9;

    const int AUTO_OPT_TARGET_RATIO = 32;
    const int AUTO_TAKING_DARK = 33;
    const int AUTO_LASER_WARNING_DELAY = 34;
    const int AUTO_LASER_WARMUP = 35;
    const int AUTO_TAKING_RAMAN = 36;

    bool dataCollectingStarted = false;
    bool optimizationDone = false;
    bool waitingForGeneric = false;
    bool acqSynced = false;

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
        await syncAutoRamanParameters();

        //updateRSSI();

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

    async Task<bool> syncAcqParams()
    {
        byte[] request = { 0xbf };
        lastRequest = Opcodes.GET_INTEGRATION_TIME;

        logger.hexdump(request, "sync acq int time data: ");

        if (!await sem.WaitAsync(100))
        {
            logger.error("Spectrometer.getIntegrationTime: couldn't get semaphore");
        }
        var ok = await writeGenericCharacteristic(request);
        sem.Release();

        waitingForGeneric = true;
        while (waitingForGeneric)
        {
            await Task.Delay(10);
        } 
        
        request = new byte[]{ 0xff, 0x63 };
        lastRequest = Opcodes.GET_SCANS_TO_AVERAGE;

        logger.hexdump(request, "sync acq avg data: ");

        if (!await sem.WaitAsync(100))
        {
            logger.error("Spectrometer.getIntegrationTime: couldn't get semaphore");
        }
        ok = await writeGenericCharacteristic(request);
        sem.Release();

        waitingForGeneric = true;
        while (waitingForGeneric)
        {
            await Task.Delay(10);
        }
        
        request = new byte[]{ 0xc5 };
        lastRequest = Opcodes.GET_DETECTOR_GAIN;

        logger.hexdump(request, "sync acq gain data: ");

        if (!await sem.WaitAsync(100))
        {
            logger.error("Spectrometer.getIntegrationTime: couldn't get semaphore");
        }
        ok = await writeGenericCharacteristic(request);
        sem.Release();

        waitingForGeneric = true;
        while (waitingForGeneric)
        {
            await Task.Delay(10);
        }

        acqSynced = true;

        return true;
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

    protected override void processGeneric(byte[] data)
    {
        byte[] payload = new byte[data.Length - 2];

        logger.debug("BLE Generic process copying payload");
        Array.Copy(data, 2, payload, 0, data.Length - 2);
        logger.debug("BLE Generic process copied payload");

        UInt64 val = ToBLEData.toUInt64(payload);
        logger.debug("BLE Generic process value parsed as {0} with last request as {1}", val, lastRequest.ToString());

        if (lastRequest == Opcodes.GET_INTEGRATION_TIME)
        {
            _nextIntegrationTimeMS = (uint)val;
            logger.debug("generic integration time getter returned {0}", val);
            NotifyPropertyChanged(nameof(integrationTimeMS));
        }
        else if (lastRequest == Opcodes.GET_SCANS_TO_AVERAGE)
        {
            _scansToAverage = (byte)val;
            logger.debug("generic scans to average getter returned {0}", val);
            NotifyPropertyChanged(nameof(scansToAverage));
        }
        else if (lastRequest == Opcodes.GET_DETECTOR_GAIN)
        {
            float gain = data[2];
            gain += (data[3] / 256f);
            _lastGainDb = gain;
            logger.debug("generic gain getter returned {0}", val);
            NotifyPropertyChanged(nameof(gainDb));
        }

        waitingForGeneric = false;
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
        request[0] = 0x95;    
        //request[1] = 0xfd;    
        Array.Copy(paramPack, 0, request, 1, paramPack.Length);
        logger.hexdump(request, "data: ");

        if (!await sem.WaitAsync(1000))
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

    public async Task updateRSSI()
    {
        while (paired)
        {
            
            //logger.debug("current RSSI {0}", rssi);
            NotifyPropertyChanged("rssi");
            await Task.Delay(500); 

        }
    }

    public override double rssi
    {
        get => bleDevice.rssi;
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

        double[] spectrum = await takeOneAsync(disableLaserAfterFirstPacket);
        logger.debug("Spectrometer.takeOneAveragedAsync: back from takeOneAsync");

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

        lastRaw = spectrum; 
        lastSpectrum = spectrum;

        measurement.reset();
        measurement.reload(this);

        if (PlatformUtil.transformerLoaded && useBackgroundRemoval && (dark != null || autoDarkEnabled || autoRamanEnabled))
        {
            if (dark != null)
            {
                logger.info("Performing background removal");
                for (int i = 0; i < spectrum.Length; ++i)
                {
                    spectrum[i] -= dark[i];
                }
            }

            double[] smoothed = PlatformUtil.ProcessBackground(wavenumbers, spectrum, eeprom.serialNumber);
            measurement.wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
            stretchedDark = new double[smoothed.Length];
            measurement.rawDark = dark;
            measurement.dark = stretchedDark;
            measurement.postProcessed = smoothed;
        }
        else
        {
            measurement.wavenumbers = wavenumbers;
            measurement.postProcessed = spectrum;
        }

        ////////////////////////////////////////////////////////////////////////
        // Store Measurement
        ////////////////////////////////////////////////////////////////////////

        logger.debug("Spectrometer.takeOneAveragedAsync: storing lastSpectrum");
        
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

        dataCollectingStarted = false;
        optimizationDone = false;
        acqSynced = false;
        collectionsCompleted = 0;  
        prevUpdate = DateTime.MinValue;
        prevValue = 0;
        firstCollect = true;
        delta = 0;

        // send acquire command
        if (!await sem.WaitAsync(100))
        {
            logger.error("Spectrometer.takeOneAveragedAsync: couldn't get semaphore");
            return null;
        }
        logger.debug("takeOneAsync: sending SPECTRUM_ACQUIRE");
        byte[] request = new byte[] { (byte)acquisitionMode };
        if (0 != await acquireChar.WriteAsync(request))
        {
            logger.error("failed to send acquire");
            sem.Release();
            return null;
        }
        sem.Release();
        monitorAutoRamanProgress();
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
        if (acquisitionMode == AcquisitionMode.AUTO_DARK)
            waitTime = 2 * (int)integrationTimeMS * scansToAverage + (int)laserWarningDelaySec * 1000 + (int)eeprom.laserWarmupSec * 1000;
        else if (acquisitionMode == AcquisitionMode.AUTO_RAMAN)
        {
            waitTime = 30000 + 2 * (int)integrationTimeMS * scansToAverage + (int)laserWarningDelaySec * 1000 + (int)eeprom.laserWarmupSec * 1000;
        }

        int timeout = waitTime * 2 + 4000;

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

    DateTime autoStart;
    DateTime autoEnd;
    int collectionsCompleted = 0;

    public async Task<bool> monitorAutoRamanProgress()
    {
        DateTime now = autoStart = DateTime.Now;
        logger.debug("monitor auto starting at {0}", autoStart.ToString("hh:mm:ss.fff"));
        double estimatedSeconds = 15 + maxCollectionTimeMS / 1000;
        double estimatedMilliseconds = estimatedSeconds * 1000;
        double prevProgress = 0;
        autoEnd = autoStart.AddSeconds(estimatedSeconds);
        logger.debug("initial auto end estimate at {0}", autoEnd.ToString("hh:mm:ss.fff"));

        await Task.Delay(67);

        while (true)
        {
            if (totalPixelsRead > 0)
                break;

            now = DateTime.Now;
            estimatedMilliseconds = (autoEnd - autoStart).TotalMilliseconds;
            double progress = (now - autoStart).TotalMilliseconds / estimatedMilliseconds;

            logger.debug("estimated progress currently at {0:f3}", progress);

            if (progress > prevProgress && progress <= 1)
            {
                raiseAcquisitionProgress(0.75 * progress);

                prevProgress = progress;
            }

            await Task.Delay(33);
        }

        return true;
    }

    public async Task<bool> monitorAutoDarkProgresS()
    {

        return true;
    }

    DateTime prevUpdate = DateTime.MinValue;
    int prevValue = 0;

    void updateAutoEstimate(int autoStep, int arg)
    {
        //double estimatedSeconds = 15 + maxCollectionTimeMS * 1000;
        //double estimatedMilliseconds = estimatedSeconds * 1000;
        //autoEnd = autoStart.AddSeconds(estimatedSeconds);
        if (autoStep == AUTO_OPT_TARGET_RATIO)
        {
            DateTime now = DateTime.Now;
            if (prevUpdate.Year != DateTime.MinValue.Year)
            {
                double slope = (now - prevUpdate).TotalMilliseconds / (arg - prevValue);
                double estMSToGo = slope * (90 - arg);
                double estimatedMilliseconds = (estMSToGo + maxCollectionTimeMS + 2500);
                autoEnd = DateTime.Now.AddMilliseconds(estimatedMilliseconds);
            }

            prevUpdate = now;
            prevValue = arg;
        }
        else if (autoStep > AUTO_OPT_TARGET_RATIO)
        {
            logger.debug("{0} scans to average with {1} remaining in arg at {2} int time", scansToAverage, arg, integrationTimeMS);

            if (acqSynced)
            {
                double estimatedMilliseconds = arg * integrationTimeMS;
                if (autoStep == AUTO_TAKING_RAMAN)
                    estimatedMilliseconds += 1700;
                logger.debug("scan completion estimated in {0} ms", estimatedMilliseconds);
                autoEnd = DateTime.Now.AddMilliseconds(estimatedMilliseconds);
            }
        }

        logger.debug("after update auto end estimate at {0}", autoEnd.ToString("hh:mm:ss.fff"));
    }

    bool firstCollect = true;
    int delta = 0;

    public void receiveSpectralUpdate(
            object sender,
            CharacteristicUpdatedEventArgs characteristicUpdatedEventArgs)
    {
        logger.debug($"BVM.receiveSpectralUpdate: start");
        var c = characteristicUpdatedEventArgs.Characteristic;

        byte[] data = c.Value;

        if (data[0] == 0xff && data[1] == 0xff)
        {
            logger.hexdump(data, "collection status update: ");

            if (data[2] == AUTO_OPT_TARGET_RATIO)
            {
                //raiseAcquisitionProgress(0.25 * (1 - Math.Abs(100 - data[3]) / (double)100));

                logger.debug("auto-raman optimize progress at {0} of 255", data[3]);

                if (Math.Abs(100 - data[3]) <= 11)
                {
                    optimizationDone = true;
                    double estimatedMilliseconds = (maxCollectionTimeMS + 2500);
                    autoEnd = DateTime.Now.AddMilliseconds(estimatedMilliseconds);
                    syncAcqParams();
                }
                else
                {
                    updateAutoEstimate(data[2], data[3]);
                }
            }
            else if (data[2] > AUTO_OPT_TARGET_RATIO)
            {
                UInt16 complete = (UInt16)((data[3] << 8) | data[4]);
                UInt16 total = (UInt16)((data[5] << 8) | data[6]);

                if (optimizationDone && !dataCollectingStarted)
                {
                    //if (autoRamanEnabled)
                    //    raiseAcquisitionProgress(0.25);
                    dataCollectingStarted = true;
                }

                if (total > 0)
                {
                    if (firstCollect)
                    {
                        firstCollect = false;
                        delta = total - complete + 1;
                    }

                    int completeProg = complete;
                    if (autoRamanEnabled)
                        completeProg = complete - delta;
                    int totalProg = total;
                    if (autoRamanEnabled)
                        totalProg = total - 5;

                    if (data[2] == AUTO_TAKING_RAMAN)
                        updateAutoEstimate(data[2], total - complete - 1);
                    else if (data[2] == AUTO_TAKING_DARK)
                        updateAutoEstimate(data[2], total - complete);

                    /*
                    if (autoDarkEnabled)
                        raiseAcquisitionProgress(0.75 * (completeProg / totalProg));
                    else if (autoRamanEnabled)
                        raiseAcquisitionProgress(0.25 + 0.5 * ((double)completeProg / totalProg));
                    */
                }

                if (data[2] == AUTO_TAKING_DARK)
                {
                    logger.debug("dark collection progress at {0} of {1}", complete, total);
                }
                else if (data[2] == AUTO_LASER_WARNING_DELAY)
                {
                    logger.debug("laser warning progress at {0} of {1}", complete, total);
                }
                else if (data[2] == AUTO_LASER_WARMUP)
                {
                    logger.debug("laser warmup progress at {0} of {1}", complete, total);
                }
                else if (data[2] == AUTO_TAKING_RAMAN)
                {
                    logger.debug("raman collection progress at {0} of {1}", complete, total);
                }
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

            if (autoRamanEnabled || autoDarkEnabled)
                raiseAcquisitionProgress(0.75 + 0.25 * ((double)totalPixelsRead) / totalPixelsToRead);
            else
                raiseAcquisitionProgress(((double)totalPixelsRead) / totalPixelsToRead);
            logger.debug($"BVM.receiveSpectralUpdate: total pixels read {totalPixelsRead} out of {totalPixelsToRead} expected");
        }
        //characteristicUpdatedEventArgs.Characteristic.
    }
}
