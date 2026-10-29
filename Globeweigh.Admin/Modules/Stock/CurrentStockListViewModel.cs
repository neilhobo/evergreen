using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class CurrentStockListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IStockRepository _stockRepo = SimpleIoc.Default.GetInstance<IStockRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        public int WarehouseId { get; set; }

        private List<vwPalletCurrentStock> _PalletList;
        public List<vwPalletCurrentStock> PalletList
        {
            get { return _PalletList; }
            set { Set(ref _PalletList, value); }
        }
        //
        private vwPalletCurrentStock _SelectedPallet;
        public vwPalletCurrentStock SelectedPallet
        {
            get { return _SelectedPallet; }
            set { Set(ref _SelectedPallet, value); }
        }

        #endregion

        #region Commands

        public RelayCommand RefreshCommand => new RelayCommand(async () => await RefreshStock());
        //        public RelayCommand ViewCommand => new RelayCommand(async () => await OnViewCommand());
        public RelayCommand OnHoldReleaseCommand => new RelayCommand(OnBlockRelease);
        public RelayCommand<object> ViewPalletHistoryCommand => new RelayCommand<object>(OnViewPalletHistory);



        #endregion

        private void OnViewPalletHistory(object obj)
        {
            if (obj == null) return;
            int palletId = (int)obj;


            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<PalletHistoryViewModel>();
            dialogVm.PalletId = palletId;
            _dialogService.ShowDialog<PalletHistoryView>(this, dialogVm);
        }

        private async void OnBlockRelease()
        {
            if (SelectedPallet == null) return;

            if (SelectedPallet.LocationId == -2)
            {
                MessageBox.Show("You cannot put a PICKED pallet on hold");
                return;
            }

            // if (SelectedPallet.PickedOrderId != null)
            // {
            //     MessageBox.Show("This pallet is currently on a dispatch and cannot be put on hold.");
            //     return;
            // }

            //SelectedPallet.Highlighted
            //await _stockRepo.UpdatePalletsOnHoldReleasedAsync(SelectedPallet.id, SelectedPallet.OnHold);
            //SelectedPallet.OnHold = !SelectedPallet.OnHold;
            SelectedPallet.RaisePropertiesChangedManual();
            SelectedPallet = null;
        }

        private async Task OnViewCommand()
        {
        }

        private async Task RefreshStock()
        {
            if(WarehouseId ==0) PalletList = new List<vwPalletCurrentStock>(await _stockRepo.GetCurrentStock());
            else
            {
                PalletList = new List<vwPalletCurrentStock>(await _stockRepo.GetCurrentStock());
            }
        }

        public async void Load(FrameworkElement element)
        {
            await RefreshStock();
        }

        public void Unload(FrameworkElement element)
        {
            PalletList = null;
            SelectedPallet = null;
            WarehouseId =0 ;
        }
    }
}
