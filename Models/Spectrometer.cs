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

namespace EnlightenMAUI.Models
{
    public abstract class Spectrometer : INotifyPropertyChanged
    {
        public static EventHandler<Spectrometer> NewConnection;
        protected Logger logger = Logger.getInstance();
        // @see https://forums.xamarin.com/discussion/93330/mutex-is-bugged-in-xamarin
        protected static readonly SemaphoreSlim sem = new SemaphoreSlim(1, 1);

        // hardware model
        public uint pixels;
        public float laserExcitationNM;
        public EEPROM eeprom = EEPROM.getInstance();
        public Battery battery;

        ////////////////////////////////////////////////////////////////////////
        // laserState
        ////////////////////////////////////////////////////////////////////////
        protected LaserState laserState = new LaserState();

        // software state
        public double[] wavelengths;
        public double[] wavenumbers;
        public double[] xAxisPixels;

        public double[] lastSpectrum;
        public double[] dark;
        public double[] stretchedDark;

        public Measurement measurement;

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

        public static void raiseSpectrometerConnected()
        {
            NewConnection.Invoke(null, null);
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
            get => laserState.mode == LaserMode.AUTO_DARK;
            set
            {
                var mode = value ? LaserMode.AUTO_DARK : LaserMode.MANUAL;
                if (laserState.mode != mode)
                {
                    logger.debug($"Spectrometer.ramanModeEnabled: laserState.mode -> {mode}");
                    laserState.mode = mode;
                    laserState.enabled = false;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoDarkEnabled)));
                }
                else
                    logger.debug($"Spectrometer.ramanModeEnabled: mode already {mode}");
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

            sem.Release();
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
    }
}
