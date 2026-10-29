using Globeweigh.Shared;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;

namespace Globeweigh.Admin
{
    public class AddEditGenericDataViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region Properties

        public string CharacterCasing { get; set; }

        private string _LabelText;
        public string LabelText
        {
            get { return _LabelText; }
            set { Set(ref _LabelText, value); }
        }

        private string _TransactionText;
        public string TransactionText
        {
            get { return _TransactionText; }
            set { Set(ref _TransactionText, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
        }

        private async void OnSave()
        {
            DialogResult = true;
        }

        public void Unload(FrameworkElement element)
        {
            TransactionText = string.Empty;
            HeaderText = string.Empty;
            LabelText = string.Empty;
            DialogResult = null;
            CharacterCasing = null;
        }
    }
}
