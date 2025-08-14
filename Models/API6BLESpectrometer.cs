using System.ComponentModel;
using Plugin.BLE.Abstractions.Contracts;

using EnlightenMAUI.Common;
using EnlightenMAUI.Platforms;
using static Android.Widget.GridLayout;
using Java.Util.Functions;

namespace EnlightenMAUI.Models;

// This more-or-less corresponds to WasatchNET.Spectrometer, or 
// SiGDemo.Spectrometer.  Spectrometer state and logic should be 
// encapsulated here.
public class API6BLESpectrometer : Spectrometer
{
    const int BLE_SUCCESS = 0; // result of Characteristic.WriteAsync

    // Singleton
    static API6BLESpectrometer instance = null;

    // BLE comms
    Dictionary<string, ICharacteristic> characteristicsByName;
    // WhereAmI whereAmI;

    public BLEDevice bleDevice = null;

    ushort lastCRC;

    const int MAX_RETRIES = 5;
    const int THROWAWAY_SPECTRA = 9;

    uint totalPixelsToRead;
    uint totalPixelsRead;

    ////////////////////////////////////////////////////////////////////////
    // Lifecycle 
    ////////////////////////////////////////////////////////////////////////

    static public API6BLESpectrometer getInstance()
    {
        if (instance is null)
            instance = new API6BLESpectrometer();
        return instance;
    }

    API6BLESpectrometer()
    {
        reset();
    }

    public override void disconnect()
    {
        logger.debug("Spectrometer.disconnect: start");
        laserEnabled = false;
        autoDarkEnabled = false;
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
        originalWavenumbers = Util.wavelengthsToWavenumbers(laserExcitationNM, wavelengths);
        wavenumbers = new double[originalWavenumbers.Length];
        Array.Copy(originalWavenumbers, wavenumbers, originalWavenumbers.Length);
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
        {
            originalWavenumbers = Util.wavelengthsToWavenumbers(laserExcitationNM, wavelengths);
            wavenumbers = new double[originalWavenumbers.Length];
            Array.Copy(originalWavenumbers, wavenumbers, originalWavenumbers.Length);
        }
        else
            wavenumbers = originalWavenumbers = null;

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

        // ignore EEPROM configuration and hardcode int time and gain. Our preferred defaults here
        // are different than those written to EEPROM and since there is strong data binding between the
        // UI and the spectro we have to use static values rather than those in EEPROM 
        // integrationTimeMS = (ushort)(eeprom.startupIntegrationTimeMS > 0 && eeprom.startupIntegrationTimeMS < 5000 ? eeprom.startupIntegrationTimeMS : 400);
        // gainDb = eeprom.detectorGain;
        integrationTimeMS = 400;
        gainDb = 8;

        verticalROIStartLine = eeprom.ROIVertRegionStart[0];
        verticalROIStopLine = eeprom.ROIVertRegionEnd[0];

        logger.info($"initialized {eeprom.serialNumber} {fullModelName}");
        logger.info($"  detector: {eeprom.detectorName}");
        logger.info($"  pixels: {pixels}");
        logger.info($"  integrationTimeMS: {integrationTimeMS}");
        logger.info($"  gainDb: {gainDb}");
        // logger.info($"  verticalROI: ({verticalROIStartLine}, {verticalROIStopLine})");
        logger.info($"  excitation: {laserExcitationNM:f3}nm");
        logger.info($"  wavelengths: ({wavelengths[0]:f2}, {wavelengths[pixels - 1]:f2})");
        if (wavenumbers != null)
            logger.info($"  wavenumbers: ({wavenumbers[0]:f2}, {wavenumbers[pixels - 1]:f2})");

        // I'm honestly not sure where we should initialize location, but it 
        // should probably happen after we've successfully connected to a
        // spectrometer and are ready to take measurements.  Significantly,
        // at this point we know the user has already granted location privs.
        //
        // whereAmI = WhereAmI.getInstance();

        logger.debug("Spectrometer.initAsync: done");
        return true;
    }
    internal override async Task<bool> initializeCollectionParams()
    {
        return true;
    }

