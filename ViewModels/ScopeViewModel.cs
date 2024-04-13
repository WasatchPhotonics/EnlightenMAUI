using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using EnlightenMAUI.Models;
using static Android.Widget.GridLayout;

namespace EnlightenMAUI.ViewModels
{
    public class ScopeViewModel : INotifyPropertyChanged
    {
        public string xAxisLabelFormat { get; set; } = "F2";
        public double xAxisMinimum { get; set; } = 400;
        public double xAxisMaximum { get; set; } = 2400;
        public double spectrumMax { get; set; } = 0;

        public ScopeViewModel()
        {
            updateChart();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void updateChart()
        {
            refreshChartData();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(chartData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(spectrumMax)));
        }


        private void refreshChartData()
        {
            // use last Measurement from the Spectrometer
            uint pixels = 1952; // spec.pixels;
            double[] intensities = { 1, 2, 3, 2, 1, 2, 3, 4, 5, 4 }; // spec.measurement.processed;

            try
            {
                /*
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
                */
                double[] xAxis = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

                var updateChartData = new ObservableCollection<ChartDataPoint>();

                double hi = -1;
                for (int i = 0; i < pixels; i++)
                {
                    updateChartData.Add(new ChartDataPoint() { intensity = intensities[i], xValue = xAxis[i] });
                    hi = Math.Max(hi, intensities[i]);
                }

                chartData = updateChartData;

                xAxisMinimum = xAxis[0];
                xAxisMaximum = xAxis[pixels - 1];
                spectrumMax = hi;
                return;
            }
            catch
            {
                return;
            }

        }

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
    }
}
