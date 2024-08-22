using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Models
{
    /*
     * Mostly a pared down clone of BluetoothDevice
     */

    public class USBViewDevice : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        Logger logger = Logger.getInstance();


        public static bool paired { get; set; }

        ////////////////////////////////////////////////////////////////////////
        // Lifecycle
        ////////////////////////////////////////////////////////////////////////

        public USBViewDevice(string name, string pid, string vid)
        {
            _Name = name;
            _pid = pid;
            _vid = vid;
        }

        public string name
        {
            get => _Name;
        }
        string _Name = "";

        public string pid
        {
            get => _pid;
        }
        string _pid = "";

        public string vid
        {
            get => _vid;
        }
        string _vid = "";

        ////////////////////////////////////////////////////////////////////////
        // GUI selection state
        ////////////////////////////////////////////////////////////////////////

        public string backgroundColor
        {
            get => selected ? "#555" : "#444";
        }

        public bool selected
        {
            get => _selected;
            set
            {
                if (_selected != value)
                {
                    _selected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(backgroundColor)));
                }
            }
        }
        bool _selected;
    }
}
