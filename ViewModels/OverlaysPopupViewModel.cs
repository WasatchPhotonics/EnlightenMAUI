using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EnlightenMAUI.Models;

namespace EnlightenMAUI.ViewModels
{
    public class OverlaysPopupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public ObservableCollection<SpectrumOverlayMetadata> overlays {  get; private set; }

        public OverlaysPopupViewModel(List<SpectrumOverlayMetadata> overlayList)
        {
            overlays = new ObservableCollection<SpectrumOverlayMetadata>(overlayList);
        }
    }
}
