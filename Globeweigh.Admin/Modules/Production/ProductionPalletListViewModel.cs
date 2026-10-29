using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using MvvmDialogs;

namespace Globeweigh.Admin
{
    public class ProductionPalletListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private List<vwCodicoPallet> _PalletList;
        public List<vwCodicoPallet> PalletList
        {
            get { return _PalletList; }
            set { Set(ref _PalletList, value); }
        }

        private vwCodicoPallet _SelectedPallet;
        public vwCodicoPallet SelectedPallet
        {
            get { return _SelectedPallet; }
            set { Set(ref _SelectedPallet, value); }
        }

        private DateTime _DateFrom;
        public DateTime DateFrom
        {
            get { return _DateFrom; }
            set { Set(ref _DateFrom, value); }
        }

        private DateTime _DateTo;
        public DateTime DateTo
        {
            get { return _DateTo; }
            set { Set(ref _DateTo, value); }
        }

        private ObservableCollection<vwCodicoPallet> _SelectedPallets;
        public ObservableCollection<vwCodicoPallet> SelectedPallets
        {
            get { return _SelectedPallets; }
            set { Set(ref _SelectedPallets, value); }
        }

        #endregion

        #region Commands

        public RelayCommand RefreshCommand => new RelayCommand(async () => await RefreshPallets());
        public RelayCommand EditCommand => new RelayCommand(OnEdit, CanEdit);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete, CanDelete);
        public RelayCommand AddPalletsCommand => new RelayCommand(OnAddPalletPallets);

        #endregion

        private bool CanEdit()
        {
            if (SelectedPallets == null || SelectedPallets.Count == 0) return false;
            if (SelectedPallets.Count > 1) return false;
            return true;
        }

        private bool CanDelete()
        {
            if (SelectedPallets == null || SelectedPallets.Count == 0) return false;
            return true;
        }

        private async void OnDelete()
        {
            // var result = _dialogService.ShowMessageBox(this, "Delete Pallet(s)?", "", MessageBoxButton.OKCancel);
            // if (result == MessageBoxResult.Cancel) return;
            //
            // await _palletRepo.UpdatePalletsDateDeleted(SelectedPallets.ToList(), DateTime.Now);
            // await RefreshPallets();
        }


        private async void OnAddPalletPallets()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddProductionPalletsViewModel>();
            bool? success = _dialogService.ShowDialog<AddProductionPalletsView>(this, dialogViewModel);
            if (success == true)
            {
                await RefreshPallets();
                SelectedPallet = null;
            }
        }

        private async void OnEdit()
        {
            if (SelectedPallets == null) return;
            if (SelectedPallets.Count == 1)
            {
                var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddProductionPalletsViewModel>();
                dialogViewModel.HeaderText = "Edit Pallet";
                dialogViewModel.PalletId = SelectedPallets.FirstOrDefault().id;
                bool? success = _dialogService.ShowDialog<AddProductionPalletsView>(this, dialogViewModel);
                if (success == true)
                {
                    await RefreshPallets();
                }
            }
            else
            {
                // var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<EditMultipleProductionPalletsViewModel>();
                // dialogViewModel.HeaderText = "Edit Pallet";
                // dialogViewModel.SelectedPallets = new List<vwCodicoPallet>(SelectedPallets);
                // bool? success = _dialogService.ShowDialog<EditMultipleProductionPalletsView>(this, dialogViewModel);
                // if (success == true)
                // {
                //     await RefreshPallets();
                // }
            }

        }

        private async Task RefreshPallets()
        {
            try
            {
                IsBusy = true;
                PalletList = new List<vwCodicoPallet>(await _palletRepo.GetProductionPalletsAsync(DateFrom, DateTo.AddDays(1)));
                SelectedPallet = null;
                SelectedPallets = new ObservableCollection<vwCodicoPallet>();
            }
            catch
            {
                IsBusy = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async void Load(FrameworkElement element)
        {
            DateFrom = DateTime.Today.AddDays(-5);
            DateTo = DateTime.Today.AddDays(1);
            await RefreshPallets();
            SelectedPallets = new ObservableCollection<vwCodicoPallet>();

        }

        public void Unload(FrameworkElement element)
        {
            PalletList = null;
            SelectedPallet = null;
        }
    }
}
