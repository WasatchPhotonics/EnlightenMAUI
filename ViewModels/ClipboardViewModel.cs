using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.ComponentModel;

using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels;
public class ClipboardViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public ObservableCollection<Measurement> clipboardList = new ObservableCollection<Measurement>();
}
