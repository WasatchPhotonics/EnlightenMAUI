using System.ComponentModel;

using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels;

// Provides the backing logic and bound properties shown on the SettingsView.
public class SettingsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    Settings settings = Settings.getInstance();

    BluetoothSpectrometer spec = BluetoothSpectrometer.getInstance();
    Logger logger = Logger.getInstance();

    public SettingsViewModel()
    {
        laserWatchdogTimeoutSec = 0;
        laserWarningDelaySec = 0;
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

    public string title
    {
        get => "Application Settings";
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
}
