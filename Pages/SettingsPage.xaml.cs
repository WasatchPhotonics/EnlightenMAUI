using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EnlightenMAUI.ViewModels;
using EnlightenMAUI.Models;

namespace EnlightenMAUI;

[XamlCompilation(XamlCompilationOptions.Compile)] 
public partial class SettingsPage : ContentPage
{
    SettingsViewModel svm; 

    Logger logger = Logger.getInstance();

    public SettingsPage()
    {
        InitializeComponent();

        svm = (SettingsViewModel)BindingContext; 
        svm?.loadSettings();
    }

    // the user clicked "return" or "done" when entering the password, so
    // hide what he entered, then ask the ViewModel to authenticate
    void entryPassword_Completed(Object sender, EventArgs e)
    {
    }

    // the user clicked in an Entry, so clear the field
    void entry_Focused(Object sender, FocusEventArgs e)
    {
        var entry = sender as Entry;
        entry.Text = "";
    }

    /*
    void entryLaserWarningDelaySec_Completed(Object sender, EventArgs e)
    {
        var entry = sender as Entry;
        svm?.setLaserWarningDelaySec(entry.Text);
    }
    */
    
    void entryVerticalROIStartLine_Completed(Object sender, EventArgs e)
    {
        var entry = sender as Entry;
        // svm?.setVerticalROIStartLine(entry.Text);
    }

    void entryVerticalROIStopLine_Completed(Object sender, EventArgs e)
    {
        var entry = sender as Entry;
        // svm?.setVerticalROIStopLine(entry.Text);
    }
}
