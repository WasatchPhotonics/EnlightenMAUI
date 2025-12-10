using Accord.Statistics.Testing.Power;
using CommunityToolkit.Maui.Views;
using EnlightenMAUI.Models;
using EnlightenMAUI.Platforms;
using EnlightenMAUI.Popups;
using Microsoft.Maui.ApplicationModel;
using Google.Android.Material.Shape;
using MathNet.Numerics.Statistics;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static Android.Widget.GridLayout;
using Telerik.Maui.Controls;
using Bumptech.Glide.Util;
using System.Runtime.Serialization;

namespace EnlightenMAUI.ViewModels
{
    internal class AnalysisViewModel : INotifyPropertyChanged
    {
        Settings settings = Settings.getInstance();
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<AnalysisViewModel> SpectraChanged;
        public event EventHandler<AnalysisViewModel> TriggerRetry;
        public event EventHandler<AnalysisViewModel> TriggerIncreasedPrecision;
        public delegate void ToastNotification(string msg);
        public event ToastNotification notifyToast;
        Measurement lastMeas;
        SelectionPopupViewModel sublibraryViewModel = new SelectionPopupViewModel();

        Logger logger = Logger.getInstance();
        public Library library;
        public Spectrometer spec;
        static AnalysisViewModel instance = null; 
        SaveSpectrumPopupViewModel saveViewModel;
        AddToLibraryPopup savePopup;
        bool popupClosing = false;
        double[] xAxis;

        static public AnalysisViewModel getInstance()
        {
            if (instance is null)
                instance = new AnalysisViewModel(true);
            return instance;
        }

