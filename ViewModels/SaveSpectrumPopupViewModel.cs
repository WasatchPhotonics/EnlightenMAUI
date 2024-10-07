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
        }

        public string saveName
        {
            get { return _saveName; }
            set
            {
                _saveName = value; 
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(saveName));
            }
        }
         string _saveName;


    }
}
