using System.Collections.ObjectModel;
using System.ComponentModel;

using EnlightenMAUI.Models;
using EnlightenMAUI.Popups;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Telerik.Windows.Documents.Spreadsheet.History;

namespace EnlightenMAUI.ViewModels
{
    public struct AutoRamanParameters
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

        public SettingsViewModel()
        {
            laserWatchdogTimeoutSec = 0;
            laserWarningDelaySec = 0;

            if (spec == null || !spec.paired)
                spec = API6BLESpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = USBSpectrometer.getInstance();

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isAuthenticated)));
        }

        public Command addCmd { get; private set; }

        public string title
        {
            get => "Application Settings";
        }


        ////////////////////////////////////////////////////////////////////////
        // Acquisition Parameters
        ////////////////////////////////////////////////////////////////////////

        public UInt32 integrationTimeMS
        {
            get => spec.integrationTimeMS;
            set
            {
                spec.integrationTimeMS = value;
            }
        }

        public float gainDb
        {
            get => spec.gainDb;
            set
            {
                spec.gainDb = value;
            }
        }

        public byte scansToAverage
        {
            get => spec.scansToAverage;
            set
            {
                spec.scansToAverage = value;
            }
        }

        ////////////////////////////////////////////////////////////////////////
        // misc acquisition parameters
        ////////////////////////////////////////////////////////////////////////

        // @todo: let the user live-toggle this and update the on-screen spectrum
        public bool useHorizontalROI
        {
            get => spec.useHorizontalROI;
            set
            {
                spec.useHorizontalROI = value;
            }
        }

        public bool autoDarkEnabled
        {
            get => spec.autoDarkEnabled;
            set
            {
                if (spec.autoDarkEnabled != value)
                    spec.autoDarkEnabled = value;

                if (spec.acquisitionMode == AcquisitionMode.STANDARD)
                    assertSettings();

                advancedModeEnabled = advancedModeEnabled;
                updateLaserProperties();
            }
        }

        public bool autoRamanEnabled
        {
            get => spec.autoRamanEnabled;
            set
            {
                if (spec.autoRamanEnabled != value)
                    spec.autoRamanEnabled = value;

                if (spec.acquisitionMode == AcquisitionMode.STANDARD)
                    assertSettings();

                advancedModeEnabled = advancedModeEnabled;
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
            get => spec.useRamanIntensityCorrection;
            set => spec.useRamanIntensityCorrection = value;
        }

        public bool useBackgroundRemoval
        {
            get => spec.useBackgroundRemoval;
            set
            {
                spec.useBackgroundRemoval = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(useBackgroundRemoval)));
            }
        }
        private bool _useBackgroundRemoval = true;

        public bool performMatch
        {
            get => spec.performMatch;
            set
            {
                spec.performMatch = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(performMatch)));
            }
        }

        public bool performDeconvolution
        {
            get => spec.performDeconvolution;
            set
            {
                spec.performDeconvolution = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(performDeconvolution)));
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
            get => spec.laserWatchdogSec;
            set
            {
                spec.laserWatchdogSec = value;
                Preferences.Set("laserWatchdog", value);
            }
        }

        public byte laserWarningDelaySec
        {
            get => spec.laserWarningDelaySec;
            set
            {
                spec.laserWarningDelaySec = value;
                Preferences.Set("laserWarningDelaySec", value);
            }
        }

        public ushort verticalROIStartLine
        {
            get => spec.verticalROIStartLine;
            set
            {
                spec.verticalROIStartLine = value;
                Preferences.Set("verticalROIStartLine", value);
            }
        }

        public ushort verticalROIStopLine
        {
            get => spec.verticalROIStopLine;
            set
            {
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