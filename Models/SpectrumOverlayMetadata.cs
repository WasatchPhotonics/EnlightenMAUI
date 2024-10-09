using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Models
{
    public  class SpectrumOverlayMetadata : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public SpectrumOverlayMetadata(string name, bool selected = false)
        {
            this.name = name;
            this.selected = selected;
        }

        public string name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(name)));
            }
        }
        string _name = "";
        
        /*
        public string backgroundColor
        {
            get
            {
                return _backgroundColor;
            }
            set
            {
                _backgroundColor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(backgroundColor)));
            }
        }
        string _backgroundColor = "#afafaf";
        */

        public bool selected
        {
            get => _selected;
            set
            {
                if (_selected != value)
                {
                    _selected = value;
                    //PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(backgroundColor)));
                }
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(selected)));
            }
        }
        bool _selected = false;

    }
}
