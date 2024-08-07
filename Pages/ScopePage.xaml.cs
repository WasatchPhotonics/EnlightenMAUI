using System;
using System.Threading;
using EnlightenMAUI.ViewModels;
using EnlightenMAUI.Common;
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
    }

    async void notifyUserAsync(string title, string message, string button) =>
       await DisplayAlert(title, message, button);

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
