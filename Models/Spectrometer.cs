using Accord;
using EnlightenMAUI.Common;
using EnlightenMAUI.ViewModels;
using Plugin.BLE.Abstractions.Contracts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static Android.Provider.ContactsContract.CommonDataKinds;
using static Android.Telephony.CarrierConfigManager;

namespace EnlightenMAUI.Models
{
    public enum AcquisitionMode { STANDARD=0, AUTO_DARK=1, AUTO_RAMAN=2 };

    public abstract class Spectrometer : INotifyPropertyChanged
    {
        public static EventHandler<Spectrometer> NewConnection;
        protected Logger logger = Logger.getInstance();
        public BLEDeviceInfo bleDeviceInfo = new BLEDeviceInfo();
        // @see https://forums.xamarin.com/discussion/93330/mutex-is-bugged-in-xamarin
        protected static readonly SemaphoreSlim sem = new SemaphoreSlim(1, 1);

        // hardware model
        public uint pixels;
        public float laserExcitationNM;
        public EEPROM eeprom = EEPROM.getInstance();
        public Battery battery;
        public AcquisitionMode acquisitionMode = AcquisitionMode.AUTO_RAMAN;
        public Opcodes lastRequest;

        ////////////////////////////////////////////////////////////////////////
        // laserState
        ////////////////////////////////////////////////////////////////////////
        protected LaserState laserState = new LaserState();

        // software state
        public double[] wavelengths;
        public double[] originalWavenumbers;
        public double[] wavenumbers { get; set; }
        public double[] xAxisPixels;

        public double[] lastRaw;
        public double[] lastSpectrum;
        public double[] dark;
        public double[] stretchedDark;

        public Measurement measurement;

        protected bool EEPROMReadComplete = false;
        protected uint EEPROMBytesRead = 0;
        protected uint CurrentEEPROMPage = 0;
        protected byte[] EEPROMBuffer = new byte[EEPROM.PAGE_LENGTH * EEPROM.MAX_PAGES];
        protected bool genericReturned = false;

        public Spectrometer() { }

        public delegate void ConnectionProgressNotification(double perc);
        public event ConnectionProgressNotification showConnectionProgress;

        public delegate void AcquisitionProgressNotification(double perc);
        public event AcquisitionProgressNotification showAcquisitionProgress;

        public abstract void disconnect();

        public abstract void reset();

        public void raiseConnectionProgress(double arg)
        {
            showConnectionProgress(arg);
        }
        public void raiseAcquisitionProgress(double arg)
        {
            showAcquisitionProgress(arg);
        }

        protected void generatePixelAxis()
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

        public string fullModelName { get => $"{eeprom.model}{eeprom.productConfiguration}"; }

        protected abstract Task<List<byte[]>> readEEPROMAsync();

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

        public virtual byte scansToAverage
        {
            get => _scansToAverage;
            set
            {
                _scansToAverage = value;
            }
        }
        byte _scansToAverage = 1;

        ////////////////////////////////////////////////////////////////////////
        // integrationTimeMS
        ////////////////////////////////////////////////////////////////////////

