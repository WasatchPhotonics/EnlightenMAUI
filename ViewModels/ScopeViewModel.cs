using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using Telerik.Maui.Controls.Compatibility.Chart;

using EnlightenMAUI.Models;
using EnlightenMAUI.Platforms;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using EnlightenMAUI.Popups;
using static Android.Provider.DocumentsContract;
using static Java.Util.Jar.Attributes;
#if USE_DECON
using Deconvolution = DeconvolutionMAUI;
#endif
using EnlightenMAUI.Common;
using static Microsoft.Maui.LifecycleEvents.AndroidLifecycle;
using Telerik.Windows.Documents.Spreadsheet.Expressions.Functions;
using System.Reflection.Metadata;
using System.Text;
using Xamarin.Google.Crypto.Tink.Signature;

namespace EnlightenMAUI.ViewModels;

// This class provides all the business logic controlling the ScopeView. 
public class ScopeViewModel : INotifyPropertyChanged
{
    public static UInt32[] colors =
    {
        0xffe6194b,
        0xff3cb44b,
        0xffffe119,
        0xff4363d8,
        0xfff58231,
        0xff911eb4,
        0xff46f0f0,
        0xfff032e6,
        0xffbcf60c,
        0xfffabebe,
        0xff008080,
        0xffe6beff,
        0xff9a6324,
        0xfffffac8,
        0xff800000,
        0xffaaffc3,
        0xff808000,
        0xffffd8b1,
        0xff000075,
        0xff808080
    };

    //private readonly IPopupService popupService;
    public event PropertyChangedEventHandler PropertyChanged;
    public event EventHandler<ScopeViewModel> OverlaysChanged;
    public event EventHandler<ScopeViewModel> WipeOverlays;
    SaveSpectrumPopupViewModel saveViewModel;
    OverlaysPopupViewModel overlaysViewModel;
    SaveSpectrumPopup savePopup;
    bool popupClosing = false;
    public Dictionary<string, bool> fullLibraryOverlayStatus = new Dictionary<string, bool>();
    Dictionary<string, Measurement> userDataLibrary = new Dictionary<string, Measurement>();

    // So the ScopeViewModel can float-up Toast events to the ScopeView.
    // This probably could be done using notifications, but I'm not sure I
    // want to make a "public string toastMessage" Property, and I'm not
    // sure what the "best practice" architecture would be.
    public delegate void ToastNotification(string msg);
    public event ToastNotification notifyToast;

    ////////////////////////////////////////////////////////////////////////
    // Private attributes
    ////////////////////////////////////////////////////////////////////////

    public Spectrometer spec;
    Settings settings;

    Logger logger = Logger.getInstance();
    Library library;
    Task libraryLoader;

    public delegate void UserNotification(string title, string message, string button);
    public event UserNotification notifyUser;

    ////////////////////////////////////////////////////////////////////////
    // Lifecycle
    ////////////////////////////////////////////////////////////////////////

