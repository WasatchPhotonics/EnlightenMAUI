using CommunityToolkit.Maui.Views;
using EnlightenMAUI.ViewModels;
namespace EnlightenMAUI.Popups;

public partial class OverlaysPopup : Popup
{
	public OverlaysPopup(SelectionPopupViewModel vm)
	{
		InitializeComponent();
		BindingContext = vm;
	}
}