using Android;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Content;
using AndroidX.Core.App;
using EnlightenMAUI.Common;

namespace EnlightenMAUI
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        public static int REQUEST_TREE = 85;

        // @see https://stackoverflow.com/a/76859167
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            if (Build.VERSION.SdkInt > BuildVersionCodes.R && ActivityCompat.CheckSelfPermission(this, Manifest.Permission.BluetoothConnect) != Permission.Granted)
                ActivityCompat.RequestPermissions(Platform.CurrentActivity, new string[] { Manifest.Permission.BluetoothConnect }, 102);
            else if (Build.VERSION.SdkInt <= BuildVersionCodes.R && ActivityCompat.CheckSelfPermission(this, Manifest.Permission.Bluetooth) != Permission.Granted)
                ActivityCompat.RequestPermissions(Platform.CurrentActivity, new string[] { Manifest.Permission.Bluetooth }, 102);
        }
        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            if (requestCode == StorageHelper.RequestCode)
                StorageHelper.OnActivityResult();

            base.OnActivityResult(requestCode, resultCode, data);

            if (resultCode == Result.Ok)
            {
                if (requestCode == REQUEST_TREE)
                {
                    // The result data contains a URI for the document or directory that the user selected.
                    if (data != null)
                    {
                        Android.Net.Uri uri = data.Data;
                        var flags = data.Flags & (Android.Content.ActivityFlags.GrantReadUriPermission | Android.Content.ActivityFlags.GrantWriteUriPermission);

                        //Take the persistable URI permissions (so that they actually persist)
                        this.ContentResolver.TakePersistableUriPermission(uri, flags);
                    }
                }
            }
        }

    }
}
