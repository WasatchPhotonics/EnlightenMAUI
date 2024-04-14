using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Plugin.BLE.Abstractions.Contracts;

using EnlightenMAUI.Common;

namespace EnlightenMAUI.Models;

// This more-or-less corresponds to WasatchNET.Spectrometer, or 
// SiGDemo.Spectrometer.  Spectrometer state and logic should be 
// encapsulated here.
public class Spectrometer : INotifyPropertyChanged
{
    // Singleton
    static Spectrometer instance = null;

    // BLE comms
    Dictionary<string, ICharacteristic> characteristicsByName;
    // WhereAmI whereAmI;

    // hardware model
    public uint pixels;
    public float laserExcitationNM;
    public EEPROM eeprom = EEPROM.getInstance();
    public Battery battery;

    public BLEDeviceInfo bleDeviceInfo = new BLEDeviceInfo();
    public BLEDevice bleDevice = null;

    // software state
    public double[] wavelengths;
    public double[] wavenumbers;
    public double[] xAxisPixels;

    public double[] lastSpectrum;
    public double[] dark;

    public Measurement measurement;
    public string note { get; set; }
    public string qrValue { get; set; } // MZ: ???

    ushort lastCRC;

    // @see https://forums.xamarin.com/discussion/93330/mutex-is-bugged-in-xamarin
    static readonly SemaphoreSlim sem = new SemaphoreSlim(1, 1); // MZ: check

    const int MAX_RETRIES = 4;
    const int THROWAWAY_SPECTRA = 6;

    uint totalPixelsToRead;
    uint totalPixelsRead;

    public delegate void ConnectionProgressNotification(double perc);
    public event ConnectionProgressNotification showConnectionProgress;

    public delegate void AcquisitionProgressNotification(double perc);
    public event AcquisitionProgressNotification showAcquisitionProgress;

    Logger logger = Logger.getInstance();

    ////////////////////////////////////////////////////////////////////////
    // Lifecycle 
    ////////////////////////////////////////////////////////////////////////

    static public Spectrometer getInstance()
    {
        if (instance is null)
            instance = new Spectrometer();
        return instance;
    }

    Spectrometer()
    {
        reset();
    }

    public void disconnect()
    {
        logger.debug("Spectrometer.disconnect: start");
        laserEnabled = false;
        ramanModeEnabled = false;
        reset();
        logger.debug("Spectrometer.disconnect: done");
    }

    public void reset()
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

    void generatePixelAxis()
    {
        xAxisPixels = new double[pixels];
        for (int i = 0; i < pixels; i++)
            xAxisPixels[i] = i;
    }

