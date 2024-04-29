using System;
using System.ComponentModel;
using System.Text.RegularExpressions;
// using Xamarin.Forms;
// using Xamarin.Essentials;

// using EnlightenMobile.Services;

namespace EnlightenMAUI.Models;

// This class represents application-wide settings.  It currently corresponds 
// to ENLIGHTEN's Configuration (enlighten.ini) and SaveOptions classes, and
// a bit of FileManager and common.py.
//
// @todo split Authentication into its own Model
public class Settings : INotifyPropertyChanged
{
    static Settings instance = null;

    public const string stars = "••••••••";

    // so it can send out notifications that authentication has changed, to
    // anyone interested in authentication status
    public event PropertyChangedEventHandler PropertyChanged;

    // where to save spectra on the internet
    public string saveURL;

    // if provided, an override directing where to save spectra on the filesystem (else use default path)
    public string savePath;

    // todo: move to SaveOptions
    public bool savePixel;
    public bool saveWavelength;
    public bool saveWavenumber;
    public bool saveRaw;
    public bool saveDark;
    public bool saveReference;

    // todo: prompt to auto-connect this device if found on scan
    // public Guid lastConnectedGuid;

    public string version
    {
        get => $"version {VersionTracking.CurrentVersion}";
    }

    public string companyURL = "https://wasatchphotonics.com";

    Logger logger = Logger.getInstance();

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
        get
        {
            var model   = DeviceInfo.Model;         // SMG-950U, iPhone10,6 etc
            var manuf   = DeviceInfo.Manufacturer;  // Samsung, Apple etc
            var name    = DeviceInfo.Name;          // "Mark's iPhone" etc
            var version = DeviceInfo.VersionString; // 7.0 etc
            var os      = DeviceInfo.Platform;      // Android, iOS etc

            return $"{name} ({manuf} {model} running {os} {version})";
        }
    }

    ////////////////////////////////////////////////////////////////////////
    // SaveOptions / FileManager
    ////////////////////////////////////////////////////////////////////////

    public string getSavePath()
    {
        // IPlatformUtil platformUtil = DependencyService.Get<IPlatformUtil>();
        // return platformUtil.getSavePath();
        return "NotImplemented";
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
