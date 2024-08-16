using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Telerik.Maui.Controls.Compatibility.Chart;

using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels;

// This class provides all the business logic controlling the ScopeView. 
public class ScopeViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    // So the ScopeViewModel can float-up Toast events to the ScopeView.
    // This probably could be done using notifications, but I'm not sure I
    // want to make a "public string toastMessage" Property, and I'm not
    // sure what the "best practice" architecture would be.
    public delegate void ToastNotification(string msg);
    public event ToastNotification notifyToast;

    ////////////////////////////////////////////////////////////////////////
    // Private attributes
    ////////////////////////////////////////////////////////////////////////

    public BluetoothSpectrometer spec;
    Settings settings;

    Logger logger = Logger.getInstance();

    public delegate void UserNotification(string title, string message, string button);
    public event UserNotification notifyUser;

    ////////////////////////////////////////////////////////////////////////
    // Lifecycle
    ////////////////////////////////////////////////////////////////////////

    public ScopeViewModel()
    {
        logger.debug("SVM.ctor: start");

        spec = BluetoothSpectrometer.getInstance();
        settings = Settings.getInstance();

        settings.PropertyChanged += handleSettingsChange;
        spec.PropertyChanged += handleSpectrometerChange;
        spec.showAcquisitionProgress += showAcquisitionProgress; 
        spec.measurement.PropertyChanged += handleSpectrometerChange;

        // bind ScopePage Commands
        laserCmd   = new Command(() => { _ = doLaser       (); }); 
        acquireCmd = new Command(() => { _ = doAcquireAsync(); });
        refreshCmd = new Command(() => { _ = doAcquireAsync(); }); 
        darkCmd    = new Command(() => { _ = doDark        (); });

        saveCmd    = new Command(() => { _ = doSave        (); });
        uploadCmd  = new Command(() => { _ = doUpload      (); });
        addCmd     = new Command(() => { _ = doAdd         (); });
        clearCmd   = new Command(() => { _ = doClear       (); });
        matchCmd   = new Command(() => { _ = doMatchAsync  (); });

        xAxisNames = new ObservableCollection<string>();
        xAxisNames.Add("Pixel");
        xAxisNames.Add("Wavelength");
        xAxisNames.Add("Wavenumber");

        logger.debug("SVM.ctor: updating chart");
        updateChart();
        
        logger.debug("SVM.ctor: done");
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
        get => BLEDevice.paired;
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
        get => xAxisName == "Pixel" ? "F0" : "F2";
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
    // dark subtraction
    ////////////////////////////////////////////////////////////////////////

    public Command darkCmd { get; }

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

    ////////////////////////////////////////////////////////////////////////
    // misc acquisition parameters
    ////////////////////////////////////////////////////////////////////////

    // @todo: let the user live-toggle this and update the on-screen spectrum
    public bool useHorizontalROI 
    { 
        get => _useHorizontalROI;
        set
        {
            _useHorizontalROI = value;
            updateChart();
        }
    }
    private bool _useHorizontalROI = true;

    // @todo: let the user live-toggle this and update the on-screen spectrum
    public bool useRamanIntensityCorrection 
    { 
        get => spec.useRamanIntensityCorrection;
        set => spec.useRamanIntensityCorrection = value;
    }

    public string note
    {
        get => spec.note;
        set => spec.note = value;
    }

    ////////////////////////////////////////////////////////////////////////
    // Laser Shenanigans
    ////////////////////////////////////////////////////////////////////////

    public Command laserCmd { get; }

    public string laserButtonForegroundColor
    {
        get => spec.laserEnabled ? "#eee" : "#ccc";
    }

    public string laserButtonBackgroundColor
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

    public bool ramanModeEnabled
    {
        get => spec.ramanModeEnabled;
        set
        {
            if (spec.ramanModeEnabled != value)
                spec.ramanModeEnabled = value;
            updateLaserProperties();
        }
    }

    // Provided so the "Laser Enable" Switch is disabled if we're in Raman
    // Mode (or battery is low).  Note that unless authenticated, you can't
    // even see this switch.
    public bool laserIsAvailable
    {
        get
        {
            var available = !ramanModeEnabled && spec.battery.level >= 5;
            if (!available)
                logger.debug($"laser not available because ramanModeEnabled ({ramanModeEnabled}) or bettery < 5 ({spec.battery.level})");
            return available;
        }
    }

    public void updateLaserProperties()
    { 
        logger.debug("SVM.updateLaserProperties: start");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserIsAvailable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ramanModeEnabled)));
        logger.debug("SVM.updateLaserProperties: done");
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

    // Provided so any changes to Settings.authenticated will immediately
    // take effect on our View.
    void handleSettingsChange(object sender, PropertyChangedEventArgs e)
    {
        logger.debug($"SVM.handleSettingsChange: received notification from {sender}, so refreshing isAuthenticated");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isAuthenticated)));

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
    public Command refreshCmd { get; }

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

    void updateBatteryProperties()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(batteryState)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(batteryColor)));
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
    public Command acquireCmd { get; }

    // the user clicked the "Acquire" button on the Scope View
    async Task<bool> doAcquireAsync()
    {
        if (spec.acquiring)
            return false;

        logger.debug("doAcquireAsync: =======================================");
        logger.debug("doAcquireAsync: Attempting to take one averaged reading");
        logger.debug("doAcquireAsync: =======================================");

        // take a fresh Measurement
        var startTime = DateTime.Now;
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
            if (m.raw[i] !=     0) allZero = false;
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

    public Command addCmd { get; }
    public Command clearCmd { get; }

    public RadCartesianChart theChart;

    public ObservableCollection<ChartDataPoint> chartData { get; set; } = new ObservableCollection<ChartDataPoint>();

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
        switch(trace)
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
        switch(trace)
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
        uint pixels = spec.pixels;
        double[] intensities = spec.measurement.processed;

        try
        {
            xAxis = null;
            if (xAxisName == "Wavelength")
                xAxis = spec.wavelengths;
            else if (xAxisName == "Wavenumber")
                xAxis = spec.wavenumbers;
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
                if (useHorizontalROI && 
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
        return true; 
    }

    bool doClear()
    {
        logger.debug("Clear button pressed");
        for (int i = 0; i < MAX_TRACES; i++)
        {
            getTraceData(i).Clear();
            updateTrace(i);
        }
        nextTrace = 0;
        hasTraces = false;
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // Save Command
    ////////////////////////////////////////////////////////////////////////

    public Command saveCmd { get; }

    // the user clicked the "Save" button on the Scope View
    async Task<bool> doSave()
    {
        var ok = await spec.measurement.saveAsync();
        if (ok)
            notifyToast?.Invoke($"saved {spec.measurement.filename}");
        return ok;
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
    }

    ////////////////////////////////////////////////////////////////////////
    // Upload Command
    ////////////////////////////////////////////////////////////////////////

    public Command uploadCmd { get; }

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

    public Command matchCmd { get; }
    public bool hasMatchingLibrary {get; private set;}
    public bool hasMatch {get; private set;}
    public string matchResult {get; private set;}

    async Task<bool> doMatchAsync()
    {
        return true;
    }
}