    public bool paired
    {
        get => _paired;
        set
        {
            _paired = value;
            logger.debug($"Spectrometer.paired -> {value}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(paired)));
        }
    }
    bool _paired;

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

        if (!eeprom.parse(pages))
        {
            logger.error("Spectrometer.initAsync: failed to parse EEPROM");
            return false;
        }

        ////////////////////////////////////////////////////////////////////
        // post-process EEPROM
        ////////////////////////////////////////////////////////////////////

        pixels = eeprom.activePixelsHoriz;
        laserExcitationNM = eeprom.laserExcitationWavelengthNMFloat;

        logger.debug("computing wavecal");
        wavelengths = Util.generateWavelengths(pixels, eeprom.wavecalCoeffs);

        if (laserExcitationNM > 0)
            wavenumbers = Util.wavelengthsToWavenumbers(laserExcitationNM, wavelengths);
        else
            wavenumbers = null;

        generatePixelAxis();

        // set this early so battery and other BLE calls can progress
        paired = true;

        ////////////////////////////////////////////////////////////////////
        // finish initializing Spectrometer 
        ////////////////////////////////////////////////////////////////////

        showConnectionProgress(1);
        
        logger.debug("finishing spectrometer initialization");
        pixels = eeprom.activePixelsHoriz;

        await updateBatteryAsync(); 
        integrationTimeMS = (ushort)(eeprom.startupIntegrationTimeMS > 0 && eeprom.startupIntegrationTimeMS < 5000 ? eeprom.startupIntegrationTimeMS : 3);
        gainDb = eeprom.detectorGain;

        verticalROIStartLine = eeprom.ROIVertRegionStart[0];
        verticalROIStopLine = eeprom.ROIVertRegionEnd[0];

        logger.info($"initialized {eeprom.model} {eeprom.serialNumber}");
        logger.info($"  detector: {eeprom.detectorName}");
        logger.info($"  pixels: {pixels}");
        logger.info($"  verticalROI: ({verticalROIStartLine}, {verticalROIStopLine})");
        logger.info( "  excitation: {0:f2}nm", laserExcitationNM);
        logger.info( "  wavelengths: ({0:f2}, {1:f2})", wavelengths[0], wavelengths[pixels-1]);
        if (wavenumbers != null)
            logger.info("  wavenumbers: ({0:f2}, {1:f2})", wavenumbers[0], wavenumbers[pixels-1]);

        // I'm honestly not sure where we should initialize location, but it 
        // should probably happen after we've successfully connected to a
        // spectrometer and are ready to take measurements.  Significantly,
        // at this point we know the user has already granted location privs.
        //
        // whereAmI = WhereAmI.getInstance();

        return true;
    }

    async Task<List<byte[]>> readEEPROMAsync()
    {
        logger.info("Attempting to read EEPROM data.");
        Plugin.BLE.Abstractions.Contracts.ICharacteristic eepromCmd;
        Plugin.BLE.Abstractions.Contracts.ICharacteristic eepromData;

        if (characteristicsByName.ContainsKey("eepromCmd") && characteristicsByName.ContainsKey("eepromData"))
        {
            eepromCmd = characteristicsByName["eepromCmd"];
            eepromData = characteristicsByName["eepromData"];
        }
        else
        {
            logger.error("Can't read EEPROM w/o characteristics");                
            return null;
        }
        logger.debug("reading EEPROM");
        List<byte[]> pages = new List<byte[]>();
        for (int page = 0; page < EEPROM.MAX_PAGES; page++)
        {
            byte[] buf = new byte[EEPROM.PAGE_LENGTH];
            int pos = 0;
            for (int subpage = 0; subpage < EEPROM.SUBPAGE_COUNT; subpage++)
            {
                byte[] request = ToBLEData.convert((byte)page, (byte)subpage);
                logger.debug($"requestEEPROMSubpage: page {page}, subpage {subpage}");
                bool ok = 0 == await eepromCmd.WriteAsync(request);
                if (!ok)
                {
                    logger.error($"Failed to write eepromCmd({page}, {subpage})");
                    return null;
                } 

                try
                {
                    logger.debug("reading eepromData");
                    var response = await eepromData.ReadAsync();
                    logger.hexdump(response.data, "response");
                    logger.info($"The length of buf is {buf.Length} and lenght of response is {response.data.Length}");

                    for (int i = 0; i < response.data.Length; i++)
                        buf[pos++] = response.data[i];
                }
                catch(Exception ex)
                {
                    logger.error($"Caught exception when trying to read EEPROM characteristic: {ex}");
                    return null;
                }
            }
            logger.hexdump(buf, "adding page: ");
            pages.Add(buf);
            showConnectionProgress(.15 + .85 * page / EEPROM.MAX_PAGES);
        }
        return pages;
    }

    ////////////////////////////////////////////////////////////////////////
    // acquiring
    ////////////////////////////////////////////////////////////////////////

