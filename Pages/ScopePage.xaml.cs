using System;
using System.Threading;
using EnlightenMAUI.ViewModels;
// using ZXing.Net.Mobile.Forms; // for QR codes

namespace EnlightenMAUI;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class ScopePage : ContentPage
{
    // bool lastLandscape;
    // bool showingControls = true;
    static readonly SemaphoreSlim semRotate = new SemaphoreSlim(1, 1);

    // ZXingScannerPage scanPage; // for QR codes
    
    const string rightArrow = ">>";
    const string leftArrow = "<<";

    ScopeViewModel svm;

    Logger logger = Logger.getInstance();

    public ScopePage()
    {
        InitializeComponent();

        // OnSizeAllocated(Width, Height); // MZ: landscape features disabled

        // ScopeView has numerous View <--> ViewModel interactions, so grab
        // a handle to the ViewModel
        svm = (ScopeViewModel)BindingContext;

        // Give the ScopeViewModel an ability to display "toast" messages on
        // the View (such as "saved foo.csv") by having the View monitor for
        // notifications, and if one is received
        // https://stackoverflow.com/a/26038700/11615696
        // svm.notifyToast += (string msg) => Util.toast(msg, scrollOptions); // MZ: fix

        svm.theChart = chart; 
    }

    ////////////////////////////////////////////////////////////////////////
    // Entries
    ////////////////////////////////////////////////////////////////////////

    /* Not currently using any text entry fields.

    // the user clicked in an Entry, so clear the field
    void entry_Focused(Object sender, FocusEventArgs e)
    {
        var entry = sender as Entry;
        entry.Text = "";
    }
    */

    async void notifyUserAsync(string title, string message, string button) =>
       await DisplayAlert(title, message, button);

    ////////////////////////////////////////////////////////////////////////
    // Expand / hide control palette in Landscape mode
    ////////////////////////////////////////////////////////////////////////
    
    /*
    private void buttonExpander_Clicked(object sender, EventArgs e)
    {
        logger.debug("Clicked the expander button");
        scrollOptions.IsVisible = showingControls = !showingControls;
        buttonExpander.Text = showingControls ? rightArrow : leftArrow;
        updateLandscapeGridColumns();
    }
    */

    /*
    // This event is used to reformat the ScopeView from Portrait to Landscape 
    // and back again.
    // 
    // @see https://docs.microsoft.com/en-us/xamarin/xamarin-forms/user-interface/layouts/device-orientation
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        bool doRotate = false;
        var landscape = width > height;

        if (!semRotate.Wait(5))
        {
            logger.debug($"OnSizeAllocated: timeout");
            return;
        }

        if (landscape != lastLandscape)
        {
            logger.debug($"OnSizeAllocated: Width {width}, Height {height}");
            logger.debug($"OnSizeAllocated: rotated from lastLandscape {lastLandscape} to landscape {landscape}");
            lastLandscape = landscape;
            doRotate = true;
        }
        semRotate.Release();

        if (doRotate)
        {
            logger.debug($"OnSizeAllocated: performing rotation");
            if (landscape)
            {
                // transition to Landscape
                logger.debug("OnSizeAllocated: transitioning to Landscape");
                updateLandscapeGridColumns();
            }
            else
            {
                // transition to Portrait
                logger.debug("OnSizeAllocated: transitioning to Portrait");

                // change Grid to [ chart    ]
                //                [ hide     ]
                //                [ controls ]
                outerGrid.RowDefinitions.Clear();
                outerGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) } );
                outerGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(0, GridUnitType.Star) } );
                outerGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) } );
                outerGrid.ColumnDefinitions.Clear();
                outerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) } );

                stackChart.SetValue(Grid.RowProperty, 0);
                stackChart.SetValue(Grid.ColumnProperty, 0);
                stackExpander.SetValue(Grid.RowProperty, 1);
                stackExpander.SetValue(Grid.ColumnProperty, 0);
                scrollOptions.SetValue(Grid.RowProperty, 2);
                scrollOptions.SetValue(Grid.ColumnProperty, 0);

                // always show controls in portrait
                showingControls = scrollOptions.IsVisible = true;
                stackExpander.IsVisible = false;
                buttonExpander.Text = rightArrow;
            }

            logoVertical.IsVisible = !landscape;
            logoHorizontal.IsVisible = landscape;

            logger.debug("OnSizeAllocated: rotation complete");
        }
    }

    void updateLandscapeGridColumns()
    {
        outerGrid.RowDefinitions.Clear();
        outerGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) } );
        outerGrid.ColumnDefinitions.Clear();

        stackExpander.IsVisible = true;

        logger.debug($"updateLandscapeGridColumns: showingControls {showingControls}");

        // change Grid to [ chart | expander | controls ]
        if (showingControls)
        {
            buttonExpander.Text = rightArrow;
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) } );
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(0.1, GridUnitType.Star) } );
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) } );
        }
        else
        {
            // not showing controls
            buttonExpander.Text = leftArrow;
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) } );
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(0.05, GridUnitType.Star) } );
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(0, GridUnitType.Star) } );
        }

        stackChart.SetValue(Grid.RowProperty, 0);
        stackChart.SetValue(Grid.ColumnProperty, 0);
        stackExpander.SetValue(Grid.RowProperty, 0);
        stackExpander.SetValue(Grid.ColumnProperty, 1);
        scrollOptions.SetValue(Grid.RowProperty, 0);
        scrollOptions.SetValue(Grid.ColumnProperty, 2);

        logger.debug($"updateLandscapeGridColumns: done");
    }
    */

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
