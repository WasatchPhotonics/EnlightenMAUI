using EnlightenMAUI.Models;
using EnlightenMAUI.ViewModels;
using EnlightenMAUI.Common;

namespace EnlightenMAUI;
public partial class AnalysisPage : ContentPage
{
    AnalysisViewModel avm;

	public AnalysisPage()
	{
		InitializeComponent();

        avm = (AnalysisViewModel)BindingContext;

        // https://stackoverflow.com/a/26038700/11615696
        avm.notifyToast += (string msg) => Util.toast(msg); // MZ: iOS needs View
    }
}