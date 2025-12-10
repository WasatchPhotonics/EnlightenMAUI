using System.Collections.ObjectModel;
using System.ComponentModel;

using EnlightenMAUI.Models;
using EnlightenMAUI.Popups;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Telerik.Windows.Documents.Spreadsheet.History;
using System.Runtime.CompilerServices;
using EnlightenMAUI.Platforms;
using WPProduction.Utils;
using Newtonsoft.Json;
using Telerik.Maui.Controls;

namespace EnlightenMAUI.ViewModels
{
    public class PersistentSettings
    {
        public Dictionary<string, AutoRamanParameters> AutoParameters;
        public double  MatchThereshold;
        public double  EllmanCorrection;
        public int SNRThreshold;
        public string lastSpecDate;
        public int? specCount;
    }

    public class AutoRamanParameters
    {
        public ushort maxCollectionTimeMS;
        public ushort startIntTimeMS;
        public byte startGainDb;
        public ushort minIntTimeMS;
        public ushort maxIntTimeMS;
        public byte minGainDb;
        public byte maxGainDb;
        public ushort targetCounts;
        public ushort minCounts;
        public ushort maxCounts;
        public byte maxFactor;
        public float dropFactor;
        public ushort saturationCounts;
        public byte maxAverage;
    }

    // Provides the backing logic and bound properties shown on the SettingsView.
    public class SettingsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        Settings settings = Settings.getInstance();

        Spectrometer spec = BluetoothSpectrometer.getInstance();
        Logger logger = Logger.getInstance();
        bool initialized = false;

        public ObservableCollection<string> paramSets
        {
            get => _paramSets;
        }
            
        static ObservableCollection<string> _paramSets = new ObservableCollection<string>()
        {
            "Default",
            "Faster"
        };

        public string currentParamSet
        {
            get { return _currentParamSet; }
            set
            {
                if (value != _currentParamSet)
                {
                    changeParamSet(value);
                }
            }

        }
        string _currentParamSet = "Faster";

        public bool fastMode
        {
            get => _fastMode;
            set
            {
                if (value != _fastMode)
                {
                    if (value)
                        changeParamSet("Faster");
                    else
                        changeParamSet("Default");

                    _fastMode = value;
                }
            }
        }
        bool _fastMode = true;

