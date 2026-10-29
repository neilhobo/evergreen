using System;
using System.Collections.Generic;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using IDialogService = MvvmDialogs.IDialogService;
using System.Threading.Tasks;
using System.Windows.Controls;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class CostingZoneListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly ICostingRepository _costingRepo = SimpleIoc.Default.GetInstance<ICostingRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private List<CostingZone> _CostingZoneList;
        public List<CostingZone> CostingZoneList
        {
            get { return _CostingZoneList; }
            set { Set(ref _CostingZoneList, value); }
        }

        private CostingZone _SelectedCostingZone;
        public CostingZone SelectedCostingZone
        {
            get { return _SelectedCostingZone; }
            set { Set(ref _SelectedCostingZone, value); }
        }

        #endregion

        #region Commands

        public RelayCommand AddCommand => new RelayCommand(OnAdd);
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);

        #endregion

        private void OnAdd()
        {
            AddEditUserDialog(0);
        }

        private void OnEdit()
        {
            AddEditUserDialog(SelectedCostingZone.id);
        }

        private async void OnDelete()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete User?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _costingRepo.MarkCostingZoneAsDeleted(SelectedCostingZone.id);
            await RefreshList();
            SelectedCostingZone = null;
        }

        private async void AddEditUserDialog(int id)
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditCostingZoneViewModel>();
            dialogViewModel.CostingZoneId = id;
            if (id == 0) dialogViewModel.HeaderText = "Add New Zone";
            else dialogViewModel.HeaderText = "Edit Zone";
            bool? success = _dialogService.ShowDialog<AddEditCostingZoneView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedCostingZone = null;
                await RefreshList();
            }
        }

        private async Task RefreshList()
        {
            CostingZoneList = new List<CostingZone>(await _costingRepo.GetCostingZones());
        }


        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            await RefreshList();
        }

        public void Unload(FrameworkElement element)
        {
            CostingZoneList = null;
            SelectedCostingZone = null;
        }

    }
}