        public virtual uint integrationTimeMS
        {
            get => _nextIntegrationTimeMS;
            set
            {
                _nextIntegrationTimeMS = value;
                logger.debug($"Spectrometer.integrationTimeMS: next = {value}");
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(integrationTimeMS)));
            }
        }
        protected uint _nextIntegrationTimeMS = 3;
        protected uint _lastIntegrationTimeMS = 9999;

        ////////////////////////////////////////////////////////////////////////
        // gainDb
        ////////////////////////////////////////////////////////////////////////

        // for documentation on the unsigned bfloat16 datatype used by gain, see
        // https://github.com/WasatchPhotonics/Wasatch.NET/blob/master/WasatchNET/FunkyFloat.cs

        public virtual float gainDb
        {
            get => _nextGainDb;
            set
            {
                if (0 <= value && value <= 72)
                {
                    _nextGainDb = value;
                    logger.debug($"Spectrometer.gainDb: next = {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(gainDb)));
                }
                else
                {
                    logger.error($"ignoring out-of-range gainDb {value}");
                }
            }
        }
        protected float _nextGainDb = 24;
        protected float _lastGainDb = -1;

        ////////////////////////////////////////////////////////////////////////
        // Vertical ROI Start/Stop
        ////////////////////////////////////////////////////////////////////////

        public virtual ushort verticalROIStartLine
        {
            get => _nextVerticalROIStartLine;
            set 
            { 
                if (value > 0 && value < eeprom.activePixelsVert)
                {
                    _nextVerticalROIStartLine = value;
                    logger.debug($"Spectrometer.verticalROIStartLine -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(verticalROIStartLine)));
                }
                else
                {
                    logger.error($"ignoring out-of-range start line {value}");
                }
            }
        }
        protected ushort _nextVerticalROIStartLine = 200;
        protected ushort _lastVerticalROIStartLine = 0;

        public virtual ushort verticalROIStopLine
        {
            get => _nextVerticalROIStopLine;
            set 
            { 
                if (value > 0 && value < eeprom.activePixelsVert)
                {
                    _nextVerticalROIStopLine = value;
                    logger.debug($"Spectrometer.verticalROIStopLine -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(verticalROIStopLine)));
                }
                else
                {
                    logger.error($"ignoring out-of-range stop line {value}");
                }
            }
        }
        protected ushort _nextVerticalROIStopLine = 800;
        protected ushort _lastVerticalROIStopLine = 0;

        ////////////////////////////////////////////////////////////////////////
        // laserWarningDelaySec
        ////////////////////////////////////////////////////////////////////////

        public virtual byte laserWarningDelaySec
        {
            get => _laserWarningDelaySec;
            set
            {
                _laserWarningDelaySec = value;
            }
        }
        protected byte _laserWarningDelaySec = 3;

        public virtual bool autoDarkEnabled
        {
            get => acquisitionMode == AcquisitionMode.AUTO_DARK;
            set
            {
                if (value && acquisitionMode != AcquisitionMode.AUTO_DARK)
                {
                    logger.debug($"Spectrometer.ramanModeEnabled: autoDark -> {value}");
                    acquisitionMode = AcquisitionMode.AUTO_DARK;
                    laserState.enabled = false;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoRamanEnabled)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoDarkEnabled)));
                }
                else if (!value && !autoRamanEnabled)
                {
                    logger.debug($"Spectrometer.ramanModeEnabled: autoDark -> {value}");
                    acquisitionMode = AcquisitionMode.STANDARD;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoRamanEnabled)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoDarkEnabled)));
                }
                else if (value)
                    logger.debug($"Spectrometer.ramanModeEnabled: mode already {AcquisitionMode.AUTO_DARK}");
            }
        }

        public virtual bool autoRamanEnabled
        {
            get => acquisitionMode == AcquisitionMode.AUTO_RAMAN;
            set
            {
                if (value && acquisitionMode != AcquisitionMode.AUTO_RAMAN)
                {
                    logger.debug($"Spectrometer.ramanModeEnabled: autoRaman -> {value}");
                    acquisitionMode = AcquisitionMode.AUTO_RAMAN;
                    laserState.enabled = false;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoRamanEnabled)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoDarkEnabled)));
                }
                else if (!value && !autoDarkEnabled)
                {
                    logger.debug($"Spectrometer.ramanModeEnabled: autoRaman -> {value}");
                    acquisitionMode = AcquisitionMode.STANDARD;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoRamanEnabled)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoDarkEnabled)));
                }
                else if (value)
                    logger.debug($"Spectrometer.ramanModeEnabled: mode already {AcquisitionMode.AUTO_RAMAN}");
            }
        }

        public bool useBackgroundRemoval
        {
            get { return _useBackgroundRemoval; }
            set
            {
                if (!value)
                    stretchedDark = null;
                _useBackgroundRemoval = value;
            }
        }
        bool _useBackgroundRemoval = true;
        
        public bool performMatch
        {
            get { return _performMatch; }
            set
            {
                _performMatch = value;
            }
        }
        bool _performMatch = true;

        public bool performDeconvolution
        {
            get { return _performDeconvolution; }
            set
            {
                _performDeconvolution = value;
            }
        }
        bool _performDeconvolution = false;
        
        public bool useHorizontalROI
        {
            get { return _useHorizontalROI; }
            set
            {
                _useHorizontalROI = value;
            }
        }
        bool _useHorizontalROI = true;

        public virtual byte laserWatchdogSec
        {
            get => laserState.watchdogSec;
            set
            {
                if (laserState.watchdogSec != value)
                {
                    laserState.watchdogSec = value;
                }
                else
                    logger.debug($"Spectrometer.laserWatchdogSec: already {value}");
            }
        }   
        public virtual double rssi { get; }

        public void toggleLaser()
        {
            logger.debug($"toggleLaser: laserEnabled was {laserEnabled}");
            laserEnabled = !laserEnabled;
            logger.debug($"toggleLaser: laserEnabled now {laserEnabled}");
        }

        public virtual  bool laserEnabled
        {
            get => laserState.enabled;
            set
            {
                logger.debug($"laserEnabled.set: setting {value}");
                if (laserState.enabled != value)
                {
                    laserState.enabled = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserEnabled)));
                }
                else
                    logger.debug($"Spectrometer.laserEnabled: already {value}");
                logger.debug("laserEnabled.set: done");
            }
        }

        public virtual ushort laserDelayMS
        {
            get => laserState.laserDelayMS;
            set
            {
                if (laserState.laserDelayMS != value)
                {
                    laserState.laserDelayMS = value;
                }
                else
                    logger.debug($"Spectrometer.laserDelayMS: already {value}");
            }
        }

        protected bool laserSyncEnabled = true;

        public string note { get; set; }
        public string qrValue { get; set; } // parsed QR code

        ////////////////////////////////////////////////////////////////////////
        // battery
        ////////////////////////////////////////////////////////////////////////

        // I used to call this at the END of an acquisition, and that worked; 
        // until it didn't.  Now I call it BEFORE each acquisition, and that
        // seems to work better?
        internal abstract Task<bool> updateBatteryAsync();


        ////////////////////////////////////////////////////////////////////////
        // Auto-Raman Parameters
        ////////////////////////////////////////////////////////////////////////

        public bool holdAutoRamanParameterSet = false;

        public virtual ushort maxCollectionTimeMS
        {
            get => _maxCollectionTimeMS;
            set
            {
                if (value != _maxCollectionTimeMS)
                {
                    _maxCollectionTimeMS = value;
                    logger.debug($"Spectrometer.maxCollectionTimeMS -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(maxCollectionTimeMS)));
                }
            }
        }
        protected ushort _maxCollectionTimeMS = 2000;
        
        public virtual ushort startIntTimeMS
        {
            get => _startIntTimeMS;
            set
            {
                if (value != _startIntTimeMS)
                {
                    _startIntTimeMS = value;
                    logger.debug($"Spectrometer.startIntTimeMS -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(startIntTimeMS)));
                }
            }
        }
        protected ushort _startIntTimeMS = 200;

        public virtual byte startGainDb
        {
            get => _startGainDB;
            set
            {
                if (0 <= value && value <= 72)
                {
                    _startGainDB = value;
                    logger.debug($"Spectrometer.startGainDb: next = {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(startGainDb)));
                }
                else
                {
                    logger.error($"ignoring out-of-range gainDb {value}");
                }
            }
        }
        protected byte _startGainDB = 8;

        public virtual ushort minIntTimeMS
        {
            get => _minIntTimeMS;
            set
            {
                if (value != _minIntTimeMS)
                {
                    _minIntTimeMS = value;
                    logger.debug($"Spectrometer.minIntTimeMS -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(minIntTimeMS)));
                }
            }
        }
        protected ushort _minIntTimeMS = 10;
        
        public virtual ushort maxIntTimeMS
        {
            get => _maxIntTimeMS;
            set
            {
                if (value != _maxIntTimeMS)
                {
                    _maxIntTimeMS = value;
                    logger.debug($"Spectrometer.maxIntTimeMS -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(maxIntTimeMS)));
                }
            }
        }
        protected ushort _maxIntTimeMS = 1000;

        public virtual byte minGainDb
        {
            get => _minGainDb;
            set
            {
                if (0 <= value && value <= 72)
                {
                    _minGainDb = value;
                    logger.debug($"Spectrometer.minGainDb: next = {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(minGainDb)));
                }
                else
                {
                    logger.error($"ignoring out-of-range gainDb {value}");
                }
            }
        }
        protected byte _minGainDb = 0;

        public virtual byte maxGainDb
        {
            get => _maxGainDb;
            set
            {
                if (0 <= value && value <= 72)
                {
                    _maxGainDb = value;
                    logger.debug($"Spectrometer.maxGainDb: next = {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(maxGainDb)));
                }
                else
                {
                    logger.error($"ignoring out-of-range gainDb {value}");
                }
            }
        }
        protected byte _maxGainDb = 30;

        public virtual ushort targetCounts
        {
            get => _targetCounts;
            set
            {
                if (value != _targetCounts)
                {
                    _targetCounts = value;
                    logger.debug($"Spectrometer.targetCounts -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(targetCounts)));
                }
            }
        }
        protected ushort _targetCounts = 40000;
        
        public virtual ushort minCounts
        {
            get => _minCounts;
            set
            {
                if (value != _minCounts)
                {
                    _minCounts = value;
                    logger.debug($"Spectrometer.minCounts -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(minCounts)));
                }
            }
        }
        protected ushort _minCounts = 30000;

        public virtual ushort maxCounts
        {
            get => _maxCounts;
            set
            {
                if (value != _maxCounts)
                {
                    _maxCounts = value;
                    logger.debug($"Spectrometer.maxCounts -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(maxCounts)));
                }
            }
        }
        protected ushort _maxCounts = 50000;
        
        public virtual byte maxFactor
        {
            get => _maxFactor;
            set
            {
                if (value != _maxFactor)
                {
                    _maxFactor = value;
                    logger.debug($"Spectrometer.maxFactor -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(maxFactor)));
                }
            }
        }
        protected byte _maxFactor = 10;

        public virtual float dropFactor
        {
            get => _dropFactor;
            set
            {
                _dropFactor = value;
                logger.debug($"Spectrometer.dropFactor: next = {value}");
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(dropFactor)));
            }
        }
        protected float _dropFactor = 0.5f;

        public virtual ushort saturationCounts
        {
            get => _saturationCounts;
            set
            {
                if (value != _saturationCounts)
                {
                    _saturationCounts = value;
                    logger.debug($"Spectrometer.saturationCounts -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(saturationCounts)));
                }
            }
        }
        protected ushort _saturationCounts = 65000;

        public virtual byte maxAverage
        {
            get => _maxAverage;
            set
            {
                if (value != _maxAverage)
                {
                    _maxAverage = value;
                    logger.debug($"Spectrometer.maxAverage -> {value}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(maxAverage)));
                }
            }
        }
        protected byte _maxAverage = 1;

        protected byte[] packAutoRamanParameters()
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
        // dark
        ////////////////////////////////////////////////////////////////////////

        public void toggleDark()
        {
            logger.debug("Spectrometer.toggleDark: start");
            if (dark is null)
            {
                logger.debug("Spectrometer.dark: storing lastSpectrum as dark");
                dark = lastSpectrum;
            }
            else
            {
                logger.debug("Spectrometer.dark: clearing dark");
                dark = null;
                stretchedDark = null;
            }
            logger.debug("Spectrometer.toggleDark: dark {0} null", dark == null ? "is" : "IS NOT");
            logger.debug("Spectrometer.toggleDark: done");
        }

        ////////////////////////////////////////////////////////////////////////
        // spectra
        ////////////////////////////////////////////////////////////////////////

        // responsible for taking one fully-averaged measurement
        public abstract Task<bool> takeOneAveragedAsync();

        // Take one spectrum (of many, if doing scan averaging).  This is private,
        // callers are expected to use takeOneAveragedAsync().
        // 
        // There is no need to disable the laser if returning NULL, as the caller
        // will do so anyway.
        protected abstract Task<double[]> takeOneAsync(bool disableLaserAfterFirstPacket);

        ////////////////////////////////////////////////////////////////////////
        // BLE Characteristic Notifications (routed via BluetoothViewModel)
        ////////////////////////////////////////////////////////////////////////

        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserWatchdogSec)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserDelayMS)));
            //type = newType;
            //enabled = newEnabled laserEnabled;
            //watchdogSec = newWatchdog; laserWatchdogSec
            //laserDelayMS = newLaserDelayMS;  laserDelayMS

            sem.Release();
        }

        protected abstract void processGeneric(byte[] data);

        public void processGenericNotification(byte[] data)
        {
            logger.hexdump(data, "received generic notification: ");
            logger.info($"the length of notification is {data.Length}");

            if (EEPROMReadComplete)
            {
                if (data.Length > 2)
                    processGeneric(data);
                return;
            }
            
            int bytesToRead = data.Length - 2;
            Array.Copy(data, 2, EEPROMBuffer, EEPROMBytesRead, Math.Min(bytesToRead, EEPROM.PAGE_LENGTH));
            EEPROMBytesRead += (uint)bytesToRead;
            if (EEPROMBytesRead % EEPROM.PAGE_LENGTH == 0 || bytesToRead > EEPROM.PAGE_LENGTH)
                ++CurrentEEPROMPage;
            if (CurrentEEPROMPage == EEPROM.MAX_PAGES)
                EEPROMReadComplete = true;

            raiseConnectionProgress(.15 + .85 * CurrentEEPROMPage / EEPROM.MAX_PAGES);

            genericReturned = true;
        }


        // I'm never sure if this is needed or not
        protected async Task<bool> pauseAsync(string caller)
        {
            const int DELAY_MS = 10;
            logger.debug($"pauseAsync({caller}): waiting {DELAY_MS} ms");
            await Task.Delay(DELAY_MS);
            GC.Collect();
            return true;
        }

        ////////////////////////////////////////////////////////////////////////
        // 2x2 Binning
        ////////////////////////////////////////////////////////////////////////

        protected void apply2x2Binning(double[] spectrum)
        {
            if (eeprom.featureMask.bin2x2)
                for (int i = 0; i < spectrum.Length - 1; i++)
                    spectrum[i] = (spectrum[i] + spectrum[i + 1]) / 2.0;
        }

        ////////////////////////////////////////////////////////////////////////
        // Raman Intensity Correction (NIST SRM Calibration)
        ////////////////////////////////////////////////////////////////////////

        public bool useRamanIntensityCorrection { get; set; } = false;

        /// <summary>
        /// Performs SRM correction on the given spectrum.
        /// Non-ROI pixels are not corrected. 
        /// </summary>
        protected void applyRamanIntensityCorrection(double[] spectrum)
        {
            if (!useRamanIntensityCorrection)
            {
                logger.debug("declining RamanIntensityCorrection: disabled");
                return;
            }

            if (dark == null)
            {
                logger.debug("declining RamanIntensityCorrection: not dark-corrected");
                return;
            }

            if (eeprom.ROIHorizStart >= eeprom.ROIHorizEnd)
            {
                logger.debug("declining RamanIntensityCorrection: invalid horizontal ROI");
                return;
            }

            if (!laserEnabled)
            {
                logger.debug("declining RamanIntensityCorrection: laser not enabled");
                return;
            }

            for (int i = eeprom.ROIHorizStart; i <= eeprom.ROIHorizEnd; ++i)
            {
                double logTen = 0.0;
                for (int j = 0; j < eeprom.intensityCorrectionCoeffs.Length; j++)
                {
                    double x_to_i = Math.Pow(i, j);
                    double scaled = eeprom.intensityCorrectionCoeffs[j] * x_to_i;
                    logTen += scaled;
                }
                double factor = Math.Pow(10, logTen);
                spectrum[i] *= factor;
            }
        }

        ////////////////////////////////////////////////////////////////////////
        // Raman Wavenumber Correction (ASTM 1840-96 Calibration)
        ////////////////////////////////////////////////////////////////////////

        public int getPixelFromWavenumber(double cm)
        {
            if (wavenumbers == null)
                return -1;
            int pixel = Array.BinarySearch(wavenumbers, cm);
            if (pixel < 0)
                return ~pixel;
            else if (pixel >= wavenumbers.Length)
                return wavenumbers.Length - 1;
            else
                return pixel;
        }

        public bool FindAndApplyRamanShiftCorrection(Measurement spectrum, string compoundName)
        {
            //
            // Use code here to open file, look for peak list based on compound
            //

            List<double> peaksToFind = new List<double>()
            {
                1001.4,
                1031.8,
                1602.3
            };


            //
            // Perform peak finding here
            //

            List<double> offsets = new List<double>();
            PeakFindingConfig pfc = new PeakFindingConfig();
            PeakFinder pf = new PeakFinder(pfc);

            SortedDictionary<int, PeakInfo> peakInfos = pf.findPeaks(originalWavenumbers, spectrum.processed);


            foreach (double peak in peaksToFind)
            {
                int pixel = getPixelFromWavenumber(peak);

                try
                {
                    double minDelta = double.MaxValue;
                    PeakInfo info = null;
                    foreach (PeakInfo pi in peakInfos.Values)
                    {
                        double delta = Math.Abs(peak - pi.wavelength);
                        if (delta < minDelta)
                        {
                            minDelta = delta;
                            info = pi;
                        }
                    }

                    offsets.Add(minDelta);
                    logger.info("found peak {0}-{1}cm-1 at pixel {2}", compoundName, peak, info.interpolatedPixel);

                }
                catch (Exception ex)
                {
                    logger.info("failed to find peak {0}-{1}cm-1", compoundName, peak);
                }
            }

            if (offsets.Count > 0)
            {
                double averageOffset = offsets.Average();
                wavenumberOffset = averageOffset;
                return true;
            }
            else
                return false;
            //measurement.wavenumbers = wavenumbers;
        }

        public double wavenumberOffset
        {
            get => _wavenumberOffset;
            set
            {
                _wavenumberOffset = value;
                for (int i = 0; i < wavenumbers.Length; ++i)
                {
                    wavenumbers[i] = originalWavenumbers[i] + _wavenumberOffset;
                }
            }
        }
        double _wavenumberOffset = 0;


    }
}
