using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;
using GalaSoft.MvvmLight.Ioc;

namespace Globeweigh.Admin
{
    public class StockLocationListViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IStockLocationRepository _stockLocationRepo = SimpleIoc.Default.GetInstance<IStockLocationRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private StockLocation _SelectedLocation;
        public StockLocation SelectedLocation
        {
            get { return _SelectedLocation; }
            set { Set(ref _SelectedLocation, value); }
        }

        private List<StockLocation> _LocationList;
        public List<StockLocation> LocationList
        {
            get { return _LocationList; }
            set { Set(ref _LocationList, value); }
        }



        #endregion

        #region Commands

        public RelayCommand AddNewCommand => new RelayCommand(OnAdd);
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);

        #endregion

        private async void OnAdd()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditLocationViewModel>();
            bool? success = _dialogService.ShowDialog<AddEditLocationView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedLocation = null;
                await RefreshList();
            }

        }

        private async void OnEdit()
        {
            if (SelectedLocation == null) return;
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditLocationViewModel>();
            dialogViewModel.LocationId = SelectedLocation.id;
            bool? success = _dialogService.ShowDialog<AddEditLocationView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedLocation = null;
                await RefreshList();
            }

        }

        private async void OnDelete()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Item?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _stockLocationRepo.MarkAsDeletedAsync(SelectedLocation.id);
            await RefreshList();
        }


        public async Task RefreshList()
        {
            LocationList = new List<StockLocation>(await _stockLocationRepo.GetStockLocations());
        }


        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            await RefreshList();
        }

        public void Unload(FrameworkElement element)
        {
            LocationList = null;
            SelectedLocation = null;
            HeaderText = null;
        }
    }
}
