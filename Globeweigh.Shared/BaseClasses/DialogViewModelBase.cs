using GalaSoft.MvvmLight.CommandWpf;

namespace Globeweigh.Shared
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

        public  RelayCommand _CancelCommand;
        public virtual RelayCommand CancelCommand
        {
            get
            {
                return _CancelCommand ?? (_CancelCommand = new RelayCommand(()=>
                {
                    DialogResult = false;
                }));
            }
        }
    }
}