        public string enteredPassword
        {
            get
            {
                return _enteredPassword;
            }
            set
            {
                _enteredPassword = value;
                _passwordCorrect = _enteredPassword == UNLOCK_PASSWORD;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(enteredPassword)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(passwordCorrect)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(passwordIncorrect)));
            }
        }
        string _enteredPassword;


        const string UNLOCK_PASSWORD = "photon";
        public bool passwordCorrect
        {
            get => _passwordCorrect;
        }
        public bool passwordIncorrect
        {
            get => !_passwordCorrect;
        }

        bool _passwordCorrect = false;



        void changeParamSet(string key)
        {
            if (!settings.parameterSets.ContainsKey(key))
                return;

            if (spec != null && spec.paired)
            { 
                spec.holdAutoRamanParameterSet = true;
                AutoRamanParameters parameters = settings.parameterSets[key];
                spec.maxCollectionTimeMS = parameters.maxCollectionTimeMS;
                spec.startIntTimeMS = parameters.startIntTimeMS;
                spec.startGainDb = parameters.startGainDb;
                spec.minIntTimeMS = parameters.minIntTimeMS;
                spec.maxIntTimeMS = parameters.maxIntTimeMS;
                spec.minGainDb = parameters.minGainDb;
                spec.maxGainDb = parameters.maxGainDb;
                spec.targetCounts = parameters.targetCounts;
                spec.minCounts = parameters.minCounts;
                spec.maxCounts = parameters.maxCounts;
                spec.maxFactor = parameters.maxFactor;
                spec.dropFactor = parameters.dropFactor;
                spec.saturationCounts = parameters.saturationCounts;
                spec.holdAutoRamanParameterSet = false;
                spec.maxAverage = parameters.maxAverage;

                _currentParamSet = key;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(currentParamSet)));
            }
            }

        public SettingsViewModel()
        {
            laserWatchdogTimeoutSec = 0;
            laserWarningDelaySec = 0;

            if (spec == null || !spec.paired)
                spec = API6BLESpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = USBSpectrometer.getInstance();

            Spectrometer.NewConnection += handleNewSpectrometer;
            settings.ConfigLoaded += Settings_ConfigLoaded;

            if (settings.initialized)
                initialized = true;

            //setConfigurationFromFile();
        }

        private void Settings_ConfigLoaded(object sender, Settings e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(snrThreshold)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchThreshold)));
            initialized = true;
        }

        void handleNewSpectrometer(object sender, Spectrometer e)
        {
            spec = e;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(integrationTimeMS)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(gainDb)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(scansToAverage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(useHorizontalROI)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoDarkEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoRamanEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(useRamanIntensityCorrection)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(useBackgroundRemoval)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(performMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(performDeconvolution)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserWatchdogTimeoutSec)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserWarningDelaySec)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(verticalROIStartLine)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(verticalROIStopLine)));

        }


        async Task setConfigurationFromFile()
        {
            //await settings.setConfigurationFromFile();
            initialized = true;
        }

        async Task updateConfigFile()
        {
            await settings.updateConfigFile();
        }

        public void loadSettings()
        {
            bool savePixelValue = Preferences.Get("savePixel", false);
            bool saveWavelengthValue = Preferences.Get("saveWavelength", false);
            bool saveWavenumberValue = Preferences.Get("saveWavenumber", false);
            bool saveRawValue = Preferences.Get("saveRaw", false);
            bool saveDarkValue = Preferences.Get("saveDark", false);
            bool saveReferenceValue = Preferences.Get("saveReference", false);
            bool authValue = Preferences.Get("authenticated", false);

            settings.savePixel = savePixelValue;
            settings.saveWavelength = saveWavelengthValue;
            settings.saveWavenumber = saveWavenumberValue;
            settings.saveRaw = saveRawValue;
            settings.saveDark = saveDarkValue;
            settings.saveReference = saveReferenceValue;
            settings.authenticated = authValue;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(savePixel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(saveWavelength)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(saveWavenumber)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(saveRaw)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(saveDark)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(saveReference)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoSave)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isAuthenticated)));
        }

        public string title
        {
            get => "Application Settings";
        }


        ////////////////////////////////////////////////////////////////////////
        // Acquisition Parameters
        ////////////////////////////////////////////////////////////////////////

        public UInt32 integrationTimeMS
        {
            get => spec != null ? spec.integrationTimeMS : 0;
            set
            {
                if (spec != null && spec.paired)
                spec.integrationTimeMS = value;
            }
        }

        public float gainDb
        {
            get => spec != null ? spec.gainDb : 0f;
            set
            {
                if (spec != null && spec.paired)
                    spec.gainDb = value;
            }
        }

        public byte scansToAverage
        {
            get => spec != null ? spec.scansToAverage : (byte)0;
            set
            {
                if (spec != null && spec.paired)
                    spec.scansToAverage = value;
            }
        }

        ////////////////////////////////////////////////////////////////////////
        // misc acquisition parameters
        ////////////////////////////////////////////////////////////////////////

        // @todo: let the user live-toggle this and update the on-screen spectrum
        public bool useHorizontalROI
        {
            get => spec != null ? spec.useHorizontalROI : true;
            set
            {
                if (spec != null && spec.paired)
                    spec.useHorizontalROI = value;
            }
        }

        public bool autoDarkEnabled
        {
            get => spec != null ? spec.autoDarkEnabled : false;
            set
            {
                if (spec != null && spec.paired)
                {
                    if (spec.autoDarkEnabled != value)
                        spec.autoDarkEnabled = value;

                    if (spec.acquisitionMode == AcquisitionMode.STANDARD)
                        assertSettings();

                    advancedModeEnabled = advancedModeEnabled;
                }
                updateLaserProperties();
            }
        }

        public bool autoRamanEnabled
        {
            get => spec != null ? spec.autoRamanEnabled : false;
            set
            {
                if (spec != null && spec.paired)
                {
                    if (spec.autoRamanEnabled != value)
                        spec.autoRamanEnabled = value;

                    if (spec.acquisitionMode == AcquisitionMode.STANDARD)
                        assertSettings();

                    advancedModeEnabled = advancedModeEnabled;
                }
                updateLaserProperties();
            }
        }

        void assertSettings()
        {
            spec.scansToAverage = spec.scansToAverage;
            spec.integrationTimeMS = spec.integrationTimeMS;
            spec.gainDb = spec.gainDb;
        }

        // @todo: let the user live-toggle this and update the on-screen spectrum
        public bool useRamanIntensityCorrection
        {
            get => spec != null ? spec.useRamanIntensityCorrection : false;
            set => spec.useRamanIntensityCorrection = value;
        }

        public bool useBackgroundRemoval
        {
            get => spec != null ? spec.useBackgroundRemoval : true;
            set
            {
                if (spec != null && spec.paired)
                    spec.useBackgroundRemoval = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(useBackgroundRemoval)));
            }
        }
        private bool _useBackgroundRemoval = true;

        public bool performMatch
        {
            get => spec != null ? spec.performMatch : true;
            set
            {
                if (spec != null && spec.paired)
                    spec.performMatch = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(performMatch)));
            }
        }

        public bool performDeconvolution
        {
            get => spec != null ? spec.performDeconvolution : false;
            set
            {
                if (spec != null && spec.paired)
                    spec.performDeconvolution = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(performDeconvolution)));
            }
        }

        public decimal matchThreshold
        {
            get => (decimal)settings.matchThreshold;
            set
            {
                settings.matchThreshold = (float)value;
                Preferences.Set("matchThreshold", (float)value);
                if (initialized)
                    updateConfigFile();
            }
        }

        public decimal ellmanCorrection
        {
            get => (decimal)settings.ellmanSlopeCorrection;
            set
            {
                settings.ellmanSlopeCorrection = (float)value;
                Preferences.Set("ellmanCorrection", (float)value);
                if (initialized)
                    updateConfigFile();
            }
        }

        public int snrThreshold
        {
            get => settings.snrThreshold;
            set
            {
                settings.snrThreshold = value;
                Preferences.Set("snrThreshold", value);
                if (initialized)
                    updateConfigFile();
            }
        }

        public void updateLaserProperties()
        {
            logger.debug("SVM.updateLaserProperties: start");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoDarkEnabled)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoRamanEnabled)));
            logger.debug("SVM.updateLaserProperties: done");
        }

        public bool savePixel
        {
            get => settings.savePixel;
            set
            {
                settings.savePixel = value;
                Preferences.Set("savePixel", value);
                System.Console.WriteLine($"Changed save pixel to the following: {settings.savePixel.ToString()}");
            }
        }

        public bool saveWavelength
        {
            get => settings.saveWavelength;
            set
            {
                settings.saveWavelength = value;
                Preferences.Set("saveWavelength", value);
            }
        }

        public bool saveWavenumber
        {
            get => settings.saveWavenumber;
            set
            {
                settings.saveWavenumber = value;
                Preferences.Set("saveWavenumber", value);
            }
        }

        public bool saveRaw
        {
            get => settings.saveRaw;
            set
            {
                settings.saveRaw = value;
                Preferences.Set("saveRaw", value);
            }
        }

        public bool saveDark
        {
            get => settings.saveDark;
            set
            {
                settings.saveDark = value;
                Preferences.Set("saveDark", value);
            }
        }

        public bool saveReference
        {
            get => settings.saveReference;
            set
            {
                settings.saveReference = value;
                Preferences.Set("saveReference", value);
            }
        }
        
        public bool autoSave
        {
            get => settings.autoSave;
            set
            {
                settings.autoSave = value;
                Preferences.Set("autoSave", value);
            }
        }

        ////////////////////////////////////////////////////////////////////////
        // Authentication 
        ////////////////////////////////////////////////////////////////////////

        public string password
        {
            get => Settings.stars;
            set
            {
                // We are not doing anything here, because we don't want to
                // process per-character input (which is what the Entry binding
                // gives us); instead, wait until they hit return, which will
                // trigger the View's Complete method.  That method will then
                // call the authenticate() method below.
            }
        }

        public bool isAuthenticated
        {
            get => settings.authenticated;
        }

        // the user entered a new password on the view, so authenticate it
        public void authenticate(string password)
        {
            settings.authenticate(password);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isAuthenticated)));
        }

        ////////////////////////////////////////////////////////////////////////
        // Advanced Features
        ////////////////////////////////////////////////////////////////////////

        public byte laserWatchdogTimeoutSec
        {
            get => spec != null ? spec.laserWatchdogSec : (byte)0;
            set
            {
                if (spec != null && spec.paired)
                    spec.laserWatchdogSec = value;
                Preferences.Set("laserWatchdog", value);
            }
        }

        public byte laserWarningDelaySec
        {
            get => spec != null ? spec.laserWarningDelaySec : (byte)0;
            set
            {
                if (spec != null && spec.paired)
                    spec.laserWarningDelaySec = value;
                Preferences.Set("laserWarningDelaySec", value);
            }
        }

        public ushort verticalROIStartLine
        {
            get => spec != null ? spec.verticalROIStartLine : (ushort)0;
            set
            {
                if (spec != null && spec.paired)
                    spec.verticalROIStartLine = value;
                Preferences.Set("verticalROIStartLine", value);
            }
        }

        public ushort verticalROIStopLine
        {
            get => spec != null ? spec.verticalROIStopLine : (ushort)0;
            set
            {
                if (spec != null && spec.paired)
                    spec.verticalROIStopLine = value;
                Preferences.Set("verticalROIStopLine", value);
            }
        }

        public bool advancedModeEnabled
        {
            get => settings.advancedModeEnabled;
            set
            {
                settings.advancedModeEnabled = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(advancedModeEnabled)));
            }
        }

    }
}