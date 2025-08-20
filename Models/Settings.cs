using EnlightenMAUI.Platforms;
using EnlightenMAUI.ViewModels;
using EnlightenMAUI.Common;
using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
using static Android.Widget.GridLayout;

namespace EnlightenMAUI.Models;

// This class represents application-wide settings.  It currently corresponds 
// to ENLIGHTEN's Configuration (enlighten.ini) and SaveOptions classes, and
// a bit of FileManager and common.py.
//
// @todo split Authentication into its own Model
public class Settings : INotifyPropertyChanged
{
    static Settings instance = null;
    public event EventHandler<Settings> LibraryChanged;

    public const string stars = "••••••••";

    // so it can send out notifications that authentication has changed, to
    // anyone interested in authentication status
    public event PropertyChangedEventHandler PropertyChanged;

    // where to save spectra on the internet
    public string saveURL;

    // if provided, an override directing where to save spectra on the filesystem (else use default path)
    public string savePath;

    // todo: move to SaveOptions
    public bool savePixel { get; set;} = true;
    public bool saveWavelength { get; set;} = true;
    public bool saveWavenumber { get; set;} = true;
    public bool saveRaw { get; set;} = true;
    public bool saveDark { get; set;} = true;
    public bool autoSave { get; set;} = true;
    public bool saveReference { get; set;} = true;
    public float matchThreshold
    {
        get => _matchThreshold;
        set
        {
            logger.info("setting match thresh to {0:f2}", value);
            _matchThreshold = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchThreshold)));
        }
    }
    float _matchThreshold = 0.6f;
    public int snrThreshold
    {
        get => _snrThreshold;
        set
        {
            _snrThreshold = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(snrThreshold)));
        }
    }
    int _snrThreshold = 120;

    public Spectrometer spec = BluetoothSpectrometer.getInstance();

    // todo: prompt to auto-connect this device if found on scan
    // public Guid lastConnectedGuid;

    public string version
    {
        get => $"version {VersionTracking.CurrentVersion}";
    }

    public string companyURL = "https://wasatchphotonics.com";

    Logger logger = Logger.getInstance();
    public Library library { get; set;}
    DPLibrary dpLibrary = null;
    WPLibrary wpLibrary = null;


    ////////////////////////////////////////////////////////////////////////
    // Lifecycle
    ////////////////////////////////////////////////////////////////////////

    static public Settings getInstance()
    {
        if (instance is null)
            instance = new Settings();
        return instance;
    }

    Settings()
    {
        logger.info($"EnlightenMAUI {version}");
        logger.info($"hostDescription = {hostDescription}");
        logger.info($"OS = {os}"); 
        if (spec == null || !spec.paired)
            spec = API6BLESpectrometer.getInstance();
        if (spec == null || !spec.paired)
            spec = USBSpectrometer.getInstance();
    }

    ////////////////////////////////////////////////////////////////////////
    // Device / Platform
    ////////////////////////////////////////////////////////////////////////

    public string os
    {
        get => DeviceInfo.Platform.ToString();
    }

    public string hostDescription
    {
        get => $"{DeviceInfo.Name} ({DeviceInfo.Manufacturer} {DeviceInfo.Model} running {DeviceInfo.Platform} {DeviceInfo.VersionString})";
    }
    public string hostDescriptionWrapped
    {
        get => $"EnlightenMobile {VersionTracking.CurrentVersion}\n{DeviceInfo.Name}\n{DeviceInfo.Manufacturer} {DeviceInfo.Model}\n{DeviceInfo.Platform} {DeviceInfo.VersionString}";
    }

    ////////////////////////////////////////////////////////////////////////
    // SaveOptions / FileManager
    ////////////////////////////////////////////////////////////////////////

    public async Task checkHighLevel()
    {
        _highLevelAutoSave = await Util.enableAutoSave();
    }

    public async Task setLibrary(string type)
    {
        bool libraryChanged = false;
        bool initialLoad = false;

        if (type == "Wasatch")
        {
            if (library == null || (library is DPLibrary))
            {
                libraryChanged = true;

                if (library != null && library is DPLibrary)
                    dpLibrary = (DPLibrary)library;

                if (wpLibrary != null)
                    library = wpLibrary;
                else
                {
                    initialLoad = true;
                    await Task.Run(() =>
                    {
                        //library = new DPLibrary("database", spec);
                        library = new WPLibrary("library/Wasatch", spec);
                        library.LoadFinished += Library_LoadFinished;
                    });
                }
            }
        }
        else if (type == "3rd Party")
        {
            if (library == null || !(library is DPLibrary))
            {
                libraryChanged = true;

                if (library != null && !(library is DPLibrary))
                    wpLibrary = (WPLibrary)library;

                if (dpLibrary != null)
                    library = dpLibrary;
                else
                {
                    initialLoad = true;
                    await Task.Run(() =>
                    {
                        library = new DPLibrary("database", spec);
                        library.LoadFinished += Library_LoadFinished;
                    });
                }
            }
        }

        if (libraryChanged)
        {
            libraryLabel = type;
            if (!initialLoad)
                LibraryChanged.Invoke(this, this);
        }
    }

    private void Library_LoadFinished(object sender, Library e)
    {
        LibraryChanged.Invoke(this, this);
    }

    public string libraryLabel = "Wasatch";

    public bool autoRetry
    {
        get { return _autoRetry; }
        set
        {
            _autoRetry = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(autoRetry)));
        }
    }
    bool _autoRetry = true;


    public string getSavePath()
    {
        return PlatformUtil.getSavePath();
    }

    public string getUserLibraryPath()
    {
        return PlatformUtil.getUserLibraryPath();
    }

    public string getAutoSavePath()
    {
        return PlatformUtil.getAutoSavePath(highLevelAutoSave);
    }

    public bool highLevelAutoSave
    {
        get=> _highLevelAutoSave;
    }
    bool _highLevelAutoSave = false;


    // Write the file content to the app data directory
    public void writeFile(string pathname, string text)
    {
        File.WriteAllText(pathname, text);
    }

    ////////////////////////////////////////////////////////////////////////
    // Authentication
    ////////////////////////////////////////////////////////////////////////

    // This exposes Production Quality Control (test/verification) operations
    // normally not exposed to the end-user.
    //
    // @warning This mode increases opportunity for laser eye injury due to
    //          operator error.  Do not enable without cause and appropriate
    //          Personal Protective Equipment.
    public bool authenticated
    {
        get => _authenticated;
        set
        {
            _authenticated = value;
            Preferences.Set("authenticated", value);
            // notify anyone listening to Settings.authenticated, such as
            // ScopeViewModel (which uses this to decide whether to show the
            // laserFiring switch, etc)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(authenticated)));
        }
    }
    bool _authenticated;

    public bool advancedModeEnabled
    {
        get => _advancedModeEnabled;
        set
        {
            _advancedModeEnabled = value;
            Preferences.Set("advancedModeEnabled", value);
            // notify anyone listening to Settings.advancedModeEnabled, such as
            // ScopeViewModel (which uses this to decide whether to show the
            // laserFiring switch, etc)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(advancedModeEnabled)));
        }
    }
    bool _advancedModeEnabled = true;

    // The user entered a new password on the SettingsView, and hit
    // return, so the View asked the ViewModel to authenticate it.  The
    // SettingsViewModel then asked the Model to authenticate it.
    //
    // Obviously this is not a way to conceal genuinely dangerous
    // functionality in an open-source project.  Programmers can access the
    // full BLE or USB API all they want.  This is meant to keep casual
    // users from accidentally enabling dangerous test-mode behaviors by
    // simply clicking the wrong button.
    public bool authenticate(string password)
    {
        const string EXPECTED_PASSWORD = "DangerMan";
        authenticated = password == EXPECTED_PASSWORD;

        logger.debug($"authenticated = {authenticated}");
        return authenticated;
    }
}
