using CommunityToolkit.Maui.Views;
using EnlightenMAUI.ViewModels;
namespace EnlightenMAUI.Popups;

public partial class OverlaysPopup : Popup
{
	public OverlaysPopup(OverlaysPopupViewModel vm)
	{
		InitializeComponent();
		BindingContext = vm;
	}
}