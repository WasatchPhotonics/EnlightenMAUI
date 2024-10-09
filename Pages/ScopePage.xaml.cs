using System;
using System.Threading;
using EnlightenMAUI.ViewModels;
using EnlightenMAUI.Common;
using Telerik.Maui.Controls.Compatibility.Chart;
// using ZXing.Net.Mobile.Forms; // for QR codes

namespace EnlightenMAUI;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class ScopePage : ContentPage
{
    static readonly SemaphoreSlim semRotate = new SemaphoreSlim(1, 1);

    // ZXingScannerPage scanPage; // for QR codes
    
    ScopeViewModel svm;

    Logger logger = Logger.getInstance();

    public ScopePage()
    {
        InitializeComponent();

        svm = (ScopeViewModel)BindingContext;

        // Give the ScopeViewModel an ability to display "toast" messages on
        // the View (such as "saved foo.csv") by having the View monitor for
        // notifications, and if one is received
        // https://stackoverflow.com/a/26038700/11615696
        svm.notifyToast += (string msg) => Util.toast(msg); // MZ: iOS needs View

        svm.theChart = chart;
        //svm.OverlaysChanged += Svm_OverlaysChanged;
        //svm.WipeOverlays += Svm_WipeOverlays;
    }


    private void Svm_OverlaysChanged(object sender, ScopeViewModel e)
    {
        /*
        List<CartesianSeries> removeMe = new List<CartesianSeries>();
        foreach (var series in chart.Series)
        {
            if (series.DisplayName != "Live" && !svm.DataOverlays.ContainsKey(series.DisplayName))
                removeMe.Add(series);

        }

        foreach (var series in removeMe) 
            chart.Series.Remove(series);


        foreach (string name in svm.fullLibraryOverlayStatus.Keys)
        {
            bool found = false;
            if (!svm.fullLibraryOverlayStatus[name])
                continue;

            foreach (var series in chart.Series)
            {
                if (series.DisplayName == name)
                    found = true;
            }
            if (!found)
            {
                var series = new ScatterLineSeries
                {
                    Stroke = Color.FromUint(ScopeViewModel.colors[chart.Series.Count - 1 % ScopeViewModel.colors.Length]),
                    DisplayName = name,
                    XValueBinding = new PropertyNameDataPointBinding("xValue"),
                    YValueBinding = new PropertyNameDataPointBinding("intensity"),
                    ItemsSource = svm.DataOverlays[name]
                };

                chart.Series.Add(series);
            }

        }

        if (chart.Series.Count > 1) 
            svm.hasTraces = true;
        */
    }

    private void Svm_WipeOverlays(object sender, ScopeViewModel e)
    {
        List<CartesianSeries> removeMe = new List<CartesianSeries>();
        foreach (var series in chart.Series)
        {
            if (series.DisplayName != "Live")
                removeMe.Add(series);
        }

        foreach (var series in removeMe)
            chart.Series.Remove(series);
    }

    async void notifyUserAsync(string title, string message, string button) =>
       await DisplayAlert(title, message, button);

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        await Task.Delay(1);//Yield thread so the page is shown to the user
                            //This initial page should mostly just have a spinner
        //await VM?.UILoaded();//Load slow complex elements while the user watches the spinner
    }
    /*
    private void qrScan(object sender, EventArgs e)
    {
        performQRScan();
    }

    private void photoCapture(object sender, EventArgs e)
    {
        svm.performPhotoCapture();
    }

    private async void performQRScan()
    {
        scanPage = new ZXingScannerPage();
        scanPage.OnScanResult += (result) =>
        {
            scanPage.IsScanning = false;

            Device.BeginInvokeOnMainThread(async () =>
            {
                await Navigation.PopAsync();
                svm.setQRText(result.Text);
            });
        };

        await Navigation.PushAsync(scanPage);
    }
    */
}