    protected override async Task<List<byte[]>> readEEPROMAsync()
    {
        logger.info("reading EEPROM");
        ICharacteristic eepromCmd;
        ICharacteristic eepromData;

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
        logger.debug("Spectrometer.readEEPROMAsync: reading EEPROM");
        List<byte[]> pages = new List<byte[]>();
        for (int page = 0; page < EEPROM.MAX_PAGES; page++)
        {
            byte[] buf = new byte[EEPROM.PAGE_LENGTH];
            int pos = 0;
            for (int subpage = 0; subpage < EEPROM.API6_SUBPAGE_COUNT; subpage++)
            {
                byte[] request = ToBLEData.convert((byte)page, (byte)subpage);
                logger.debug($"Spectrometer.readEEPROMAsync: requestEEPROMSubpage: page {page}, subpage {subpage}");
                bool ok = 0 == await eepromCmd.WriteAsync(request);
                if (!ok)
                {
                    logger.error($"Spectrometer.readEEPROMAsync: failed to write eepromCmd({page}, {subpage})");
                    return null;
                }

                try
                {
                    logger.debug($"Spectrometer.readEEPROMAsync: reading eepromData");
                    var response = await eepromData.ReadAsync();
                    logger.hexdump(response.data, "response: ");
                    logger.info($"The length of buf is {buf.Length} and length of response is {response.data.Length}");

                    for (int i = 0; i < response.data.Length; i++)
                        buf[pos++] = response.data[i];
                }
                catch (Exception ex)
                {
                    logger.error($"Caught exception when trying to read EEPROM characteristic: {ex}");
                    return null;
                }
            }
            logger.hexdump(buf, $"adding page {page}: ");
            pages.Add(buf);
            raiseConnectionProgress(.15 + .85 * page / EEPROM.MAX_PAGES);
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

        var characteristic = characteristicsByName["integrationTimeMS"];
        if (characteristic is null)
        {
            logger.error("can't find integrationTimeMS characteristic");
            return false;
        }

        ushort value = Math.Min((ushort)5000, Math.Max((ushort)1, (ushort)Math.Round((decimal)_nextIntegrationTimeMS)));
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

        var characteristic = characteristicsByName["gainDb"];
        if (characteristic is null)
        {
            logger.error("gainDb characteristic not found");
            return false;
        }

        byte msb = (byte)Math.Floor(_nextGainDb);
        byte lsb = (byte)(((byte)Math.Round((_nextGainDb - msb) * 256.0)) & 0xff);

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

    protected override void processGeneric(byte[] data)
    {
        throw new NotImplementedException();
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
    // genericCharacteristic
    ////////////////////////////////////////////////////////////////////////

    byte genericSequence = 0;

    private async Task<bool> writeGenericCharacteristic(byte[] data)
    {
        if (!paired || characteristicsByName is null)
        {
            logger.error("writeGenericCharacteristic: not paired or no characteristics");
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
    internal override async Task<bool> updateBatteryAsync(bool extendedTimeout = false)
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
        battery.parseAPI6(response.data);
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
        if (!await syncIntegrationTimeMSAsync())
            return false;

        logger.debug("Spectrometer.takeOneAveragedAsync: syncing gain");
        if (!await syncGainDbAsync())
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

            double[] smoothed = PlatformUtil.ProcessBackground(wavenumbers, spectrum, eeprom.serialNumber, eeprom.avgResolution, eeprom.ROIHorizStart);
            measurement.wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
            stretchedDark = new double[smoothed.Length];
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
    protected override async Task<double[]> takeOneAsync(bool disableLaserAfterFirstPacket, bool extendedTimeout = false)
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
        byte[] request = ToBLEData.convert(autoDarkEnabled);
        if (0 != await acquireChar.WriteAsync(request))
        {
            logger.error("failed to send acquire");
            return null;
        }

        // wait for acquisition to complete
        logger.debug($"takeOneAsync: waiting {integrationTimeMS}ms");

        int waitTime = (int)integrationTimeMS;
        if (laserState.mode == LaserMode.AUTO_DARK)
            waitTime = 2 * (int)integrationTimeMS * scansToAverage + (int)laserWarningDelaySec * 1000 + (int)eeprom.laserWarmupSec * 1000;

        await Task.Delay(waitTime);

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

            if (responseLen == 3)
            {
                if (response.data[2] != 0)
                {
                    logger.error("attempted spectrum read returned error code 0x{0:x2},0x{1:x2},0x{2:x2}", response.data[0], response.data[1], response.data[2]);
                    return null;
                }
                else
                {
                    requestRetry = true;
                    continue;
                }

            }
            else if (responseLen < headerLen || responseLen % 2 != 0)
            {
                logger.error($"received invalid response of {responseLen} bytes");
                requestRetry = true;
                continue;
            }

            // firstPixel is a big-endian UInt16
            short firstPixel = (short)((response.data[0] << 8) | response.data[1]);
            if (firstPixel > 2048 || firstPixel < 0)
            {
                logger.error($"received NACK (firstPixel {firstPixel}, retrying)");
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
                ushort intensity = (ushort)((response.data[offset + 1] << 8) | response.data[offset]);
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

            raiseAcquisitionProgress(((double)totalPixelsRead) / totalPixelsToRead);
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
        spectrum[pixels - 1] = spectrum[pixels - 2];

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

}