        public AnalysisViewModel()
        {
            //scatterData.Add(new ChartDataPoint() { intensity = 0, xValue = 0 });

            spec = BluetoothSpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = API6BLESpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = USBSpectrometer.getInstance();

            shareCmd = new Command(() => { _ = ShareSpectrum(); });
            saveCmd = new Command(() => { _ = doSave(); });
            addCmd = new Command(() => { _ = doAdd(); });
            correctionCmd = new Command(() => { _ = changeCorrection(); });
            retryCmd = new Command(() => { _ = triggerReanalyze(); });
            precisionCmd = new Command(() => { _ = triggerPrecision(); });

            if (instance != null)
            {
                updateFromInstance();
                //addAndWait();
            }
            else
                SetData(null, null);

            if (settings.library is DPLibrary)
            {
                _currentLibrary = "3rd Party";
                foreach (var pair in (settings.library as DPLibrary).LibraryOptions)
                {
                    sublibraryViewModel.selections.Add(new SelectionMetadata(pair.Item1, pair.Item2));
                }
            }
            else if (settings.library is WPLibrary)
                _currentLibrary = "Wasatch";


            settings.LibraryChanged += Settings_LibraryChanged;
            Spectrometer.NewConnection += handleNewSpectrometer;
            getInstance().PropertyChanged += AnalysisViewModel_PropertyChanged;
            getInstance().SpectraChanged += AnalysisViewModel_SpectraChanged;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isVIS)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isNotVIS)));
        }

        public AnalysisViewModel(bool isInstance = false)
        {
            //scatterData.Add(new ChartDataPoint() { intensity = 0, xValue = 0 });

            spec = BluetoothSpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = API6BLESpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = USBSpectrometer.getInstance();

            shareCmd = new Command(() => { _ = ShareSpectrum(); });
            addCmd = new Command(() => { _ = doAdd(); });
            correctionCmd = new Command(() => { _ = changeCorrection(); });
            retryCmd = new Command(() => { _ = triggerReanalyze(); });
            precisionCmd = new Command(() => { _ = triggerPrecision(); });

            SetData(null, null);

            Spectrometer.NewConnection += handleNewSpectrometer;
            if (!isInstance)
            {
                getInstance().PropertyChanged += AnalysisViewModel_PropertyChanged;
                getInstance().SpectraChanged += AnalysisViewModel_SpectraChanged;
            }
        }

        public Command shareCmd { get; private set; }
        public Command addCmd { get; private set; }
        public Command saveCmd { get; private set; }
        public Command correctionCmd { get; private set; }
        public Command retryCmd { get; private set; }
        public Command precisionCmd { get; private set; }

        bool triggerReanalyze()
        {
            TriggerRetry?.Invoke(this, this);
            return true;
        }

        bool triggerPrecision()
        {
            TriggerIncreasedPrecision?.Invoke(this, this);
            return true;
        }

        private void AnalysisViewModel_SpectraChanged(object sender, AnalysisViewModel e)
        {
            updateFromInstance();
        }


        async Task addAndWait()
        {
            await Task.Delay(2000);

            int index = 0;
            while (true)
            {
                scatterData.Add(new ChartDataPoint() { xValue = index, intensity = index});
                index++;
                await Task.Delay(200);
            }
        }


        //
        // need to resolve the parameter set duplicates here and in settings
        //

        Dictionary<string, AutoRamanParameters> parameterSets = new Dictionary<string, AutoRamanParameters>()
        {
            {
                "Default" ,
                new AutoRamanParameters()
                {
                    maxCollectionTimeMS = 10000,
                    startIntTimeMS = 100,
                    startGainDb = 0,
                    minIntTimeMS = 10,
                    maxIntTimeMS = 2000,
                    minGainDb = 0,
                    maxGainDb = 30,
                    targetCounts = 45000,
                    minCounts = 40000,
                    maxCounts = 50000,
                    maxFactor = 5,
                    dropFactor = 0.5f,
                    saturationCounts = 65000,
                    maxAverage = 100
                }
            },
            {
                "Faster" ,
                new AutoRamanParameters()
                {
                    maxCollectionTimeMS = 2000,
                    startIntTimeMS = 200,
                    startGainDb = 8,
                    minIntTimeMS = 10,
                    maxIntTimeMS = 1000,
                    minGainDb = 0,
                    maxGainDb = 30,
                    targetCounts = 40000,
                    minCounts = 30000,
                    maxCounts = 50000,
                    maxFactor = 10,
                    dropFactor = 0.5f,
                    saturationCounts = 65000,
                    maxAverage = 1
                }
            }

        };

        public ObservableCollection<string> paramSets
        {
            get => _paramSets;
        }

        static ObservableCollection<string> _paramSets = new ObservableCollection<string>()
        {
            "Default",
            "Faster"
        };

        public ObservableCollection<string> compLibrary
        {
            get => _compLibrary;
        }

        static ObservableCollection<string> _compLibrary = new ObservableCollection<string>()
        {
            "Wasatch",
            "3rd Party"
        };

        public string currentLibrary
        {
            get { return _currentLibrary; }
            set
            {
                if (value != _currentLibrary)
                {
                    changeLibrary(value);
                    _currentLibrary = value;
                }
            }

        }
        string _currentLibrary = "Wasatch";

        void changeLibrary(string key)
        {
            settings.setLibrary(key);
        }

        private async void Settings_LibraryChanged(object sender, Settings e)
        {
            if (!settings.library.loadSucceeded)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    notifyToast?.Invoke("Issue loading library, make sure phone is paired");
                });
            }
            if (settings.library is DPLibrary)
            {
                if ((settings.library as DPLibrary).isLoading)
                {
                    while (((settings.library as DPLibrary).isLoading))
                        Thread.Sleep(50);
                }

                logger.info("switching to DP library with {0} sublibraries", (settings.library as DPLibrary).LibraryOptions.Count);
                if (sublibraryViewModel.selections.Count == 0)
                {
                    foreach (var pair in (settings.library as DPLibrary).LibraryOptions)
                    {
                        sublibraryViewModel.selections.Add(new SelectionMetadata(pair.Item1, pair.Item2));
                    }
                }
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(subLibrariesAvailable)));
        }

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

        void changeParamSet(string key)
        {
            if (!parameterSets.ContainsKey(key))
                return;

            if (spec != null && spec.paired)
            {
                spec.holdAutoRamanParameterSet = true;
                AutoRamanParameters parameters = parameterSets[key];
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


        void updateFromInstance()
        {
            if (library == null)
                library = instance.library;
            xAxisMinimum = instance.xAxisMinimum;
            xAxisMaximum = instance.xAxisMaximum;
            matchString = instance.matchString;
            scoreString = instance.scoreString;
            _matchFound = instance.matchFound;
            spectrumCollected = instance.spectrumCollected;
            lastMeas = instance.lastMeas;
            matchIsPoly = instance.matchIsPoly;

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchFound)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(noMatchYet)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isNotVIS)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isVIS)));

            TriggerRetry = null;
            foreach (var listener in instance.TriggerRetry.GetInvocationList())
            {
                TriggerRetry += (System.EventHandler<AnalysisViewModel>)listener;
            }

            TriggerIncreasedPrecision = null;
            foreach (var listener in instance.TriggerIncreasedPrecision.GetInvocationList())
            {
                TriggerIncreasedPrecision += (System.EventHandler<AnalysisViewModel>)listener;
            }

            chartData.Clear();
            foreach (ChartDataPoint point in instance.chartData)
                chartData.Add(point);
            referenceData.Clear();
            foreach (ChartDataPoint point in instance.referenceData)
                referenceData.Add(point);
            scatterData.Clear();
            foreach (ChartDataPoint point in instance.scatterData)
                scatterData.Add(point);

            if (scatterData.Count > 10)
                fitScatterLine();
        }

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
            savePopup = new AddToLibraryPopup(saveViewModel);
            popupClosing = false;
            Shell.Current.ShowPopup<AddToLibraryPopup>(savePopup);
        }

        private async void SaveViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (saveViewModel != null)
            {
                if (e.PropertyName == nameof(saveViewModel.toBeSaved) && saveViewModel.toBeSaved)
                {
                    spec.measurement.filename = saveViewModel.saveName + ".csv";
                    spec.measurement.notes = saveViewModel.notes;
                    library.addSampleToLibrary(saveViewModel.saveName, spec.measurement);
                    var ok = await spec.measurement.saveAsync(true);
                    if (ok)
                    {
                        notifyToast?.Invoke($"{saveViewModel.saveName} added to library");
                    }
                    spec.measurement.pathname = null;
                    ok = await spec.measurement.saveAsync(false);
                }
            }

            if (e.PropertyName == nameof(saveViewModel.toBeSaved) && !popupClosing)
            {
                popupClosing = true;
                await savePopup.CloseAsync();
            }
        }


        private void AnalysisViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            /*
            if (library == null)
                library = instance.library;
            xAxisMinimum = instance.xAxisMinimum;
            xAxisMaximum = instance.xAxisMaximum;
            matchString = instance.matchString;
            */
        }

        public ObservableCollection<ChartDataPoint> chartData { get; set; } = new ObservableCollection<ChartDataPoint>();
        public ObservableCollection<ChartDataPoint> scatterData { get; set; } = new ObservableCollection<ChartDataPoint>();
        public ObservableCollection<ChartDataPoint> referenceData { get; set; } = new ObservableCollection<ChartDataPoint>();

        void handleNewSpectrometer(object sender, Spectrometer e)
        {
            refreshSpec();
            if (settings.library is DPLibrary)
            {
                _currentLibrary = "3rd Party";
                foreach (var pair in (settings.library as DPLibrary).LibraryOptions)
                {
                    sublibraryViewModel.selections.Add(new SelectionMetadata(pair.Item1, pair.Item2));
                }
            }
            else if (settings.library is WPLibrary)
                _currentLibrary = "Wasatch";
        }

        async Task changeCorrection()
        {
            if (lastMeas != null)
                spec.FindAndApplyRamanShiftCorrection(lastMeas, "Polystyrene");
            notifyToast?.Invoke($"Raman correction applied for future samples");
        }

        async Task ShareSpectrum()
        {
            var ok = await spec.measurement.saveAsync();

            if (ok)
            {
                string savePath = Settings.getInstance().getSavePath();
                string pathname = Path.Join(savePath, spec.measurement.filename);
                try
                {
                    await Share.Default.RequestAsync(new ShareFileRequest
                    {
                        Title = spec.measurement.filename + " " + matchString,
                        File = new ShareFile(pathname)
                    });
                }
                catch (Exception ex)
                {
                    logger.error("Share failed with exception {0}", ex.Message);
                }
            }
        }

        public void refreshSpec()
        {
            spec = BluetoothSpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = API6BLESpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = USBSpectrometer.getInstance();

            if (spec != null && spec.paired)
            {
                spec.laserWatchdogSec = 0;
                spec.laserWarningDelaySec = 0;
            }

            logger.debug("AVM.ctor: done");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isVIS)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(isNotVIS)));
        }

        public void SetData(Measurement sample, Measurement reference)
        {
            bool usingRemovalAxis = PlatformUtil.transformerLoaded && spec.useBackgroundRemoval && (spec.measurement.dark != null || spec.autoDarkEnabled || spec.autoRamanEnabled);

            double scaleFactor = 1;

            lastMeas = sample;

            if (sample == null &&  reference == null)
            {
                if (spec.wavenumbers != null)
                {
                    xAxisMinimum = spec.wavenumbers.Minimum();
                    xAxisMaximum = spec.wavenumbers.Maximum();

                    if (spec != null)
                    {
                        for (int i = 0; i < spec.wavenumbers.Length; i++)
                        {
                            chartData.Add(new ChartDataPoint() { intensity = 0, xValue = i });
                        }
                    }
                    else
                    {
                        for (int i = 0; i < 1024; i++)
                        {
                            chartData.Add(new ChartDataPoint() { intensity = 0, xValue = i });
                        }
                    }
                }
                else
                {
                    xAxisMinimum = spec.wavelengths.Minimum();
                    xAxisMaximum = spec.wavelengths.Maximum();

                    if (spec != null)
                    {
                        for (int i = 0; i < spec.wavelengths.Length; i++)
                        {
                            chartData.Add(new ChartDataPoint() { intensity = 0, xValue = i });
                        }
                    }
                    else
                    {
                        for (int i = 0; i < 1024; i++)
                        {
                            chartData.Add(new ChartDataPoint() { intensity = 0, xValue = i });
                        }
                    }
                }

                spectrumCollected = false;
            }
            else
            {
                spectrumCollected = true;
            }

            if (reference != null && sample != null)
            {

                if (!usingRemovalAxis &&
                    spec.useHorizontalROI &&
                    spec.eeprom.ROIHorizStart != spec.eeprom.ROIHorizEnd)
                {
                    double refScale = reference.processed.Skip(spec.eeprom.ROIHorizStart).Take(spec.eeprom.ROIHorizEnd - spec.eeprom.ROIHorizStart).Max();
                    double samScale = sample.postProcessed.Skip(spec.eeprom.ROIHorizStart).Take(spec.eeprom.ROIHorizEnd - spec.eeprom.ROIHorizStart).Max();
                    scaleFactor = refScale / samScale;
                }
                else
                {
                    double refScale = reference.processed.Maximum();
                    double samScale = sample.postProcessed.Maximum();
                    scaleFactor = refScale / samScale;
                }
            }
            if (sample != null)
            {
                uint pixels = sample.pixels;
                double[] intensities = sample.postProcessed;

                try
                {
                    xAxis = null;
                    if (xAxisName == "Wavelength")
                        xAxis = spec.wavelengths;
                    else if (xAxisName == "Wavenumber")
                    {
                        if (usingRemovalAxis)
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

                        updateChartData.Add(new ChartDataPoint() { intensity = scaleFactor * intensities[i], xValue = xAxis[i] });
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
            }
            if (reference != null)
            {
                uint pixels = reference.pixels;
                double[] intensities = reference.processed;

                try
                {
                    xAxis = null;
                    if (xAxisName == "Wavelength")
                        xAxis = spec.wavelengths;
                    else if (xAxisName == "Wavenumber")
                    {
                        if (usingRemovalAxis)
                            xAxis = spec.measurement.wavenumbers;
                        else
                            xAxis = spec.wavenumbers;
                    }
                    else
                        xAxis = spec.xAxisPixels;

                    logger.debug($"SVM.refreshreferenceData: using x-axis {xAxisName}");
                    if (intensities is null || xAxis is null || xAxis.Length == 0)
                    {
                        logger.error("SVM.refreshreferenceData: no x-axis or intensities");
                        return;
                    }

                    logger.info("populating ReferenceData");
                    var updateReferenceData = new ObservableCollection<ChartDataPoint>();

                    int pxLo = -1;
                    int pxHi = -1;
                    for (int i = 0; i < pixels; i++)
                    {
                        if (!usingRemovalAxis &&
                            spec.useHorizontalROI &&
                            spec.eeprom.ROIHorizStart != spec.eeprom.ROIHorizEnd &&
                            (i < spec.eeprom.ROIHorizStart || i > spec.eeprom.ROIHorizEnd))
                            continue;

                        updateReferenceData.Add(new ChartDataPoint() { intensity = intensities[i], xValue = xAxis[i] });
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
                    referenceData.Clear();
                    foreach (ChartDataPoint chartDataPoint in updateReferenceData)
                    {
                        referenceData.Add(chartDataPoint);
                    }

                    xAxisMinimum = xAxis[pxLo];
                    xAxisMaximum = xAxis[pxHi];
                    if (library != null)
                    {
                        TextInfo ti = CultureInfo.CurrentCulture.TextInfo;

                        matchIsPoly = library.mostRecentCompound.ToLower() == "polystyrene";
                        matchString = ti.ToTitleCase(library.mostRecentCompound);
                        scoreString = library.mostRecentScore.ToString("f2");
                        _matchFound = true;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchFound)));
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(noMatchYet)));
                    }
                    logger.debug($"refreshChartData: pxLo {pxLo}, pxHi {pxHi}, xMin {xAxisMinimum:f2}, xMax {xAxisMaximum:f2}");
                }
                catch (Exception ex)
                {
                    logger.debug($"refreshChartData: caught exception {ex}");
                }
            }
            else
            {
                referenceData.Clear();
                matchString = "No Match Found";
                _matchFound = false;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchFound)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(noMatchYet)));
            }

            logger.debug("refreshChartData: done");
            SpectraChanged?.Invoke(this, this);
        }

        public void AddScatter(double x, double y)
        {
            logger.info("adding scatter point");
            scatterData.Add(new ChartDataPoint() { intensity = y, xValue = x });
            logger.info("triggering scatter add");
            SpectraChanged?.Invoke(this, this);
            logger.info("triggered scatter add");
            //logger.info("updating instance");
            //updateFromInstance();
            //logger.info("updated instance");
        }

        const double MS_TO_MIN = 60000;
        const double SEC_TO_MIN = 60;

        public void fitScatterLine()
        {
            Common.LinearRegression lr = new Common.LinearRegression();
            List<double> xs = new List<double>();
            List<double> ys = new List<double>();
            foreach (ChartDataPoint point in scatterData)
            {
                xs.Add(point.xValue);
                ys.Add(point.intensity);
            }

            double[] line = lr.computeLinearRegression(1, xs.ToArray(), ys.ToArray());
            double slope = line[1];
            double score = slope * SEC_TO_MIN * settings.ellmanSlopeCorrection;

            logger.info("Scatter fit to c0: {0:g}, c1: {1:g}", line[0], line[1]);
            logger.info("Ellman correction {0:g}", settings.ellmanSlopeCorrection);
            logger.info("Activity score {0:g}", score);

            EllmanScoreString = score.ToString("F1") + " umol ACh/min/L";
            SlopeString = (slope * SEC_TO_MIN).ToString("F4") + " ΔOD/min";
        }


        ////////////////////////////////////////////////////////////////////////
        // X-Axis
        ////////////////////////////////////////////////////////////////////////
        
        public string xAxisName => "Wavenumber";

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

        public string matchString
        {
            get => _matchString;
            set
            {
                _matchString = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchString)));
            }
        }
        string _matchString = "No Match Found";
        
        public string scoreString
        {
            get => _scoreString;
            set
            {
                _scoreString = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(scoreString)));
            }
        }
        string _scoreString = "TBD";

        public bool spectrumCollected
        {
            get { return _spectrumCollected; }
            set 
            {
                _spectrumCollected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(spectrumCollected)));
            }
        }
        bool _spectrumCollected = false;

        public bool matchFound
        {
            get
            {
                return _matchFound;
            }
        }
        public bool noMatchYet
        {
            get
            {
                return !_matchFound;
            }
        }
        bool _matchFound;

        public bool matchIsPoly
        {
            get => _matchIsPoly;
            set
            {
                _matchIsPoly = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchIsPoly)));
            }
        }
        bool _matchIsPoly = false;

        public bool subLibrariesAvailable
        {
            get => settings.library is DPLibrary;
        }

        public string EllmanScoreString
        {
            get => _EllmanScoreString;
            set
            {
                _EllmanScoreString = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EllmanScoreString)));
            }
        }
        string _EllmanScoreString = "";
        
        public string SlopeString
        {
            get => _SlopeString;
            set
            {
                _SlopeString = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SlopeString)));
            }
        }
        string _SlopeString = "";

        public bool isVIS
        {
            get => true;//spec.laserExcitationNM == 0; //true;
        }

        public bool isNotVIS
        {
            get => false; //spec.laserExcitationNM != 0; //true;
        }

        bool doAdd()
        {
            OverlaysPopup op = new OverlaysPopup(sublibraryViewModel);
            op.Closed += Op_Closed; ;
            Shell.Current.ShowPopupAsync<OverlaysPopup>(op);

            return true;
        }

        private void Op_Closed(object sender, CommunityToolkit.Maui.Core.PopupClosedEventArgs e)
        {
            Dictionary<string, bool> activeLibraries = new Dictionary<string, bool>();

            foreach (SelectionMetadata omd in sublibraryViewModel.selections)
            {
                activeLibraries.Add(omd.name, omd.selected);
            }

            if (settings.library is DPLibrary)
            {
                (settings.library as DPLibrary).setFilter(activeLibraries);
            }
        }


    }
}
