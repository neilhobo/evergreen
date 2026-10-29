using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using Globeweigh.Shared;

namespace Globeweigh.Admin
{
    public class DialogViewModelBase : ViewModelBase
    {
        private string _HeaderText;
        public virtual string HeaderText
        {
            get { return _HeaderText; }
            set { Set(ref _HeaderText, value); }
        }

        private bool? _DialogResult;
        public virtual bool? DialogResult
        {
            get => _DialogResult;
            set => Set(ref _DialogResult, value);
        }

        private bool _HasChanges;
        public virtual bool HasChanges
        {
            get => _HasChanges;
            set => Set(ref _HasChanges, value);
        }

        private bool _IsLoading;
        public virtual bool IsLoading
        {
            get => _IsLoading;
            set => Set(ref _IsLoading, value);
        }

        public bool IsAdmin
        {
            get { return GlobalVariables.IsGlobeweighAdminOrAdmin; }
        }

        public GlobeweighUser CurrentGlobeweighUser
        {
            get { return GlobalVariables.CurrentGlobeweighUser; }
        }

        public RelayCommand _CancelCommand;
        public virtual RelayCommand CancelCommand
        {
            get
            {
                return _CancelCommand ?? (_CancelCommand = new RelayCommand(() =>
                {
                    if (HasChanges)
                    {
                        var dlgRes = MessageBox.Show("Exit without saving changes?", "", MessageBoxButton.OKCancel);
                        if (dlgRes == MessageBoxResult.Cancel) return;
                    }
                    DialogResult = false;
                }));
            }
        }
    }
}
