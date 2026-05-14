using CommunityToolkit.Maui;
using EnlightenMAUI.Popups;
using EnlightenMAUI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using EnlightenMAUI.Models;
using System.Reflection;
using Telerik.Maui.Controls.Compatibility;
using Android.Content.Res;

#if ANDROID
using AndroidX.AppCompat.Widget;
#endif

namespace EnlightenMAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseTelerik()
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        var a = Assembly.GetExecutingAssembly();
        using var stream = a.GetManifestResourceStream("EnlightenMAUI.appsettings.json");

        if (stream != null)
        {
            var config = new ConfigurationBuilder()
                .AddJsonStream(stream)
                .Build();

            builder.Configuration.AddConfiguration(config);
        }

        builder.Services.AddTransient<AppShell>();

        Microsoft.Maui.Handlers.RadioButtonHandler.Mapper.AppendToMapping("MyCustomization", (handler, view) =>
        {
#if ANDROID
            if (handler.PlatformView is Android.Widget.RadioButton radioButton)
            {
                var states = new int[][]
                {
                        new int[] { Android.Resource.Attribute.StateChecked },
                        new int[] { -Android.Resource.Attribute.StateChecked }
                };

                // Define the colors corresponding to the states (border color)
                var colors = new int[]
                {
                        new Android.Graphics.Color(0x27, 0xc0, 0xa1),
                        new Android.Graphics.Color(0x27, 0xc0, 0xa1)
                };

                // Create a ColorStateList with the states and colors
                var colorStateList = new Android.Content.Res.ColorStateList(states, colors);

                // Apply the color tint to the button's border
                radioButton.ButtonTintList = colorStateList;
            }
#endif
        });

        /*
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("MyCustomization", (handler, view) =>
        {
#if ANDROID
            //handler.PlatformView.BorderStyle = Android.Graphics.Color.White;
            var pv = handler.PlatformView;
            if (pv is AppCompatEditText editText)
            {
                editText.Background?.Mutate();
                var color = new Android.Graphics.Color(0x27, 0xc0, 0xa1);
                var hintColor = new Android.Graphics.Color(0xBB, 0xBB, 0xBB);
                editText.BackgroundTintList = ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
                //editText.SetHintTextColor(hintColor);
            }
#endif
        });
        */

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
