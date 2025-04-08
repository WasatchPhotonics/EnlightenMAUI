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

    private void RemoveButton_Clicked(object sender, EventArgs e)
    {
        SelectionPopupViewModel vm = BindingContext as SelectionPopupViewModel;
        foreach (var item in vm.selections)
        {
            if (item.selected)
                vm.markedForRemoval.Add(item.name);
        }

        this.Close();
    }
}