using EnlightenMAUI.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.ViewModels
{
    public class ShellViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ShellViewModel()
        {
            Spectrometer.NewConnection += handleNewSpectrometer;
        }

        void handleNewSpectrometer(object sender, Spectrometer e)
        {
            specConnected = true;
        }

        public bool specConnected
        {
            get => _specConnected;
            set
            {
                if (_specConnected != value) 
                    _specConnected = value;

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(specConnected)));
            }
        }
        bool _specConnected = false;

    }
}
