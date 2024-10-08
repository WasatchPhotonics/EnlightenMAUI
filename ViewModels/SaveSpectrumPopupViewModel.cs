using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.ViewModels
{
    public class SaveSpectrumPopupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public SaveSpectrumPopupViewModel(string saveName)
        {
            this.saveName = saveName;
            saveCmd = new Command(() => { _ = toBeSaved = true; });
            cancelCmd = new Command(() => { _ = toBeSaved = false; });
        }

        public Command saveCmd { get; }
        public Command cancelCmd { get; }

        public string saveName
        {
            get { return _saveName; }
            set
            {
                _saveName = value; 
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(saveName)));
            }
        }
        string _saveName;

        public bool toBeSaved
        {
            get { return _toBeSaved; }
            set 
            {
                _toBeSaved = value; 
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(toBeSaved)));
            }
        }
        bool _toBeSaved = false;



    }
}
