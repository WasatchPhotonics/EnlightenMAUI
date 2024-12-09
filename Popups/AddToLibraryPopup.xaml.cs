using CommunityToolkit.Maui.Views;
using EnlightenMAUI.ViewModels;

namespace EnlightenMAUI.Popups;

public partial class AddToLibraryPopup : Popup
{
	public AddToLibraryPopup(SaveSpectrumPopupViewModel vm)
	{
		InitializeComponent();
        BindingContext = vm;
    }

    private void saveEntry_Focused(object sender, FocusEventArgs e)
    {
        Dispatcher.Dispatch(new Action(() =>
        {
            var entry = sender as Entry;

            entry.CursorPosition = 0;
            entry.SelectionLength = entry.Text == null ? 0 : entry.Text.Length;
        }));
    }
}