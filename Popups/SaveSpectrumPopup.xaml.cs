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