    public ScopeViewModel()
    {
        //this.popupService = popupService;
        logger.debug("SVM.ctor: start");

        spec = BluetoothSpectrometer.getInstance();
        if (spec == null || !spec.paired)
            spec = API6BLESpectrometer.getInstance();
        if (spec == null || !spec.paired)
            spec = USBSpectrometer.getInstance();

        Task loader = PlatformUtil.loadONNXModel("background_model.onnx", "etalon_correction.json");
        loader.Wait();
        //Thread.Sleep(100);

        if (spec != null && spec.paired)
        {
            libraryLoader = Task.Run(() =>
            {
                library = new DPLibrary("database", spec);
                //library = new WPLibrary("library", spec); 
                AnalysisViewModel.getInstance().library = library;
            });
            libraryLoader.Wait();
            library.LoadFinished += Library_LoadFinished;
            Task.Run(() => findUserFiles());
        }


        overlaysViewModel = new OverlaysPopupViewModel(new List<SpectrumOverlayMetadata>());
        settings = Settings.getInstance();
        string savePath = settings.getSavePath();

        settings.PropertyChanged += handleSettingsChange;
        spec.PropertyChanged += handleSpectrometerChange;
        spec.showAcquisitionProgress += showAcquisitionProgress;
        spec.measurement.PropertyChanged += handleSpectrometerChange;
        Spectrometer.NewConnection += handleNewSpectrometer;

        if (spec != null && spec.paired)
        {
            spec.laserWatchdogSec = 0;
            spec.laserWarningDelaySec = 0;
        }

        // bind ScopePage Commands
        laserCmd = new Command(() => { _ = doLaser(); });
        laserWarningCmd = new Command(() => { _ = advanceLaserWarning(); });
        acquireCmd = new Command(() => { _ = doAcquireAsync(); });
        refreshCmd = new Command(() => { _ = doAcquireAsync(); });
        darkCmd = new Command(() => { _ = doDark(); });

        saveCmd = new Command(() => { _ = doSave(); });
        uploadCmd = new Command(() => { _ = doUpload(); });
        addCmd = new Command(() => { _ = doAdd(); });
        clearCmd = new Command(() => { _ = doClear(); });
        //matchCmd   = new Command(() => { _ = doMatchAsync  (); });

        xAxisNames = new ObservableCollection<string>();
        xAxisNames.Add("Pixel");
        xAxisNames.Add("Wavelength");
        xAxisNames.Add("Wavenumber");

        logger.debug("SVM.ctor: updating chart");
        updateChart();

       if (spec != null && spec.paired && spec.eeprom.hasBattery)
            spec.updateBatteryAsync();
        if (spec != null && spec.paired)
        {
            if (spec is USBSpectrometer || spec is BluetoothSpectrometer)
                spec.autoRamanEnabled = true;
            else
            {
                spec.autoRamanEnabled = false;
                spec.autoDarkEnabled = false;
            }
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(paired)));
        logger.debug("SVM.ctor: done");
    }

    void handleNewSpectrometer(object sender, Spectrometer e)
    {
        refreshSpec();
    }

    public void refreshSpec()
    {
        fullLibraryOverlayStatus.Clear();
        spec = BluetoothSpectrometer.getInstance();
        if (spec == null || !spec.paired)
            spec = USBSpectrometer.getInstance();

        logger.debug("refreshing from USB spec");

        if (spec != null && spec.paired)
        {
            libraryLoader = Task.Run(() =>
            {
                library = new WPLibrary("library", spec);
                AnalysisViewModel.getInstance().library = library;
            });
            libraryLoader.Wait();
            library.LoadFinished += Library_LoadFinished;
            Task.Run(() => findUserFiles());
        }

        logger.debug("finished loading library in refresh");

        overlaysViewModel = new OverlaysPopupViewModel(new List<SpectrumOverlayMetadata>());

        spec.PropertyChanged += handleSpectrometerChange;
        spec.showAcquisitionProgress += showAcquisitionProgress;
        spec.measurement.PropertyChanged += handleSpectrometerChange;

        laserCmd = new Command(() => { _ = doLaser(); });
        acquireCmd = new Command(() => { _ = doAcquireAsync(); });
        refreshCmd = new Command(() => { _ = doAcquireAsync(); });
        darkCmd = new Command(() => { _ = doDark(); });

        saveCmd = new Command(() => { _ = doSave(); });
        uploadCmd = new Command(() => { _ = doUpload(); });
        addCmd = new Command(() => { _ = doAdd(); });
        clearCmd = new Command(() => { _ = doClear(); });

        if (spec != null && spec.paired)
        {
            spec.laserWatchdogSec = 0;
            spec.laserWarningDelaySec = 0;
        }

        logger.debug("SVM.ctor: updating chart");
        updateChart();

        if (spec != null && spec.paired && spec.eeprom.hasBattery)
            spec.updateBatteryAsync();

        if (spec != null && spec.paired)
            spec.autoRamanEnabled = true;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(paired)));

        laserCmd.ChangeCanExecute();
        acquireCmd.ChangeCanExecute();
        refreshCmd.ChangeCanExecute();
        darkCmd.ChangeCanExecute();

        saveCmd.ChangeCanExecute();
        uploadCmd.ChangeCanExecute();
        addCmd.ChangeCanExecute();
        clearCmd.ChangeCanExecute();

        logger.debug("SVM.ctor: done");
    }

    private void Library_LoadFinished(object sender, Library e)
    {
        foreach (string sample in library.samples)
        {
            if (!fullLibraryOverlayStatus.ContainsKey(sample))
            {
                fullLibraryOverlayStatus.Add(sample, false);
                overlaysViewModel.overlays.Add(new SpectrumOverlayMetadata(sample, false));
            }
        }
    }

    private async Task findUserFiles()
    {
        var cacheDirs = Platform.AppContext.GetExternalFilesDirs(null);
        Java.IO.File libraryFolder = null;
        foreach (var cDir in cacheDirs)
        {
            var subs = await cDir.ListFilesAsync();
            foreach (var sub in subs)
            {
                if (sub.AbsolutePath.Split('/').Last() == "Documents")
                {
                    libraryFolder = sub;
                    break;
                }
            }
        }

        if (libraryFolder == null)
            return;

        Regex csvReg = new Regex(@".*\.csv$");

        var libraryFiles = libraryFolder.ListFiles();

        foreach (var libraryFile in libraryFiles)
        {
            if (libraryFile.IsDirectory)
            {
                findUserFilesDeeper(libraryFile);
            }
            else if (csvReg.IsMatch(libraryFile.AbsolutePath))
            {
                try
                {
                    await addUserFile(libraryFile);
                }
                catch (Exception e)
                {
                    logger.debug("loading {0} failed with exception {1}", libraryFile.AbsolutePath, e.Message);
                }
            }
        }
    }

    async Task findUserFilesDeeper(Java.IO.File folder)
    {
        var libraryFiles = folder.ListFiles();

        foreach (var libraryFile in libraryFiles)
        {
            if (libraryFile.IsDirectory)
            {
                findUserFilesDeeper(libraryFile);
            }
            else
            {
                await addUserFile(libraryFile, true);
            }
        }
    }

    async Task addUserFile(Java.IO.File file, bool addToLibrary = false)
    {
        string name = file.AbsolutePath.Split('/').Last().Split('.').First();
        if (!fullLibraryOverlayStatus.ContainsKey(name))
        {
            if (addToLibrary)
                await loadCSV(file);

            fullLibraryOverlayStatus.Add(name, false);
            overlaysViewModel.overlays.Add(new SpectrumOverlayMetadata(name, false));
        }
    }

    async Task loadCSV(Java.IO.File file)
    {
        logger.info("start loading library file from {0}", file.AbsolutePath);

        string name = file.AbsolutePath.Split('/').Last().Split('.').First();

        SimpleCSVParser parser = new SimpleCSVParser();
        Stream s = File.OpenRead(file.AbsolutePath);
        StreamReader sr = new StreamReader(s);
        await parser.parseStream(s);

        Measurement m = new Measurement();
        m.wavenumbers = parser.wavenumbers.ToArray();
        m.raw = parser.intensities.ToArray();
        m.excitationNM = 785;
        Wavecal wavecal = new Wavecal(spec.pixels);
        wavecal.coeffs = spec.eeprom.wavecalCoeffs;
        wavecal.excitationNM = spec.laserExcitationNM;

        Measurement mOrig = m.copy();

        /*
        double[] smoothedSpec = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed);
        while (smoothedSpec == null || smoothedSpec.Length == 0)
        {
            smoothedSpec = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed);
            await Task.Delay(50);
        }
        */

        if (PlatformUtil.transformerLoaded)
        {
            double[] smoothed = PlatformUtil.ProcessBackground(m.wavenumbers, m.processed, spec.eeprom.serialNumber);
            double[] wavenumbers = Enumerable.Range(400, smoothed.Length).Select(x => (double)x).ToArray();
            Measurement updated = new Measurement();
            updated.wavenumbers = wavenumbers;
            updated.raw = smoothed;
            userDataLibrary.Add(name, updated);
        }

        else
        {
            Measurement updated = wavecal.crossMapWavenumberData(m.wavenumbers, m.raw);
            double airPLSLambda = 10000;
            int airPLSMaxIter = 100;
            double[] array = AirPLS.smooth(updated.processed, airPLSLambda, airPLSMaxIter, 0.001, verbose: false, (int)spec.eeprom.ROIHorizStart, (int)spec.eeprom.ROIHorizEnd);
            double[] shortened = new double[updated.processed.Length];
            Array.Copy(array, 0, shortened, spec.eeprom.ROIHorizStart, array.Length);
            updated.raw = shortened;
            updated.dark = null;

            userDataLibrary.Add(name, updated);
        }

        logger.info("finish loading library file from {0}", file.AbsolutePath);
    }


    public ObservableCollection<string> xAxisNames { get; set; }
    public string xAxisName
    {
        get => _xAxisName;
        set
        {
            _xAxisName = value;
            updateChart();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(xAxisLabelFormat)));
        }
    }
    string _xAxisName = "Wavenumber";

    ////////////////////////////////////////////////////////////////////////
    //
    //                          Bound Properties
    //
    ////////////////////////////////////////////////////////////////////////

    // allows the ScopePage to dis/enable things based on paired status
    public bool paired
    {
        get => BLEDevice.paired || USBViewDevice.paired;
    }

    ////////////////////////////////////////////////////////////////////////
    // X-Axis
    ////////////////////////////////////////////////////////////////////////

    public double xAxisMinimum
    {
        get => _xAxisMinimum;
        set
        {
            _xAxisMinimum = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(xAxisMinimum)));
        }
    }
    double _xAxisMinimum;

    public double xAxisMaximum
    {
        get => _xAxisMaximum;
        set
        {
            _xAxisMaximum = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(xAxisMaximum)));
        }
    }
    double _xAxisMaximum;

    public string xAxisLabelFormat
    {
        get => "F0";
    }

    ////////////////////////////////////////////////////////////////////////
    // dark subtraction
    ////////////////////////////////////////////////////////////////////////

    public Command darkCmd { get; private set; }

    public string darkButtonForegroundColor
    {
        get => spec.dark != null ? "#eee" : "#ccc";
    }

    public string darkButtonBackgroundColor
    {
        get => spec.dark != null ? "#ba0a0a" : "#515151";
    }

    private void updateDarkButton()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(darkButtonForegroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(darkButtonBackgroundColor)));
    }

    bool doDark()
    {
        logger.debug("SVM.doDark: start");
        spec.toggleDark();
        spec.measurement.reload(spec);
        updateDarkButton();
        updateChart();
        logger.debug("SVM.doDark: done");
        return true;
    }

    public string note
    {
        get => spec.note;
        set => spec.note = value;
    }

    ////////////////////////////////////////////////////////////////////////
    // Laser Shenanigans
    ////////////////////////////////////////////////////////////////////////

    public Command laserCmd { get; private set; }
    public Command laserWarningCmd { get; private set; }

    public string laserButtonForegroundColor
    {
        get => spec.laserEnabled ? "#eee" : "#ccc";
    }

    public string laserButtonBackgroundColor
    {
        get => spec.laserEnabled ? "#ba0a0a" : "#515151";
    }

    public string laserWarningBackgroundColor
    {
        get => spec.laserEnabled ? "#ba0a0a" : "#515151";
    }

    private void updateLaserButton()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserButtonForegroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserButtonBackgroundColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserButtonText)));
    }

    bool doLaser()
    {
        logger.debug("SVM.doLaser: start");
        spec.toggleLaser();
        updateLaserButton();
        logger.debug("SVM.doLaser: done");
        return true;
    }

    bool advanceLaserWarning()
    {
        switch (laserWarningStep)
        {
            case 0:
                laserWarningStep = 1;
                laserWarningText = "Onboard Class 3B Laser";
                break;
            case 1: 
                laserWarningStep = 2;
                laserWarningText = "Avoid eye exposure";
                break;
            case 2: 
                laserWarningStep = 3;
                laserWarningText = "Acknowledge to Arm Laser";
                break;
            case 3: 
                laserWarningStep = 4;
                laserArmed = true;
                laserWarningText = "WARNING: Laser Armed";
                break;
            case 4:
                laserWarningStep = 0;
                laserArmed = false;
                laserWarningText = "Click for Laser Warnings";
                break;
        }
        
        return true;
    }



    public string laserWarningText
    {
        get
        {
            return _laserWarningText;
        }
        set
        {
            _laserWarningText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserWarningText)));
        }
    }
    string _laserWarningText = "Click for Laser Warnings";

    public bool laserArmIncomplete
    {
        get
        {
            return laserWarningStep != 4;
        }
    }

    public bool laserArmed
    {
        get
        {
            return _laserArmed;
        }
        set
        {
            _laserArmed = value; 
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserArmed)));
        }
    }
    bool _laserArmed;

    public int laserWarningStep
    { 
        get
        { 
            return _laserWarningStep; 
        }
        set
        {
            _laserWarningStep = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserWarningStep)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserArmIncomplete)));
        }
    }
    int _laserWarningStep = 0;

    public string laserButtonText
    {
        get => laserEnabled ? "Turn Off Laser" : "Turn On Laser";
    }

    public bool laserEnabled
    {
        get => spec.laserEnabled;
        set
        {
            if (spec.laserEnabled != value)
                spec.laserEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserEnabled)));
        }
    }

    // Provided so the "Laser Enable" Switch is disabled if we're in Raman
    // Mode (or battery is low).  Note that unless authenticated, you can't
    // even see this switch.
    public bool laserIsAvailable
    {
        get
        {
            var available = !spec.autoRamanEnabled && !spec.autoDarkEnabled && spec.battery.level >= 5;
            if (!available)
                logger.debug($"laser not available because ramanModeEnabled ({spec.autoRamanEnabled}) or autoDark enabled ({spec.autoDarkEnabled}) or bettery < 5 ({spec.battery.level})");
            return available;
        }
    }

    ////////////////////////////////////////////////////////////////////////
    // Authentication
    ////////////////////////////////////////////////////////////////////////

    // Provided so the View can only show/enable certain controls if we're
    // logged-in.
    public bool isAuthenticated
    {
        get => Settings.getInstance().authenticated;
    }

    public bool manualModeEnabled
    {
        get => spec.acquisitionMode == AcquisitionMode.STANDARD;
    }

    public bool manualModeDisabled
    {
        get => spec.acquisitionMode != AcquisitionMode.STANDARD;
    }

    // Provided so any changes to Settings.authenticated will immediately
    // take effect on our View.
    void handleSettingsChange(object sender, PropertyChangedEventArgs e)
    {
        logger.debug($"SVM.handleSettingsChange: received notification from {sender}, so refreshing isAuthenticated");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isAuthenticated)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(manualModeEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(manualModeDisabled)));

        updateLaserProperties();
    }

    ////////////////////////////////////////////////////////////////////////
    // Refresh
    ////////////////////////////////////////////////////////////////////////

    public bool isRefreshing
    {
        get => _isRefreshing;
        set
        {
            logger.debug($"SVM: isRefreshing -> {value}");
            _isRefreshing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isRefreshing)));
        }
    }

    bool _isRefreshing;

    // invoked by ScopeView when the user pulls-down on the Scope Options grid
    // @todo consider whether this feature should user-configurable, as an 
    //       accidental acquisition could be destructive of both data and 
    //       health (as the laser could auto-fire)
    public Command refreshCmd { get; private set; }

    ////////////////////////////////////////////////////////////////////////
    // Status Bar
    ////////////////////////////////////////////////////////////////////////

    public string spectrumMax
    {
        get => string.Format("Max: {0:f2}", spec.measurement.max);
    }

    public string batteryState
    {
        get => spec.battery.ToString();
    }

    public string batteryColor
    {
        get => spec.battery.level > 20 ? "#eee" : "#f33";
    }

    public bool batteryCharging
    {
        get => spec.battery.charging && spec.battery.level > 15;
    }

    public bool batteryCritical
    {
        get => spec.battery.level < 15;
    }

    public bool battery25
    {
        get => !spec.battery.charging && spec.battery.level >= 15 && spec.battery.level < 39;
    }

    public bool battery50
    {
        get => !spec.battery.charging && spec.battery.level >= 39 && spec.battery.level < 63;
    }
    
    public bool battery75
    {
        get => !spec.battery.charging && spec.battery.level >= 63 && spec.battery.level < 87;
    }

    public bool battery100
    {
        get => !spec.battery.charging && spec.battery.level >= 87;
    }

    public bool ble3Bar
    {
        get => (spec is BluetoothSpectrometer || spec is API6BLESpectrometer) && spec.rssi >= -60; 
    }
    
    public bool ble2Bar
    {
        get => (spec is BluetoothSpectrometer || spec is API6BLESpectrometer) && spec.rssi >= -85 && spec.rssi < -60;
    }
    
    public bool ble1Bar
    {
        get => (spec is BluetoothSpectrometer || spec is API6BLESpectrometer) && spec.rssi < -85;
    }

    public string qrText
    {
        get => spec.qrValue;
        set
        {
            spec.qrValue = value;
            logger.info($"updating qr result value to {value}");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(qrText)));
        }
    }

    public void setQRText(string resultText)
    {
        qrText = resultText;
    }

    public void updateLaserProperties()
    {
        logger.debug("SVM.updateLaserProperties: start");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserIsAvailable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserEnabled)));
        updateLaserButton();
        logger.debug("SVM.updateLaserProperties: done");
    }

    void updateBatteryProperties()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(batteryState)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(batteryColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(batteryCharging)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(batteryCritical)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(battery25)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(battery50)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(battery75)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(battery100)));
    }

    ////////////////////////////////////////////////////////////////////////
    // Photo Command
    ////////////////////////////////////////////////////////////////////////

    /*
    public async void performPhotoCapture()
    {
        var status = await CrossPermissions.Current.CheckPermissionStatusAsync(Permission.Location);

        // if not, prompt the user to authorize it
        if (status != Plugin.Permissions.Abstractions.PermissionStatus.Granted)
        {
            if (await CrossPermissions.Current.ShouldShowRequestPermissionRationaleAsync(Permission.Location))
                notifyUser("Permissions",
                           "ENLIGHTEN Mobile requires Location data for saving photo location",
                           "Ok");

            var result = await CrossPermissions.Current.RequestPermissionsAsync(Permission.Location);
            status = result[Permission.Location];
        }
        try
        {
            var photo = await MediaPicker.CapturePhotoAsync();
            logger.info($"Photo taken.");
            DateTime timestamp = DateTime.Now;
            Settings settings = Settings.getInstance();
            string savePath = settings.getSavePath();
            var serialNumber = spec is null ? "sim" : spec.eeprom.serialNumber;
            string measurementID = string.Format("enlighten-{0}-{1}",
                timestamp.ToString("yyyyMMdd-HHmmss-ffffff"),
                serialNumber);
            string filename = measurementID + ".png";
            string pathname = string.Format($"{savePath}/{filename}");
            using (var stream = await photo.OpenReadAsync())
            {
                using (var writeStream = File.OpenWrite(pathname))
                {
                    await stream.CopyToAsync(writeStream);
                }
            }
            logger.info($"Save photo to file {pathname}");
        }
        catch(Exception e)
        {
            logger.error($"Error while taking photo of {e}");
        }
    }
    */

    ////////////////////////////////////////////////////////////////////////
    // Acquire Command
    ////////////////////////////////////////////////////////////////////////

    void updateAcquireButtonProperties()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(acquireButtonTextColor)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(acquireButtonBackgroundColor)));
    }

    public string acquireButtonBackgroundColor
    {
        // @todo move #ba0a0a to app-wide palette
        get => spec.acquiring ? "#ba0a0a" : "#515151";
    }

    public string acquireButtonTextColor
    {
        get => spec.acquiring ? "#eee" : "#ccc";
    }

    // invoked by ScopeView when the user clicks "Acquire" 
    public Command acquireCmd { get; private set; }

    // the user clicked the "Acquire" button on the Scope View
    async Task<bool> doAcquireAsync()
    {
        if (spec.acquiring)
            return false;

        hasMatch = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(hasMatch)));

        logger.debug("doAcquireAsync: =======================================");
        logger.debug("doAcquireAsync: Attempting to take one averaged reading");
        logger.debug("doAcquireAsync: =======================================");

        if (displayMatch)
        {
            try
            {
                //DataOverlays.Remove(matchCompound);
                //fullLibraryOverlayStatus[matchCompound] = false;
                displayMatch = false;
                await Task.Delay(1);
            }
            catch (Exception ex)
            {
                logger.error("ovelray removal failed out with exception {0}", ex.Message);
            }
        }

        // take a fresh Measurement
        var startTime = DateTime.Now;
        if (spec.autoDarkEnabled || spec.autoRamanEnabled)
        {
            spec.measurement.zero(spec);
            updateChart();
        }

        var ok = await spec.takeOneAveragedAsync();
        if (ok)
        {
            // info-level logging so we can QC timing w/o verbose logging
            var elapsedMS = (DateTime.Now - startTime).TotalMilliseconds;
            logger.info($"Completed acquisition in {elapsedMS} ms");

            updateChart();

            // later we could decide not to graph bad measurements, or not log
            // elapsed time, but this is fine for now
            _ = isGoodMeasurement();
        }
        else
        {
            notifyToast?.Invoke("Error reading spectrum");
        }

        updateBatteryProperties();
        acquisitionProgress = 0;
        isRefreshing = false;

        updateLaserProperties();

        if (PlatformUtil.transformerLoaded && spec.useBackgroundRemoval && spec.performMatch && (spec.dark != null || spec.autoRamanEnabled || spec.autoDarkEnabled))
            doMatchAsync();
        else
            AnalysisViewModel.getInstance().SetData(spec.measurement, null);

        return ok;
    }

    void showAcquisitionProgress(double progress) => acquisitionProgress = progress;

    // this is a floating-point "percentage completion" backing the 
    // ProgressBar on the ScopeView
    public double acquisitionProgress
    {
        get => _acquisitionProgress;
        set
        {
            _acquisitionProgress = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(acquisitionProgress)));
        }
    }
    double _acquisitionProgress;

    bool isGoodMeasurement()
    {
        Measurement m = spec.measurement;
        if (m is null || m.raw is null)
            return false;

        var allZero = true;
        var allHigh = true;
        for (int i = 0; i < m.raw.Length; i++)
        {
            if (m.raw[i] != 0) allZero = false;
            if (m.raw[i] != 65535) allHigh = false;

            // no point checking beyond this point
            if (!allHigh && !allZero)
                return true;
        }

        if (allZero)
            notifyToast?.Invoke("ERROR: spectrum is all zero");
        else if (allHigh)
            notifyToast?.Invoke("ERROR: spectrum is all 0xff");
        return !(allZero || allHigh);
    }

    ////////////////////////////////////////////////////////////////////////
    // Chart
    ////////////////////////////////////////////////////////////////////////

    public bool hasSpectrum { get => spec.lastSpectrum != null; }

    public Command addCmd { get; private set; }
    public Command clearCmd { get; private set; }

    public RadCartesianChart theChart;

    public ObservableCollection<ChartDataPoint> chartData { get; set; } = new ObservableCollection<ChartDataPoint>();
    public Dictionary<string, ObservableCollection<ChartDataPoint>> DataOverlays { get; set; } = new Dictionary<string, ObservableCollection<ChartDataPoint>>();

    // declare statically for now; these are individual Properties because 
    // I don't think I can use databinding against array elements
    public ObservableCollection<ChartDataPoint> trace0 { get; set; } = new ObservableCollection<ChartDataPoint>();
    public ObservableCollection<ChartDataPoint> trace1 { get; set; } = new ObservableCollection<ChartDataPoint>();
    public ObservableCollection<ChartDataPoint> trace2 { get; set; } = new ObservableCollection<ChartDataPoint>();
    public ObservableCollection<ChartDataPoint> trace3 { get; set; } = new ObservableCollection<ChartDataPoint>();
    public ObservableCollection<ChartDataPoint> trace4 { get; set; } = new ObservableCollection<ChartDataPoint>();
    public ObservableCollection<ChartDataPoint> trace5 { get; set; } = new ObservableCollection<ChartDataPoint>();
    public ObservableCollection<ChartDataPoint> trace6 { get; set; } = new ObservableCollection<ChartDataPoint>();
    public ObservableCollection<ChartDataPoint> trace7 { get; set; } = new ObservableCollection<ChartDataPoint>();

    const int MAX_TRACES = 8;
    double[] xAxis;
    int nextTrace = 0;

    public bool hasTraces
    {
        get => _hasTraces;
        set
        {
            _hasTraces = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(hasTraces)));
        }
    }
    bool _hasTraces;

    void setTraceData(int trace, ObservableCollection<ChartDataPoint> data)
    {
        switch (trace)
        {
            case 0: trace0 = data; return;
            case 1: trace1 = data; return;
            case 2: trace2 = data; return;
            case 3: trace3 = data; return;
            case 4: trace4 = data; return;
            case 5: trace5 = data; return;
            case 6: trace6 = data; return;
            case 7: trace7 = data; return;
        }
    }
    ObservableCollection<ChartDataPoint> getTraceData(int trace)
    {
        switch (trace)
        {
            case 0: return trace0;
            case 1: return trace1;
            case 2: return trace2;
            case 3: return trace3;
            case 4: return trace4;
            case 5: return trace5;
            case 6: return trace6;
            case 7: return trace7;
        }
        return trace0;
    }

    string getTraceName(int trace)
    {
        switch (trace)
        {
            case 0: return nameof(trace0);
            case 1: return nameof(trace1);
            case 2: return nameof(trace2);
            case 3: return nameof(trace3);
            case 4: return nameof(trace4);
            case 5: return nameof(trace5);
            case 6: return nameof(trace6);
            case 7: return nameof(trace7);
        }
        return nameof(trace0);
    }

    void updateChart()
    {
        logger.debug("updateChart: start");
        refreshChartData();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(chartData)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(spectrumMax)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(hasSpectrum)));
        logger.debug("updateChart: done");
    }

    void updateTrace(int trace) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(getTraceName(trace)));

    private void refreshChartData()
    {
        logger.debug("refreshChartData: start");

        // use last Measurement from the Spectrometer
        uint pixels = (uint)spec.measurement.postProcessed.Length;
        double[] intensities = spec.measurement.postProcessed;

        bool usingRemovalAxis = PlatformUtil.transformerLoaded && spec.useBackgroundRemoval && (spec.measurement.dark != null || spec.autoDarkEnabled || spec.autoRamanEnabled);

        try
        {
            xAxis = null;
            if (xAxisName == "Wavelength")
                xAxis = spec.wavelengths;
            else if (xAxisName == "Wavenumber")
            {
                if (usingRemovalAxis && spec.measurement.wavenumbers != null)
                    xAxis = spec.measurement.wavenumbers;
                else
                    xAxis = spec.wavenumbers;
            }
            else
                xAxis = spec.xAxisPixels;

            logger.debug($"SVM.refreshChartData: using x-axis {xAxisName}");
            if (intensities is null || xAxis is null || xAxis.Length == 0)
            {
                logger.error("SVM.refreshChartData: no x-axis or intensities");
                return;
            }

            logger.info("populating ChartData");
            var updateChartData = new ObservableCollection<ChartDataPoint>();

            int pxLo = -1;
            int pxHi = -1;
            for (int i = 0; i < pixels; i++)
            {
                if (!usingRemovalAxis &&
                    spec.useHorizontalROI &&
                    spec.eeprom.ROIHorizStart != spec.eeprom.ROIHorizEnd &&
                    (i < spec.eeprom.ROIHorizStart || i > spec.eeprom.ROIHorizEnd))
                    continue;

                updateChartData.Add(new ChartDataPoint() { intensity = intensities[i], xValue = xAxis[i] });
                if (pxLo < 0)
                    pxLo = i;
                pxHi = i;
            }

            if (pxLo == pxHi)
            {
                logger.error("Bad axis data...giving up");
                return;
            }

            //var oldChartData = chartData;
            //chartData = updateChartData;
            chartData.Clear();
            foreach (ChartDataPoint chartDataPoint in updateChartData)
            {
                chartData.Add(chartDataPoint);
            }

            //oldChartData.Clear();

            xAxisMinimum = xAxis[pxLo];
            xAxisMaximum = xAxis[pxHi];
            logger.debug($"refreshChartData: pxLo {pxLo}, pxHi {pxHi}, xMin {xAxisMinimum:f2}, xMax {xAxisMaximum:f2}");
        }
        catch (Exception ex)
        {
            logger.debug($"refreshChartData: caught exception {ex}");
        }
        logger.debug("refreshChartData: done");
    }

    bool doAdd()
    {
        /*
        logger.debug("Add button pressed");
        var name = getTraceName(nextTrace);
        logger.debug($"Populating trace {name}");
        var newData = new ObservableCollection<ChartDataPoint>();
        foreach (var orig in chartData)
            newData.Add(new ChartDataPoint() { xValue = orig.xValue, intensity = orig.intensity });
        setTraceData(nextTrace, newData);
        updateTrace(nextTrace);
        nextTrace = (nextTrace + 1) % MAX_TRACES;
        hasTraces = true;
        return true; */


        //saveViewModel = new SaveSpectrumPopupViewModel(spec.measurement.filename.Split('.')[0]);
        //saveViewModel.PropertyChanged += SaveViewModel_PropertyChanged;
        //savePopup = new SaveSpectrumPopup(saveViewModel);
        //Shell.Current.ShowPopup<SaveSpectrumPopup>(savePopup);

        OverlaysPopup op = new OverlaysPopup(overlaysViewModel);
        op.Closed += Op_Closed;
        Shell.Current.ShowPopupAsync<OverlaysPopup>(op);


        return true;


    }

    private void Op_Closed(object sender, PopupClosedEventArgs e)
    {
        bool somethingChanged = false;

        foreach (SpectrumOverlayMetadata omd in overlaysViewModel.overlays)
        {
            bool wasDisplayed = fullLibraryOverlayStatus[omd.name];

            fullLibraryOverlayStatus[omd.name] = omd.selected;

            if (wasDisplayed != omd.selected)
            {
                somethingChanged = true;
                if (!wasDisplayed)
                {
                    ObservableCollection<ChartDataPoint> newOverlay = new ObservableCollection<ChartDataPoint>();
                    Measurement m = library.getSample(omd.name);
                    if (m == null && userDataLibrary.ContainsKey(omd.name))
                    {
                        m = userDataLibrary[omd.name];

                    }

                    if (m != null)
                    {
                        for (int i = 0; i < m.wavenumbers.Length; i++)
                            newOverlay.Add(new ChartDataPoint() { intensity = m.processed[i], xValue = m.wavenumbers[i] });
                        if (DataOverlays.ContainsKey(omd.name))
                            DataOverlays[omd.name] = newOverlay;
                        else
                            DataOverlays.Add(omd.name, newOverlay);
                    }
                }
                else
                {
                    DataOverlays.Remove(omd.name);
                }

            }
        }

        if (fullLibraryOverlayStatus.ContainsKey(matchCompound))
            displayMatch = fullLibraryOverlayStatus[matchCompound];

        if (somethingChanged)
            OverlaysChanged.Invoke(this, this);
    }

    bool doClear()
    {
        displayMatch = false;
        hasTraces = false;

        WipeOverlays.Invoke(this, this);

        foreach (var overlay in overlaysViewModel.overlays)
        {
            overlay.selected = false;
        }
        foreach (string overlayName in fullLibraryOverlayStatus.Keys)
        {
            fullLibraryOverlayStatus[overlayName] = false;
        }

        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // Save Command
    ////////////////////////////////////////////////////////////////////////

    public Command saveCmd { get; private set; }

    // the user clicked the "Save" button on the Scope View
    async Task<bool> doSave()
    {
        DisplayPopup();
        return true;
    }

    public void DisplayPopup()
    {
        //this.popupService.ShowPopup<SaveSpectrumPopupViewModel>();
        saveViewModel = new SaveSpectrumPopupViewModel(spec.measurement.filename.Split('.')[0]);
        saveViewModel.PropertyChanged += SaveViewModel_PropertyChanged;
        savePopup = new SaveSpectrumPopup(saveViewModel);
        popupClosing = false;
        Shell.Current.ShowPopup<SaveSpectrumPopup>(savePopup);
    }

    private async void SaveViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (saveViewModel != null)
        {
            if (e.PropertyName == nameof(saveViewModel.toBeSaved) && saveViewModel.toBeSaved)
            {

                if (!userDataLibrary.ContainsKey(saveViewModel.saveName)) 
                    userDataLibrary.Add(saveViewModel.saveName, spec.measurement);
                else
                {
                    string name = saveViewModel.saveName.Split('-')[0];
                    int count = 0;
                    foreach (string sample in userDataLibrary.Keys)
                    {
                        if (sample.StartsWith(name))
                        {
                            ++count;
                        }
                    }

                    name += "-";
                    name += count.ToString();

                    saveViewModel.saveName = name;
                    userDataLibrary.Add(saveViewModel.saveName, spec.measurement);
                }

                spec.measurement.filename = saveViewModel.saveName + ".csv";
                spec.measurement.notes = saveViewModel.notes;

                if (!fullLibraryOverlayStatus.ContainsKey(saveViewModel.saveName))
                    fullLibraryOverlayStatus.Add(saveViewModel.saveName, saveViewModel.addToDisplay);
                else
                    fullLibraryOverlayStatus[saveViewModel.saveName] = saveViewModel.addToDisplay;

                overlaysViewModel.overlays.Add(new SpectrumOverlayMetadata(saveViewModel.saveName, saveViewModel.addToDisplay));

                if (saveViewModel.addToLibrary && library.getSample(saveViewModel.saveName) == null)
                {
                    library.addSampleToLibrary(saveViewModel.saveName, spec.measurement);
                }


                if (saveViewModel.addToDisplay)
                {

                    ObservableCollection<ChartDataPoint> newOverlay = new ObservableCollection<ChartDataPoint>();
                    Measurement m = spec.measurement;

                    if (m != null)
                    {
                        for (int i = 0; i < m.wavenumbers.Length; i++)
                            newOverlay.Add(new ChartDataPoint() { intensity = m.postProcessed[i], xValue = m.wavenumbers[i] });
                        if (DataOverlays.ContainsKey(saveViewModel.saveName))
                            DataOverlays[saveViewModel.saveName] = newOverlay;
                        else
                            DataOverlays.Add(saveViewModel.saveName, newOverlay);

                        OverlaysChanged.Invoke(this, this);
                    }

                }

                var ok = await spec.measurement.saveAsync();
                if (ok)
                {
                    notifyToast?.Invoke($"saved {spec.measurement.filename}");
                }
            }
        }

        if (e.PropertyName == nameof(saveViewModel.toBeSaved) && !popupClosing)
        {
            popupClosing = true;
            await savePopup.CloseAsync();
        }
    }

    // This is required, but I don't remember how / why
    protected void OnPropertyChanged([CallerMemberName] string caller = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(caller));
    }

    // Provided so Spectrometer notifications can be displayed to our View
    void handleSpectrometerChange(object sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        logger.debug($"SVM.handleSpectrometerChange: received notification from {sender} that property {name} changed");

        if (name == "acquiring")
            updateAcquireButtonProperties();
        else if (name == "batteryStatus")
            updateBatteryProperties();
        else if (name == "paired")
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(paired)));
        else if (name == "laserState" || name == "ramanModeEnabled" || name == "laserEnabled")
            updateLaserProperties();
        else if (name == "rssi")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ble1Bar)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ble2Bar)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ble3Bar)));
        }


    }

    ////////////////////////////////////////////////////////////////////////
    // Upload Command
    ////////////////////////////////////////////////////////////////////////

    public Command uploadCmd { get; private set; }

    // the user clicked the "Upload" button on the Scope View
    async Task<bool> doUpload()
    {
        var ok = await spec.measurement.uploadAsync();
        if (ok)
            notifyToast?.Invoke($"uploaded {spec.measurement.filename}");
        else
            notifyToast?.Invoke("upload failed");
        return ok;
    }

    ////////////////////////////////////////////////////////////////////////
    // Matching
    ////////////////////////////////////////////////////////////////////////

    public Command matchCmd { get; private set; }
    public bool displayMatch
    {
        get { return _displayMatch; }
        set 
        {
            _displayMatch = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(displayMatch)));

            if (!fullLibraryOverlayStatus.ContainsKey(matchCompound))
                return;

            bool wasDisplayed = fullLibraryOverlayStatus[matchCompound];
            bool selected = value;

            fullLibraryOverlayStatus[matchCompound] = value;

            if (wasDisplayed != selected)
            {
                foreach (var o in overlaysViewModel.overlays)
                {
                    if (o.name == matchCompound)
                        o.selected = selected;
                }

                if (!wasDisplayed)
                {
                    ObservableCollection<ChartDataPoint> newOverlay = new ObservableCollection<ChartDataPoint>();
                    Measurement m = library.getSample(matchCompound);
                    if (m == null && userDataLibrary.ContainsKey(matchCompound))
                    {
                        m = userDataLibrary[matchCompound];

                    }

                    if (m != null)
                    {
                        for (int i = 0; i < m.wavenumbers.Length; i++)
                            newOverlay.Add(new ChartDataPoint() { intensity = m.postProcessed[i], xValue = m.wavenumbers[i] });
                        if (DataOverlays.ContainsKey(matchCompound))
                            DataOverlays[matchCompound] = newOverlay;
                        else
                            DataOverlays.Add(matchCompound, newOverlay);
                    }
                }
                else
                {
                    DataOverlays.Remove(matchCompound);
                }

                OverlaysChanged.Invoke(this, this);
            }

        }
    }
    bool _displayMatch;
    public bool hasMatchingLibrary {get; private set;}
    public bool hasMatch {get; private set;}
    public bool hasDecon {get; private set;}
    public bool waitingForMatch {get; private set;}
    public string matchResult {get; private set;}
    string matchCompound = "";
    public string deconResult {get; private set;}

    public const double MATCH_THRESHOLD = 0.6;

    async Task<bool> doMatchAsync()
    {
        logger.info("calling library match function");

        waitingForMatch = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(waitingForMatch)));
        var result = await library.findMatch(spec.measurement);

        if (result != null)
        {
            logger.info("returned from library match function with result {0}", result);

            if (result.Item2 >= MATCH_THRESHOLD)
            {
                matchCompound = result.Item1;
                matchResult = String.Format("{0} : {1:f2}", result.Item1, result.Item2);
                hasMatch = true;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(hasMatch)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchResult)));


                AnalysisViewModel.getInstance().SetData(spec.measurement, library.mostRecentMeasurement);

                await Shell.Current.GoToAsync("//AnalysisPage");

                //if (fullLibraryOverlayStatus.ContainsKey(matchCompound) && fullLibraryOverlayStatus[matchCompound])
                    //displayMatch = true;
            }
            else
            {
                AnalysisViewModel.getInstance().SetData(spec.measurement, null);
            }

        }
        else
        {
            hasMatch = false;
            AnalysisViewModel.getInstance().SetData(spec.measurement, null);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(hasMatch)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchResult)));
        }

        if (spec.performDeconvolution)
        {
#if USE_DECON
            var matched_spectra = await library.findDeconvolutionMatches(spec.measurement);
            string matched = "";
            string deconS = "";
            string alts = "";
            logger.info("finished deconvolution match, setting match result string");
            foreach (double match_concentrations in matched_spectra.compounds.Keys)
            {
                string[] matches = matched_spectra.compounds[match_concentrations].ToArray();
                matched += match_concentrations.ToString("F1") + "%: ";
                deconS += match_concentrations.ToString("F1") + "%: ";
                logger.info($"match value of {match_concentrations}");
                foreach (string match in matches)
                {
                    matched += match + " ";
                    deconS += match + " ";
                    logger.info($"For this matched {match}");
                }
                matched += "\n";
            }
            foreach (string alternate in matched_spectra.alternatives)
            {
                alts += alternate + " ";
            }
            logger.info("combining results string");
            string decon = $"Matches: \n{(matched == "" ? "None\n" : matched)}Alternative: \n{alts}";
            logger.info("deconvolution results: {0}", decon);

            if (matched != "")
            {
                deconResult = deconS;
                hasDecon = true;
                logger.info("deconvolution matches: {0}", deconResult);
            }
            else
            {
                hasDecon = false;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(hasDecon)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(deconResult)));
#endif
        }

        waitingForMatch = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(waitingForMatch)));

        return true;
    }
}
