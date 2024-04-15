using EnlightenMAUI.Models;

namespace EnlightenMAUI
{
    public partial class InfoPage : ContentPage
    {
        public InfoPage()
        {
            InitializeComponent();
        }

        private async void Button_Clicked(object sender, EventArgs e)
        {
            Settings settings = Settings.getInstance();
            await Browser.OpenAsync(settings.companyURL);

        }
    }
}