    public bool acquiring
    {
        get => _acquiring;
        set
        {
            _acquiring = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(acquiring)));
        }
    }
    bool _acquiring;

    ////////////////////////////////////////////////////////////////////////
    // scansToAverage
    ////////////////////////////////////////////////////////////////////////

    public uint scansToAverage 
    { 
        get => _scansToAverage;
        set
        {
            if (value > 0)
            {
                logger.debug($"Spectrometer.scansToAverage -> {value}");
                _scansToAverage = value;
            }
        }
    }
    uint _scansToAverage = 1;

    ////////////////////////////////////////////////////////////////////////
    // integrationTimeMS
    ////////////////////////////////////////////////////////////////////////

    public uint integrationTimeMS
    {
        get => _nextIntegrationTimeMS;
        set 
        { 
            _nextIntegrationTimeMS = value;
            logger.debug($"Spectrometer.integrationTimeMS: next = {value}");
            _ = syncIntegrationTimeMSAsync();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(integrationTimeMS)));
        }
    }
    uint _nextIntegrationTimeMS = 3;
    uint _lastIntegrationTimeMS = 9999;

    async Task<bool> syncIntegrationTimeMSAsync()
    {
        if (!paired || characteristicsByName is null)
            return false;

        if (_nextIntegrationTimeMS == _lastIntegrationTimeMS)
            return true;

        var characteristic = characteristicsByName["integrationTimeMS"];
        if (characteristic is null)
        {
            logger.error("can't find integrationTimeMS characteristic");
            return false;
        }

        ushort value = Math.Min((ushort)5000, Math.Max((ushort)3, (ushort)Math.Round((decimal)_nextIntegrationTimeMS)));
        byte[] request = ToBLEData.convert(value, len: 4);

        logger.info($"Spectrometer.syncIntegrationTimeMSAsync({value})");
        logger.hexdump(request, "data: ");

        var ok = 0 == await characteristic.WriteAsync(request);
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

    public float gainDb
    {
        get => _nextGainDb;
        set 
        { 
            if (0 <= value && value <= 72)
            {
                _nextGainDb = value;
                logger.debug($"Spectrometer.gainDb: next = {value}");
                _ = syncGainDbAsync();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(gainDb)));
            }
            else
            {
                logger.error($"ignoring out-of-range gainDb {value}");
            }
        }
    }
    float _nextGainDb = 24.0f;
    float _lastGainDb = 99.0f;

    async Task<bool> syncGainDbAsync()
    {
        if (!paired || characteristicsByName is null)
            return false;

        if (_nextGainDb == _lastGainDb)
            return true;
                        
        var characteristic = characteristicsByName["gainDb"];
        if (characteristic is null)
        {
            logger.error("gainDb characteristic not found");
            return false;
        }

        byte msb = (byte)Math.Floor(_nextGainDb);
        byte lsb = (byte)(((byte)Math.Round( (_nextGainDb - msb) * 256.0)) & 0xff);

        ushort value = (ushort)((msb << 8) | lsb);
        ushort len = 2;

        byte[] request = ToBLEData.convert(value, len: len);

        logger.debug($"converting gain {_nextGainDb:f4} to msb 0x{msb:x2}, lsb 0x{lsb:x2}, value 0x{value:x4}, request {request}");

        logger.info($"Spectrometer.syncGainDbAsync({_nextGainDb})"); 
        logger.hexdump(request, "data: ");

        var ok = 0 == await characteristic.WriteAsync(request);
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

    public ushort verticalROIStartLine
    {
        get => _nextVerticalROIStartLine;
        set 
        { 
            if (value > 0 && value < eeprom.activePixelsVert)
            {
                _nextVerticalROIStartLine = value;
                logger.debug($"Spectrometer.verticalROIStartLine -> {value}");
                _ = syncROIAsync();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(verticalROIStartLine)));
            }
            else
            {
                logger.error($"ignoring out-of-range start line {value}");
            }
        }
    }
    ushort _nextVerticalROIStartLine = 200;
    ushort _lastVerticalROIStartLine = 0;

    public ushort verticalROIStopLine
    {
        get => _nextVerticalROIStopLine;
        set 
        { 
            if (value > 0 && value < eeprom.activePixelsVert)
            {
                _nextVerticalROIStopLine = value;
                logger.debug($"Spectrometer.verticalROIStopLine -> {value}");
                _ = syncROIAsync();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(verticalROIStopLine)));
            }
            else
            {
                logger.error($"ignoring out-of-range stop line {value}");
            }
        }
    }
    ushort _nextVerticalROIStopLine = 800;
    ushort _lastVerticalROIStopLine = 0;

    async Task<bool> syncROIAsync()
    {
        if (!paired || characteristicsByName is null)
            return false;

        var characteristic = characteristicsByName["roi"];
        if (characteristic is null)
        {
            logger.error("ROI characteristic not found");
            return false;
        }

        // noop
        if (_nextVerticalROIStartLine == _lastVerticalROIStartLine &&
            _nextVerticalROIStopLine == _lastVerticalROIStopLine)
            return false;

        // force ordering
        var start = verticalROIStartLine;
        var stop = verticalROIStopLine;
        if (stop < start)
            Util.swap(ref start, ref stop);

        byte[] startData = ToBLEData.convert(start, len: 2);
        byte[] stopData = ToBLEData.convert(stop, len: 2);
        byte[] request = new byte[4];
        Array.Copy(startData, request, 2);
        Array.Copy(stopData, 0, request, 2, 2);

        logger.info($"Spectrometer.syncROIAsync({verticalROIStartLine}, {verticalROIStopLine})"); 
        logger.hexdump(request, "data: ");

        var ok = 0 == await characteristic.WriteAsync(request);
        if (ok)
        {
            _lastVerticalROIStartLine = _nextVerticalROIStartLine;
            _lastVerticalROIStopLine = _nextVerticalROIStopLine;
            await pauseAsync("syncROIAsync");
        }
        else
            logger.error($"Failed to set ROI ({verticalROIStartLine}, {verticalROIStopLine})");

        return ok;
    }

    ////////////////////////////////////////////////////////////////////////
    // laserState
    ////////////////////////////////////////////////////////////////////////

    LaserState laserState = new LaserState();

    public bool ramanModeEnabled
    {
        get => laserState.mode == LaserMode.RAMAN;
        set
        {
            var mode = value ? LaserMode.RAMAN : LaserMode.MANUAL;
            if (laserState.mode != mode)
            { 
                logger.debug($"Spectrometer.ramanModeEnabled: laserState.mode -> {mode}");
                laserState.mode = mode;
                laserState.enabled = false;
                _ = syncLaserStateAsync();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ramanModeEnabled)));
            }
            else
                logger.debug($"Spectrometer.ramanModeEnabled: mode already {mode}");
        }
    }

    public byte laserWatchdogSec
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

    public bool laserEnabled
    {
        get => laserState.enabled;
        set
        {
            if (laserState.enabled != value)
            {
                laserState.enabled = value;
                _ = syncLaserStateAsync();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserEnabled)));
            }
            else
                logger.debug($"Spectrometer.laserEnabled: already {value}");
        }
    }

    public ushort laserDelayMS
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

    bool laserSyncEnabled = true;

    async Task<bool> syncLaserStateAsync()
    {
        logger.debug("syncLaserStateAsync: start");
        if (!laserSyncEnabled)
        {
            logger.debug("syncLaserState: skipping");
            return false;
        }

        laserState.dump();

        if (!paired || characteristicsByName is null)
            return false;

        ICharacteristic characteristic;
        characteristicsByName.TryGetValue("laserState", out characteristic);
        if (characteristic is null)
        {
            logger.error("laserState characteristic not found");
            return false;
        }

        byte[] request = laserState.serialize();
        logger.hexdump(request, "Spectrometer.syncLaserStateAsync: ");

        var ok = 0 == await characteristic.WriteAsync(request);
        if (ok)
            await pauseAsync("syncLaserStateAsync");
        else
            logger.error($"Failed to set laserState");

        return ok;
    }

    ////////////////////////////////////////////////////////////////////////
    // battery
    ////////////////////////////////////////////////////////////////////////

    // I used to call this at the END of an acquisition, and that worked; 
    // until it didn't.  Now I call it BEFORE each acquisition, and that
    // seems to work better?
    async Task<bool> updateBatteryAsync()
    {
        logger.debug("updateBatteryAsync: starting");

        if (!battery.isExpired)
        {
            logger.debug("battery state still valid, skipping");
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
            logger.error("batteryUpdateAsync: can't find characteristic batteryStatus");
            return false;
        }

        logger.debug("updateBatteryAsync: waiting on semaphore");
        if (!await sem.WaitAsync(50))
        {
            logger.error("updateBatteryAsync: couldn't get semaphore");
            return false;
        }

        logger.info("batteryUpdateAsync: reading battery status");
        var response = await characteristic.ReadAsync();
        if (response.data is null)
        {
            logger.error("batteryUpdateAsync: failed reading battery");
            sem.Release();
            return false;
        }
        logger.hexdump(response.data, "batteryStatus: ");
        battery.parse(response.data);
        await pauseAsync("updateBatteryAsync");

        logger.debug("updateBatteryAsync: sending batteryStatus notification");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("batteryStatus"));

        logger.debug("updateBatteryAsync: done");
        sem.Release();
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // dark
    ////////////////////////////////////////////////////////////////////////

    public void toggleDark()
    {
        if (dark is null)
            dark = lastSpectrum; 
        else
            dark = null;
        logger.debug("Spectrometer.dark -> {0}", dark is null);
    }

    ////////////////////////////////////////////////////////////////////////
    // spectra
    ////////////////////////////////////////////////////////////////////////

    // responsible for taking one fully-averaged measurement
    public async Task<bool> takeOneAveragedAsync()
    {
        if (!paired || characteristicsByName is null)
            return false;

        // push-down any changed acquisition parameters
        logger.debug("take one average reading: syncing integration time");
        if (! await syncIntegrationTimeMSAsync())
            return false;
        logger.debug("take one averaged reading: syncing gain");
        if (! await syncGainDbAsync())
            return false;

        // update battery FIRST
        logger.debug("take one averaged reading: updating battery");
        await updateBatteryAsync();

        // for progress bar
        totalPixelsToRead = pixels * scansToAverage;
        totalPixelsRead = 0;
        acquiring = true;

        // TODO: integrate laserDelayMS into showProgress
        var swRamanMode = laserState.mode == LaserMode.RAMAN && LaserState.SW_RAMAN_MODE;
        if (swRamanMode)
        {
            const int MAX_SPECTRUM_READOUT_TIME_MS = 6000;
            var watchdogMS = (scansToAverage + 1) * (integrationTimeMS + MAX_SPECTRUM_READOUT_TIME_MS);
            var watchdogSec = (byte)((Math.Max(MAX_SPECTRUM_READOUT_TIME_MS, watchdogMS) / 1000.0) * 2);
            logger.debug($"takeOneAveragedAsync: setting laserWatchdogSec -> {watchdogSec}");

            // since we're going to sync the laser state immediately after to turn on the laser,
            // skip this sync
            laserSyncEnabled = false;
            laserWatchdogSec = watchdogSec;
            laserSyncEnabled = true;

            logger.debug("takeOneAveragedAsync: setting laserEnabled = true");
            laserEnabled = true;

            logger.debug($"takeOneAveragedAsync: waiting {laserState.laserDelayMS}ms");
            await Task.Delay(laserState.laserDelayMS);
        }

        logger.debug($"takeOneAveragedAsync: integrationTimeMS {integrationTimeMS}, gainDb {gainDb}, scansToAverage {scansToAverage}, laserWatchdogSec {laserWatchdogSec}");

        double[] spectrum = null;
        for (int spectrumCount = 0; spectrumCount < scansToAverage; spectrumCount++)
        { 
            bool disableLaserAfterFirstPacket = swRamanMode && spectrumCount + 1 == scansToAverage;

            if (!await sem.WaitAsync(100))
            {
                logger.error("takeOneAveragedAsync: couldn't get semaphore");
                return false;                        
            }

            double[] tmp = await takeOneAsync(disableLaserAfterFirstPacket);
            logger.debug("takeOneAveragedAsync: back from takeOneAsync");

            sem.Release();

            if (tmp is null || (spectrum != null && tmp.Length != spectrum.Length))
            {
                if (tmp is null)
                    logger.error("takeOneAveragedAsnc: tmp is null");
                else if (spectrum != null && tmp.Length != spectrum.Length)
                    logger.error($"takeOneAveragedAsnc: length changed ({tmp.Length} != {spectrum.Length})");

                if (swRamanMode)
                    laserEnabled = false;

                logger.error("takeOneAveragedAsnc: giving up");
                return acquiring = false;
            }

            if (spectrum is null)
                spectrum = tmp;
            else
                for (int i = 0; i < spectrum.Length; i++)
                    spectrum[i] += tmp[i];    
        }

        if (scansToAverage > 1)
            for (int i = 0; i < spectrum.Length; i++)
                spectrum[i] /= scansToAverage;

        lastSpectrum = spectrum;
        measurement.reset();
        measurement.reload(this);
        logger.info($"acquired Measurement {measurement.measurementID}");

        logger.debug("takeOneAveragedAsync: done");
        acquiring = false;
        return true;
    }

    // Take one spectrum (of many, if doing scan averaging).  This is private,
    // callers are expected to use takeOneAveragedAsync().
    // 
    // There is no need to disable the laser if returning NULL, as the caller
    // will do so anyway.
    async Task<double[]> takeOneAsync(bool disableLaserAfterFirstPacket)
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

        var spectrumRequestChar = characteristicsByName["spectrumRequest"];
        if (spectrumRequestChar is null)
        {
            logger.error("can't find characteristic spectrumRequest");
            return null;
        }

        var spectrumChar = characteristicsByName["readSpectrum"];
        if (spectrumChar is null)
        {
            logger.error("can't find characteristic spectrum");
            return null;
        }

        // send acquire command
        logger.debug("takeOneAsync: sending SPECTRUM_ACQUIRE");
        byte[] request = ToBLEData.convert(true);
        if (0 != await acquireChar.WriteAsync(request))
        {
            logger.error("failed to send acquire");
            return null;
        }

        // wait for acquisition to complete
        logger.debug($"takeOneAsync: waiting {integrationTimeMS}ms");
        await Task.Delay((int)integrationTimeMS);

        var spectrum = new double[pixels];
        UInt16 pixelsRead = 0;
        var retryCount = 0;
        bool requestRetry = false;
        bool haveDisabledLaser = false;

        while (pixelsRead < pixels)
        {
            if (requestRetry)
            {
                retryCount++;
                if (retryCount > MAX_RETRIES)
                {
                    logger.error($"giving up after {MAX_RETRIES} retries");
                    return null;
                }

                int delayMS = (int)Math.Pow(5, retryCount);

                // if this is the first retry, assume that the sensor was
                // powered-down, and we need to wait for some throwaway
                // spectra 
                if (retryCount == 1)
                    delayMS = (int)(integrationTimeMS * THROWAWAY_SPECTRA);

                logger.error($"Retry requested, so waiting for {delayMS}ms");
                await Task.Delay(delayMS);

                requestRetry = false;
            }

            logger.debug($"takeOneAsync: requesting spectrum packet starting at pixel {pixelsRead}");
            request = ToBLEData.convert(pixelsRead, len: 2);
            if (0 != await spectrumRequestChar.WriteAsync(request))
            {
                logger.error($"failed to write spectrum request for pixel {pixelsRead}");
                return null;
            }

            logger.debug($"reading spectrumChar (pixelsRead {pixelsRead})");
            var response = await spectrumChar.ReadAsync();

            // make sure response length is even, and has both header and at least one pixel of data
            var responseLen = response.data.Length;
            if (responseLen < headerLen || responseLen % 2 != 0)
            {
                logger.error($"received invalid response of {responseLen} bytes");
                requestRetry = true;
                continue;
            }

            // firstPixel is a big-endian UInt16
            short firstPixel = (short)((response.data[0] << 8) | response.data[1]);
            if (firstPixel > 2048 || firstPixel < 0)
            {
                logger.error($"received NACK (firstPixel {firstPixel}, retrying");
                requestRetry = true;
                continue;
            }

            var pixelsInPacket = (responseLen - headerLen) / 2;

            logger.debug($"received spectrum packet starting at pixel {firstPixel} with {pixelsInPacket} pixels");
            // logger.hexdump(response);

            var crc = Crc16.checksum(response.data);
            if (crc == lastCRC)
            {
                logger.error($"received duplicate CRC 0x{crc:x4}, retrying");
                requestRetry = true;
                continue;
            }

            lastCRC = crc;

            for (int i = 0; i < pixelsInPacket; i++)
            {
                // pixel intensities are little-endian UInt16
                var offset = headerLen + i * 2;
                ushort intensity = (ushort)((response.data[offset+1] << 8) | response.data[offset]);
                spectrum[pixelsRead] = intensity;

                pixelsRead++;
                totalPixelsRead++;

                if (pixelsRead == pixels)
                {
                    logger.debug("read complete spectrum");
                    if (i + 1 != pixelsInPacket)
                        logger.error($"ignoring {pixelsInPacket - (i + 1)} trailing pixels");
                    break;
                }
            }
            // response = null;

            showAcquisitionProgress(((double)totalPixelsRead) / totalPixelsToRead);
        }

        // YOU ARE HERE: kludge at end
        if (disableLaserAfterFirstPacket && !haveDisabledLaser)
        {
            logger.debug("disabling laser after complete spectrum received");
            laserEnabled = false;
            logger.debug("continuing end-of-spectrum processing after triggering laser disable");
        }

        // kludge: first four pixels are zero, so overwrite from 5th
        for (int i = 0; i < 4; i++)
            spectrum[i] = spectrum[4];

        // kludge: last pixel seems to be 0xff, so re-write from previous
        spectrum[pixels-1] = spectrum[pixels-2];

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

    ////////////////////////////////////////////////////////////////////////
    // BLE Characteristic Notifications (routed via BluetoothViewModel)
    ////////////////////////////////////////////////////////////////////////

    public event PropertyChangedEventHandler PropertyChanged;

    public void processBatteryNotification(byte[] data)
    {
        // we don't have to call updateBatteryAsync, because we get the
        // value right along with the notification
        if (data is null)
            return;

        logger.hexdump(data, "Spectrometer.processBatteryNotification: ");
        battery.parse(data);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("batteryStatus"));
    }

    async public void processLaserStateNotificationAsync(byte[] data)
    {
        if (data is null)
            return;

        // this time-out may well not be nearly enough, given the potential 
        // need to wait 6 x integration time for sensor to wake up, plus 4sec
        // for read-out
        if (!await sem.WaitAsync(100))
        {
            logger.error("Spectrometer.processLaserStateNotification: timed-out");
            return;
        }

        logger.hexdump(data, "Spectrometer.processLaserStateNotification: ");
        laserState.parse(data);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("laserState"));

        sem.Release();
    }

    // I'm never sure if this is needed or not
    async Task<bool> pauseAsync(string caller)
    {
        const int DELAY_MS = 10;
        logger.debug($"pauseAsync({caller}): waiting {DELAY_MS} ms");
        await Task.Delay(DELAY_MS);
        GC.Collect();
        return true;
    }
}
