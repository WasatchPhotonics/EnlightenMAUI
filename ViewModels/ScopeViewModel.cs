using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

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

    public Spectrometer spec;
    Settings settings;

    Logger logger = Logger.getInstance();

    // public delegate void UserNotification(string title, string message, string button);
    // public event UserNotification notifyUser;

    ////////////////////////////////////////////////////////////////////////
    // Lifecycle
    ////////////////////////////////////////////////////////////////////////

    public ScopeViewModel()
    {
        logger.debug("SVM.ctor: start");

        spec = Spectrometer.getInstance();
        settings = Settings.getInstance();

        settings.PropertyChanged += handleSettingsChange;
        spec.PropertyChanged += handleSpectrometerChange;
        spec.showAcquisitionProgress += showAcquisitionProgress; // closure?

        // bind closures (method calls) to each Command
        acquireCmd = new Command(() => { _ = doAcquireAsync(); });
        refreshCmd = new Command(() => { _ = doAcquireAsync(); }); 
        saveCmd    = new Command(() => { _ = doSave        (); });
        addCmd     = new Command(() => { _ = doAdd         (); });
        clearCmd   = new Command(() => { _ = doClear       (); });
        integCmd   = new Command(() => { _ = latchInteg    (); });
        gainCmd    = new Command(() => { _ = latchGain     (); });
        avgCmd     = new Command(() => { _ = latchAvg      (); });

        xAxisOptions = new ObservableCollection<XAxisOption>()
        {
            // these names must match the fields in ChartDataPoint
            new XAxisOption() { name = "pixel", unit = "px" },
            new XAxisOption() { name = "wavelength", unit = "nm" },
            new XAxisOption() { name = "wavenumber", unit = "cm⁻¹" }
        };
        xAxisOption = xAxisOptions[0];

        logger.debug("SVM.ctor: updating chart");
        updateChart();
        
        logger.debug("SVM.ctor: done");
    }

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

    public ObservableCollection<XAxisOption> xAxisOptions { get; set; }
    public XAxisOption xAxisOption
    {
        get => _xAxisOption;
        set
        {
            logger.debug($"xAxisOption -> {value}");
            _xAxisOption = value;
            updateChart();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(xAxisLabelFormat)));
        }
    }
    XAxisOption _xAxisOption;

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
        get => xAxisOption.name == "pixel" ? "F0" : "F2";
    }

    ////////////////////////////////////////////////////////////////////////
    // integrationTimeMS
    ////////////////////////////////////////////////////////////////////////

    // This is the value backing the Scope View Slider. When the user drags
    // the slider, it sets the value here. This property is not automatically
    // sync'd to the Spectrometer and does not automatically get written out
    // to the device over BLE. After the user RELEASES the Slider at its final
    // position, THEN it will call integTimeCmd, which will READ the last-set
    // value in this property, and flow that downstream.

    public UInt32 integSlider 
    {
        get => _integSlider;
        set
        {
            logger.debug($"integSlider: {value}ms");
            _integSlider = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(label_integ)));
        }
    }
    private UInt32 _integSlider = 400;
    public string label_integ { get => $"Integration Time: {integSlider}ms"; }
    public Command integCmd { get; }
    bool latchInteg()
    {
        UInt32 value = integSlider;
        logger.debug($"latchInteg: sending current slider value {value} downstream");
        spec.integrationTimeMS = value;
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // gainDb
    ////////////////////////////////////////////////////////////////////////

    public float gainSlider
    {
        get => _gainSlider;
        set
        {
            logger.debug($"gainSlider: {value}dB");
            _gainSlider = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(label_gain)));
        }
    }
    private float _gainSlider = 8;
    public string label_gain { get => $"Gain: {gainSlider}dB"; }
    public Command gainCmd { get; }
    bool latchGain()
    {
        float value = gainSlider;
        logger.debug($"latchGain: sending current slider value {value} downstream");
        spec.gainDb = value;
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // scansToAverage
    ////////////////////////////////////////////////////////////////////////

    public Byte avgSlider
    {
        get => _avgSlider;
        set
        {
            logger.debug($"avgSlider: {value}");
            _avgSlider = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(label_avg)));
        }
    }
    private Byte _avgSlider = 1;
    public string label_avg { get => $"Scan Averaging: {avgSlider}"; }
    public Command avgCmd { get; }
    bool latchAvg()
    {
        Byte value = avgSlider;
        logger.debug($"latchAvg: sending current slider value {value} downstream");
        spec.scansToAverage = value;
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // misc acquisition parameters
    ////////////////////////////////////////////////////////////////////////

    public bool darkEnabled
    {
        get => spec.dark != null;
        set
        {
            spec.toggleDark();
            spec.measurement.reload(spec);
            updateChart();
        }
    }

    public string note
    {
        get => spec.note;
        set => spec.note = value;
    }

    ////////////////////////////////////////////////////////////////////////
    // Laser Shenanigans
    ////////////////////////////////////////////////////////////////////////

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

    public void updateLaserProperties()
    { 
        logger.debug("SVM.updateLaserProperties: start");
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserIsAvailable)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(laserEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ramanModeEnabled)));
        logger.debug("SVM.updateLaserProperties: done");
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
        get => spec.acquiring ? "#ba0a0a" : "#ccc";
    }

    public string acquireButtonTextColor
    {
        get => spec.acquiring ? "#fff" : "#333";
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

    double[] xAxis;
    string lastAxisType;
    int nextTrace = 0;
    const int MAX_TRACES = 8;
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
            // pick our x-axis
            if (lastAxisType != null && lastAxisType == xAxisOption.name)
            {
                // re-use previous axis
            }
            else 
            { 
                xAxis = null;
                if (xAxisOption.name == "wavelength")
                    xAxis = spec.wavelengths;
                else if (xAxisOption.name == "wavenumber")
                    xAxis = spec.wavenumbers;
                else
                    xAxis = spec.xAxisPixels;

                lastAxisType = xAxisOption.name;
            }
            if (intensities is null || xAxis is null)
                return;

            logger.info("populating ChartData");
            var updateChartData = new ObservableCollection<ChartDataPoint>();

            for (int i = 0; i < pixels; i++)
                updateChartData.Add(new ChartDataPoint() { intensity = intensities[i], xValue = xAxis[i] });

            chartData = updateChartData;

            xAxisMinimum = xAxis[0];
            xAxisMaximum = xAxis[pixels-1];
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

        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // Save Command
    ////////////////////////////////////////////////////////////////////////

    // invoked by ScopeView when the user clicks "Save" 
    public Command saveCmd { get; }

    // the user clicked the "Save" button on the Scope View
    bool doSave()
    {
        var ok = spec.measurement.save();
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

    // testing kludge
    // void refreshAll_NOT_USED()
    // {
    //     // there's probably a way to iterate over Properties via Reflection
    //     string[] names = {
    //         "title", "paired", "xAxisOptions", "xAxisOption", "xAxisMinimum", "xAxisMaximum",
    //         "xAxisLabelFormat", "integrationTimeMS", "gainDb", "scansToAverage", "darkEnabled",
    //         "note", "laserEnabled", "ramanModeEnabled", "laserIsAvailable", "isAuthenticated",
    //         "isRefreshing", "spectrumMax", "batteryState", "batteryColor", "acquireButtonBackgroundColor",
    //         "acquireButtonTextColor", "chartData", "trace0", "trace1", "trace2", "trace3", "trace4",
    //         "trace5", "trace6", "trace7" };
    //     foreach (var name in names)
    //         PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    // }
}
