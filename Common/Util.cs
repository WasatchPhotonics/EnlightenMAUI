using Android.Widget;
using Microsoft.Maui;
using System;
using System.Text;
using static System.Net.Mime.MediaTypeNames;
using System.Threading;
using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Telerik.Maui;

namespace EnlightenMAUI.Common;

/// <summary>
/// This class provides some generic utility methods to the whole
/// application, including some which are platform-specific.
/// </summary>
public class Util
{
    private static Logger logger = Logger.getInstance();

    public static void swap(ref ushort a, ref ushort b)
    {
        var tmp = a;
        a = b;
        b = tmp;
    }

    public static double[] generateWavelengths(uint pixels, float[] coeffs)
    {            
        double[] wavelengths = new double[pixels];
        for (uint pixel = 0; pixel < pixels; pixel++)
        {   
            wavelengths[pixel] = coeffs[0];
            for (int i = 1; i < coeffs.Length; i++)
                wavelengths[pixel] += coeffs[i] * Math.Pow(pixel, i);
        }  
        return wavelengths;
    }

    public static double[] wavelengthsToWavenumbers(double laserWavelengthNM, double[] wavelengths)
    {
        const double NM_TO_CM = 1.0 / 10000000.0;
        double LASER_WAVENUMBER = 1.0 / (laserWavelengthNM * NM_TO_CM);

        if (wavelengths == null)
            return null;

        double[] wavenumbers = new double[wavelengths.Length];
        for (int i = 0; i < wavelengths.Length; i++)
        {
            double wavenumber = LASER_WAVENUMBER - (1.0 / (wavelengths[i] * NM_TO_CM));
            if (Double.IsInfinity(wavenumber) || Double.IsNaN(wavenumber))
                wavenumbers[i] = 0;
            else
                wavenumbers[i] = wavenumber;
        }
        return wavenumbers;
    }

    // Format a 16-byte array into a standard UUID string
    //
    // 00000000-0000-1000-8000-00805F9B34FB
    //  0 1 2 3  4 5  6 7  8 9  a b c d e f
    //
    // You'd think something like this would already be in Plugin.BLE, and
    // probably it is... *shrug*
    public static string formatUUID(byte[] data)
    {
        if (data.Length != 16)
            return "invalid-uuid";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 16; i++)
        {
            sb.Append(string.Format("{0:x2}", data[i]));
            if (i == 3 || i == 5 || i == 7 || i == 9)
                sb.Append("-");
        }
        return sb.ToString();
    }

    public static string toASCII(byte[] data)
    {
        StringBuilder sb = new StringBuilder();
        foreach (var b in data)
            sb.Append((char)b);
        return sb.ToString();
    }

    ////////////////////////////////////////////////////////////////////////
    // File Utilities
    ////////////////////////////////////////////////////////////////////////

    public static async Task<string> readAllTextFromFile(string pathname)
    {
        logger.debug("readAllTextFromFile: start");
        string text = "";

        #if ANDROID
        logger.debug($"readAllTextFromFile: opening {pathname}");
        var infile = File.OpenRead(pathname);

        logger.debug($"readAllTextFromFile: reading {pathname}");
        using (StreamReader sr = new StreamReader(infile))
        { 
            text = await sr.ReadToEndAsync();
        }
        #endif

        logger.debug("readAllTextFromFile: done");
        return text;
    }

    ////////////////////////////////////////////////////////////////////////
    // Bluetooth Utilities
    ////////////////////////////////////////////////////////////////////////

    public static bool bluetoothEnabled()
    {
        logger.debug("Util.bluetoothEnabled: start");
        bool enabled = false;
#if ANDROID
        logger.debug("Util.bluetoothEnabled: getting bluetoothManager");
        var bluetoothManager = (Android.Bluetooth.BluetoothManager)Android.App.Application.Context.GetSystemService(Android.Content.Context.BluetoothService);
        logger.debug("Util.bluetoothEnabled: getting bluetoothAdapter");
        var bluetoothAdapter = bluetoothManager.Adapter;
        logger.debug("Util.bluetoothEnabled: checking enabled");
        enabled = bluetoothAdapter.IsEnabled;
#endif
        logger.debug($"Util.bluetoothEnabled: returning {enabled}");
        return enabled;
    }

    public static bool enableBluetooth(bool flag)
    {
        logger.error($"Util.enableBluetooth({flag}): start");
    #if ANDROID
        logger.error($"Util.enableBluetooth({flag}): generating intent");
        var request = flag ? Android.Bluetooth.BluetoothAdapter.ActionRequestEnable 
                           : Android.Bluetooth.BluetoothAdapter.ActionRequestDiscoverable;
        var intent = new Android.Content.Intent(request);
        intent.SetFlags(Android.Content.ActivityFlags.NewTask);
        logger.error($"Util.enableBluetooth({flag}): sending intent");
        Android.App.Application.Context.StartActivity(intent);
    #endif
        logger.error($"Util.enableBluetooth({flag}): done");
        return true;
    }

    ////////////////////////////////////////////////////////////////////////
    // GUI Utilities
    ////////////////////////////////////////////////////////////////////////

    // View is there for iOS (Android doesn't need it)
    public static async void toast(string msg, View view = null)
    {
        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        ToastDuration duration = ToastDuration.Short;
        double fontSize = 14;
        var toast = CommunityToolkit.Maui.Alerts.Toast.Make(msg, duration, fontSize);
        await toast.Show(cancellationTokenSource.Token);
    }
}
