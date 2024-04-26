using EnlightenMAUI.Models;

namespace EnlightenMAUI;

[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
    }

    // could go in ViewModel
    private async void Button_Clicked(object sender, EventArgs e)
    {
        Settings settings = Settings.getInstance();
        _ = await Browser.OpenAsync(settings.companyURL);
    }
}
