using System;
using System.Collections.Generic;
using System.Linq;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;

namespace Globeweigh.Admin
{
    public class AutoPickViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IDispatchOrderRepository _repo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();
        private readonly IStockLocationRepository _stockLocationRepo = SimpleIoc.Default.GetInstance<IStockLocationRepository>();

        #endregion

        #region Properties

        public int DispatchOrderId { get; set; }

        private vwDispatchOrder _CurrentDispatchOrder;
        public vwDispatchOrder CurrentDispatchOrder
        {
            get { return _CurrentDispatchOrder; }
            set { Set(ref _CurrentDispatchOrder, value); }
        }

        private int _LocationId;
        public int LocationId
        {
            get { return _LocationId; }
            set { Set(ref _LocationId, value); }
        }

        private List<StockLocation> _LocationList;
        public List<StockLocation> LocationList
        {
            get { return _LocationList; }
            set { Set(ref _LocationList, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            LocationList = new List<StockLocation>(await _stockLocationRepo.GetStockLocations());
            CurrentDispatchOrder = await _repo.GetDispatchOrderView(DispatchOrderId, true,true);
        }

        private async void OnSave()
        {
            foreach (var orderItem in CurrentDispatchOrder.DispatchOrderItems)
            {
                for (int i = 0; i < orderItem.QuantityRequired; i++)
                {
                    var pallet =await _palletRepo.GetNextAvailablePallet(orderItem.ProductId, LocationId);
                    if(pallet == null)continue;
                    var newPickedItem = new DispatchOrder_Picked();
                    newPickedItem.DispatchOrderId = DispatchOrderId;
                    newPickedItem.DispatchOrderItemId = orderItem.id;
                    newPickedItem.ProductId = orderItem.ProductId;
                    // newPickedItem.PalletId = pallet.id;
                    newPickedItem.Quantity = 1;
                    // pickedItemsToAdd.Add(newPickedItem);

                    await _repo.AddPalletToDispatchOrder(newPickedItem);
                }
            }

            DialogResult = true;
        }

        private bool CanSave()
        {
            if (LocationId == 0) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentDispatchOrder = null;
            DispatchOrderId = 0;
            LocationId = 0;
            DialogResult = null;
        }
    }
}
