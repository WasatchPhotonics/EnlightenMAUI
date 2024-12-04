﻿using Accord.Statistics.Testing.Power;
using EnlightenMAUI.Models;
using EnlightenMAUI.Platforms;
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

namespace EnlightenMAUI.ViewModels
{
    internal class AnalysisViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<AnalysisViewModel> SpectraChanged;


        Logger logger = Logger.getInstance();
        public Library library;
        public Spectrometer spec;
        static AnalysisViewModel instance = null;
        double[] xAxis;

        static public AnalysisViewModel getInstance()
        {
            if (instance is null)
                instance = new AnalysisViewModel(true);
            return instance;
        }

        public AnalysisViewModel()
        {
            spec = BluetoothSpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = USBSpectrometer.getInstance();

            if (instance != null)
                updateFromInstance();
            else
                SetData(null, null);

            Spectrometer.NewConnection += handleNewSpectrometer;
            getInstance().PropertyChanged += AnalysisViewModel_PropertyChanged;
            getInstance().SpectraChanged += AnalysisViewModel_SpectraChanged;
        }

        public AnalysisViewModel(bool isInstance = false)
        {
            spec = BluetoothSpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = USBSpectrometer.getInstance();

            SetData(null, null);

            Spectrometer.NewConnection += handleNewSpectrometer;
            if (!isInstance)
            {
                getInstance().PropertyChanged += AnalysisViewModel_PropertyChanged;
                getInstance().SpectraChanged += AnalysisViewModel_SpectraChanged;
            }
        }

        private void AnalysisViewModel_SpectraChanged(object sender, AnalysisViewModel e)
        {
            updateFromInstance();
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

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(matchFound)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(noMatchYet)));

            chartData.Clear();
            foreach (ChartDataPoint point in instance.chartData)
                chartData.Add(point);
            referenceData.Clear();
            foreach (ChartDataPoint point in instance.referenceData)
                referenceData.Add(point);
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
        public ObservableCollection<ChartDataPoint> referenceData { get; set; } = new ObservableCollection<ChartDataPoint>();

        void handleNewSpectrometer(object sender, Spectrometer e)
        {
            refreshSpec();
        }

        public void refreshSpec()
        {
            spec = BluetoothSpectrometer.getInstance();
            if (spec == null || !spec.paired)
                spec = USBSpectrometer.getInstance();

            if (spec != null && spec.paired)
            {
                spec.laserWatchdogSec = 0;
                spec.laserWarningDelaySec = 0;
            }

            logger.debug("AVM.ctor: done");
        }

        public void SetData(Measurement sample, Measurement reference)
        {
            bool usingRemovalAxis = PlatformUtil.transformerLoaded && spec.useBackgroundRemoval && (spec.measurement.dark != null || spec.autoDarkEnabled || spec.autoRamanEnabled);

            double scaleFactor = 1;

            if (sample == null &&  reference == null)
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

            if (reference != null && sample != null)
            {

                if (!usingRemovalAxis &&
                    spec.useHorizontalROI &&
                    spec.eeprom.ROIHorizStart != spec.eeprom.ROIHorizEnd)
                {
                    double refScale = reference.processed.Skip(spec.eeprom.ROIHorizStart).Take(spec.eeprom.ROIHorizEnd - spec.eeprom.ROIHorizStart).Max();
                    double samScale = sample.processed.Skip(spec.eeprom.ROIHorizStart).Take(spec.eeprom.ROIHorizEnd - spec.eeprom.ROIHorizStart).Max();
                    scaleFactor = refScale / samScale;
                }
                else
                {
                    double refScale = reference.processed.Maximum();
                    double samScale = sample.processed.Maximum();
                    scaleFactor = refScale / samScale;
                }
            }
            if (sample != null)
            {
                uint pixels = sample.pixels;
                double[] intensities = sample.processed;

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

    }
}
