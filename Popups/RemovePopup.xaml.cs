using CommunityToolkit.Maui.Views;
using EnlightenMAUI.ViewModels;
namespace EnlightenMAUI.Popups;

public partial class RemovePopup : Popup
{
	public RemovePopup(SelectionPopupViewModel vm)
	{
		InitializeComponent();
        BindingContext = vm;
    }
}