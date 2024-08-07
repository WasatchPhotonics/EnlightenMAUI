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
}
