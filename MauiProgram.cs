using CommunityToolkit.Maui;
using EnlightenMAUI.Popups;
using EnlightenMAUI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using EnlightenMAUI.Models;
using System.Reflection;
using Telerik.Maui.Controls.Compatibility;

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


#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
