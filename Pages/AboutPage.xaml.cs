using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using EnlightenMAUI.ViewModels;
using EnlightenMAUI.Models;

namespace EnlightenMAUI;

/// <summary>
/// Welcome page for the application.
/// </summary>
/// <remarks>This was AboutPage in 1.x</remarks>
[XamlCompilation(XamlCompilationOptions.Compile)]
public partial class AboutPage : ContentPage
{
    public AboutPage()
    {
        InitializeComponent();
    }
}
