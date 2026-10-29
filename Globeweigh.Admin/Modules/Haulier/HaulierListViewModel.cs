using System;
using System.Collections.Generic;
using Globeweigh.Shared.Services;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Helpers;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using IDialogService = MvvmDialogs.IDialogService;
using System.Threading.Tasks;

namespace Globeweigh.Admin
{
    public class HaulierListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private List<Haulier> _HaulierList;
        public List<Haulier> HaulierList
        {
            get { return _HaulierList; }
            set { Set(ref _HaulierList, value); }
        }

        private Haulier _SelectedHaulier;
        public Haulier SelectedHaulier
        {
            get { return _SelectedHaulier; }
            set { Set(ref _SelectedHaulier, value); }
        }

        #endregion

        #region Commands

        public RelayCommand AddCommand => new RelayCommand(OnAdd);
        public RelayCommand EditCommand => new RelayCommand(OnEdit, CanEdit);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete, CanDelete);

        #endregion

        private bool CanEdit()
        {
            if (SelectedHaulier == null) return false;
            return true;
        }

        private bool CanDelete()
        {
            if (SelectedHaulier == null) return false;
            return true;
        }


        private void OnAdd()
        {
            AddEditUserDialog(0);
        }
        private void OnEdit()
        {
            AddEditUserDialog(SelectedHaulier.id);
        }
        private async void OnDelete()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Haulier?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _haulierRepo.MarkAsDeletedAsync(SelectedHaulier.id);
            await RefreshList();
            SelectedHaulier = null;
        }

        private async void AddEditUserDialog(int id)
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditHaulierViewModel>();
            dialogViewModel.HaulierId = id;
            if (id == 0) dialogViewModel.HeaderText = "Add New Haulier";
            else dialogViewModel.HeaderText = "Edit Haulier";
            bool? success = _dialogService.ShowDialog<AddEditHaulierView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedHaulier = null;
                await RefreshList();
            }
        }

        private async Task RefreshList()
        {
            HaulierList = new List<Haulier>(await _haulierRepo.GetHaulierList());
        }


        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            await RefreshList();
        }

        public void Unload(FrameworkElement element)
        {
            HaulierList = null;
            SelectedHaulier = null;
        }

    }
}
