using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using IDialogService = MvvmDialogs.IDialogService;

namespace Globeweigh.Touch
{
    /// <summary>
    /// First step of the Collection flow: the haulier is given a 3-digit reference (the last 3
    /// digits of the load's weighbridge record id). The driver enters it here; we look up the
    /// matching load and move on to the collection weigh screen.
    /// </summary>
    public class CollectionInViewModel : TouchViewModelBase, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private string _RefNumber;
        public string RefNumber
        {
            get { return _RefNumber; }
            set
            {
                Set(ref _RefNumber, value);
                ErrorMessage = null;
                NextCommand.RaiseCanExecuteChanged();
            }
        }

        private string _ErrorMessage;
        public string ErrorMessage
        {
            get { return _ErrorMessage; }
            set { Set(ref _ErrorMessage, value); }
        }

        #endregion

        #region Commands

        private RelayCommand _nextCommand;
        public RelayCommand NextCommand =>
            _nextCommand ?? (_nextCommand = new RelayCommand(OnNext, CanNext));

        public RelayCommand BackCommand => new RelayCommand(OnBack);
        public RelayCommand HomeCommand => new RelayCommand(OnBack);

        #endregion

        private void OnBack()
        {
            SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel =
                SimpleIoc.Default.GetInstance<WeighbridgeInViewModel>();
        }

        private bool CanNext()
        {
            return !string.IsNullOrWhiteSpace(RefNumber);
        }

        private async void OnNext()
        {
            if (IsBusy) return;

            try
            {
                IsBusy = true;

                // IdRef is the zero-padded last 3 digits of the load id, so preserve/allow leading
                // zeros: pad short entries (e.g. "5" -> "005") to match what the haulier was given.
                var refId = RefNumber.Trim().PadLeft(3, '0');

                var preload = await _weighRepo.GetWeighbridgeRecordPendingViewByIdRef(refId);
                if (preload == null)
                {
                    ErrorMessage = "No load found for reference '" + refId + "'.";
                    return;
                }

                var vm = SimpleIoc.Default.GetInstance<CollectionWeighViewModel>();
                vm.WeighbridgeRecordId = preload.id;
                SimpleIoc.Default.GetInstance<MainWindowViewModel>().CurrentViewModel = vm;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void MouseUpCommand(string name)
        {
            if (name == "RefNumber")
            {
                var dialogViewModel = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogViewModel.HeaderText = "Enter Reference Number";
                bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogViewModel);
                if (success == true)
                {
                    RefNumber = dialogViewModel.KeypadValue;
                }
            }

            NextCommand.RaiseCanExecuteChanged();
        }

        public void Load(FrameworkElement element)
        {
            RefNumber = null;
            ErrorMessage = null;
        }

        public void Unload(FrameworkElement element)
        {
            RefNumber = null;
            ErrorMessage = null;
        }
    }
}
