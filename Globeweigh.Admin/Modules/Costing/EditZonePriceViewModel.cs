using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;

namespace Globeweigh.Admin
{
    public class EditZonePriceViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly ICostingRepository _costRepo = SimpleIoc.Default.GetInstance<ICostingRepository>();

        #endregion

        #region Properties

        // public int ZonePriceId { get; set; }

        private CostingZonePrice _CurrentCostingZonePrice;
        public CostingZonePrice CurrentCostingZonePrice
        {
            get { return _CurrentCostingZonePrice; }
            private set { Set(ref _CurrentCostingZonePrice, value); }
        }

        private vwCostingZonePrice _SelectedZonePrice;
        public vwCostingZonePrice SelectedZonePrice
        {
            get { return _SelectedZonePrice; }
            set { Set(ref _SelectedZonePrice, value); }
        }

        private vwProduct _SelectedProduct;
        public vwProduct SelectedProduct
        {
            get { return _SelectedProduct; }
            set { Set(ref _SelectedProduct, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion


        public async void Load(FrameworkElement element)
        {
            CurrentCostingZonePrice = await _costRepo.GetCostZonePrice(SelectedZonePrice.id);
        }

        private async void OnSave()
        {
            await _costRepo.UpdateCostZonePrice(CurrentCostingZonePrice.Price, SelectedZonePrice);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentCostingZonePrice == null) return false;
            if (CurrentCostingZonePrice.Price == 0) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentCostingZonePrice = null;
            DialogResult = null;
        }
    }
}
