using Globeweigh.Shared;
using Globeweigh.Model;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;

namespace Globeweigh.Touch
{
    public class OkCancelViewModel : BindableBase, IModalDialogViewModel, IViewModel
    {
        #region Properties

        private string _MessageText;
        public string MessageText
        {
            get { return _MessageText; }
            set { Set(ref _MessageText, value); }
        }

        private bool? _DialogResult;
        public bool? DialogResult
        {
            get { return _DialogResult; }
            set { Set(ref _DialogResult, value); }
        }

        private bool _HideCancel;
        public bool HideCancel
        {
            get => _HideCancel;
            set => Set(ref _HideCancel, value);
        }

        #endregion

        #region Commands

        public RelayCommand OkCommand => new RelayCommand(()=>DialogResult = true);
        public RelayCommand CancelCommand => new RelayCommand(() => DialogResult = false);

        #endregion

        public async void Load(FrameworkElement element)
        {
        }

        public void Unload(FrameworkElement element)
        {
            MessageText = string.Empty;
            DialogResult = null;
            HideCancel = false;
        }
    }
}
