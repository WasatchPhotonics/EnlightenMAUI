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
    public class SelectionPopupViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public ObservableCollection<SelectionMetadata> selections {  get; private set; }

        public SelectionPopupViewModel(List<SelectionMetadata> selectionList)
        {
            selections = new ObservableCollection<SelectionMetadata>(selectionList);
        }
        public SelectionPopupViewModel()
        {
            selections = new ObservableCollection<SelectionMetadata>();
        }
    }
}
