using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace EnlightenMAUI
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {

        // @see https://stackoverflow.com/a/76859167
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            if (Build.VERSION.SdkInt > BuildVersionCodes.R && ActivityCompat.CheckSelfPermission(this, Manifest.Permission.BluetoothConnect) != Permission.Granted)
                ActivityCompat.RequestPermissions(Platform.CurrentActivity, new string[] { Manifest.Permission.BluetoothConnect }, 102);
            else if (Build.VERSION.SdkInt <= BuildVersionCodes.R && ActivityCompat.CheckSelfPermission(this, Manifest.Permission.Bluetooth) != Permission.Granted)
                ActivityCompat.RequestPermissions(Platform.CurrentActivity, new string[] { Manifest.Permission.Bluetooth }, 102);
        }

    }
}
