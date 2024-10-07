using CommunityToolkit.Maui.Views;
using EnlightenMAUI.ViewModels;
namespace EnlightenMAUI.Popups;

public partial class SaveSpectrumPopup : Popup
{
	public SaveSpectrumPopup(SaveSpectrumPopupViewModel vm)
	{
		InitializeComponent();
		Logger.getInstance().info("displaying popup");
		BindingContext = vm;
        Logger.getInstance().info("popup binded");
    }
}