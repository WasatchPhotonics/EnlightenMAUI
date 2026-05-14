using CommunityToolkit.Maui.Views;
using EnlightenMAUI.ViewModels;
namespace EnlightenMAUI.Popups;

public partial class ExportPopup : Popup
{
	public ExportPopup(SelectionPopupViewModel vm)
	{
		InitializeComponent();
        BindingContext = vm;
    }
}