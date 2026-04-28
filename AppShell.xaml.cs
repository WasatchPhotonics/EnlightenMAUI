using Microsoft.Extensions.Configuration;
using EnlightenMAUI.Models;

namespace EnlightenMAUI
{
    public partial class AppShell : Shell
    {
        IConfiguration configuration;

        public AppShell(IConfiguration config)
        {
            InitializeComponent();
            configuration = config;

            if (configuration != null)
            {
                try
                {
                    var settings = configuration.GetRequiredSection("Settings").Get<BuildSettings>();

                    if (settings != null && settings.Hello != null)
                    {
                        Logger.HELLO = settings.Hello;
                    }
                }
                catch (Exception e)
                {
                    Logger.getInstance().info("Error loading build settings: {0}", e);
                }
            }
        }
    }
}
