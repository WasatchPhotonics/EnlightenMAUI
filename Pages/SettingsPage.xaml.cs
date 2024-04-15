using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EnlightenMAUI.ViewModels;
using EnlightenMAUI.Models;

namespace EnlightenMAUI;

[XamlCompilation(XamlCompilationOptions.Compile)] // MZ: needed?
public partial class SettingsPage : ContentPage
{
    SettingsViewModel asvm; // ApplicationSettings vs Scope...?

    Logger logger = Logger.getInstance();

    public SettingsPage()
    {
        InitializeComponent();

        // asvm = (SettingsViewModel)BindingContext; // MZ: fix
        asvm?.loadSettings();
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
    
    void entryVerticalROIStartLine_Completed(Object sender, EventArgs e)
    {
        var entry = sender as Entry;
        asvm?.setVerticalROIStartLine(entry.Text);
    }

    void entryVerticalROIStopLine_Completed(Object sender, EventArgs e)
    {
        var entry = sender as Entry;
        asvm?.setVerticalROIStopLine(entry.Text);
    }
}
