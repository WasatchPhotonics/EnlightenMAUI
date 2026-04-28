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
        public EventHandler<SelectionPopupViewModel> triggerClose;

        public ObservableCollection<SelectionMetadata> selections {  get; private set; }
        public string exportName
        {
            get => _exportName;
            set
            {
                _exportName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(exportName)));
            }
        }

        string _exportName = "";
        public bool? save = null;

        public Command saveCommand { get; private set; }
        public Command closeCommand { get; private set; }

        public SelectionPopupViewModel(List<SelectionMetadata> selectionList)
        {
            selections = new ObservableCollection<SelectionMetadata>(selectionList);
            saveCommand = new Command(() => { save = true; triggerClose(this, this); });
            closeCommand = new Command(() => { save = false; triggerClose(this, this); });
        }
        public SelectionPopupViewModel()
        {
            selections = new ObservableCollection<SelectionMetadata>();
        }
    }
}
