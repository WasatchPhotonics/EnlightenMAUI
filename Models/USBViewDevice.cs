using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EnlightenMAUI.Models
{
    public class USBViewDevice : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        Logger logger = Logger.getInstance();


        /// <summary>
        /// This is where we master whether the app is paired with a Bluetooth device or not.
        /// </summary>
        ///
        /// <remarks>
        /// This is not an ideal location, because most of this "BLEDevice" Model 
        /// relates to A SPECIFIC BLEDevice, and not the general application state of
        /// being paired. However, it is at least encapsulated in a MODEL, which 
        /// means that all ViewModels can refer to it; this beats the previous 
        /// approach of having separate flags in ScopeViewModel and 
        /// BluetoothViewModel, or having ViewModels reference each other etc. Maybe
        /// someday we'll add Models.Application[Bluetooth]State, but this is fine
        /// for now. Making static to distinguish from the instance :-)
        /// </remarks>
        public static bool paired { get; set; }

        ////////////////////////////////////////////////////////////////////////
        // Lifecycle
        ////////////////////////////////////////////////////////////////////////

        public USBViewDevice(string name, string pid, string vid)
        {
            _Name = name;
            _pid = pid;
            _vid = vid;
            /*
            foreach (var rec in device.AdvertisementRecords)
            {
                logger.hexdump(rec.Data, prefix: $"  {rec.Type}: ");

                // On iOS, this returns the 16-bit Primary Service UUID (0xff00),
                //         which is useless.
                // On Android, this returns the 128-bit Primary Service UUID
                //         d1a7ff00-af78-4449-a34f-4da1afaf51bc, equally useless.
                //
                // I do wonder if this could be fixed in firmware?
                //
                // if (rec.Type == AdvertisementRecordType.UuidsComplete128Bit)
                //    uuid = Util.formatUUID(rec.Data);
            }
            */
        }

        public string name
        {
            get => _Name;
        }
        string _Name = "";

        // This is the little hex string displayed under the device name in the
        // BluetoothView allowing you to distinguish between multiple WP-SiG
        // devices in Bluetooth range.  Bizarrely, the value won't match in
        // content or format between iOS and Android :-(
        //
        // On iOS, this gives a 128-bit device UUID, e.g. fd7b9fca-3615-da68-03f2-6557e29e2be4
        // On Android, this gives a less-impressive 48-bit device UUID, e.g. f1e9a7ce0ac8
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

        // Models generally shouldn't determine GUI colors...
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
