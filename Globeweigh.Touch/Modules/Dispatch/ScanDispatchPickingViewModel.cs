using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Shared;
using Globeweigh.Model;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using System.Windows.Input;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Touch
{
    public class ScanDispatchPickingViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();
        private readonly IStockRepository _stockRepo = SimpleIoc.Default.GetInstance<IStockRepository>();
        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();
        KeyConverter mScanKeyConverter = new KeyConverter();
        private bool finishedScanning;
        private string sResult;

        #endregion

        #region Properties

        private vwDispatchOrderItem _SelectedOrderItem;
        public vwDispatchOrderItem SelectedOrderItem
        {
            get { return _SelectedOrderItem; }
            set { Set(ref _SelectedOrderItem, value); }
        }

        private vwPalletView _ScannedStockUnit;
        public vwPalletView ScannedStockUnit
        {
            get { return _ScannedStockUnit; }
            set { Set(ref _ScannedStockUnit, value); }
        }

        private DispatchOrder_Picked _CurrentPickedItem;
        public DispatchOrder_Picked CurrentPickedItem
        {
            get { return _CurrentPickedItem; }
            set { Set(ref _CurrentPickedItem, value); }
        }

        private ObservableCollection<vwPalletView> _StockUnitList;
        public ObservableCollection<vwPalletView> StockUnitList
        {
            get { return _StockUnitList; }
            set { Set(ref _StockUnitList, value); }
        }

        private vwPalletView _SelectedStockUnit;
        public vwPalletView SelectedStockUnit
        {
            get { return _SelectedStockUnit; }
            set
            {
                Set(ref _SelectedStockUnit, value);
                if (SelectedStockUnit != null)
                {
                    GetStockUnit(SelectedStockUnit.id);
                }
            }
        }

        private string _ScanMessage;
        public string ScanMessage
        {
            get { return _ScanMessage; }
            private set { Set(ref _ScanMessage, value); }
        }

        private int? _QuantityToAdd;
        public int? QuantityToAdd
        {
            get { return _QuantityToAdd; }
            set { Set(ref _QuantityToAdd, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);
        public RelayCommand EnterIdManuallyCommand => new RelayCommand(OnEnterIdManually);
        public RelayCommand<KeyEventArgs> KeyPreviewDownCommand => new RelayCommand<KeyEventArgs>(OnKeyPreviewDown);

        #endregion

        private async void OnEnterIdManually()
        {
            var dialogVm = SimpleIoc.Default.GetInstance<KeypadViewModel>();
            dialogVm.HeaderText = "Enter Transfer ID:";
            bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogVm);
            if (success == true)
            {
                var stockId = Convert.ToInt32(dialogVm.KeypadValue);
                await GetStockUnit(stockId);
            }
        }

        public async void MouseUpCommand(string name)
        {
            if (name == "QuantityToAdd")
            {
                var dialogVm = SimpleIoc.Default.GetInstance<KeypadViewModel>();
                dialogVm.HeaderText = "Enter Quantity";
                // decimal maxValue;
                // if (ScannedStockUnit.QuantityAvailable > SelectedOrderItem.QuantityRequired) maxValue = SelectedOrderItem.QuantityRequired;
                // else
                // {
                //     maxValue = ScannedStockUnit.QuantityAvailable;
                // }
                // dialogVm.MaxValue = Convert.ToInt32(maxValue);

                bool? success = _dialogService.ShowDialog<KeypadView>(this, dialogVm);
                if (success == true)
                {
                    QuantityToAdd = Convert.ToInt32(dialogVm.KeypadValue);
                }
            }

        }

        private async Task GetStockUnit(int stockUnitId)
        {
            ScannedStockUnit = null;
            var scannedEntity = await _palletRepo.GetPalletViewAsync(stockUnitId);
            if (scannedEntity != null)
            {
                if (scannedEntity.ProductId == SelectedOrderItem.ProductId)
                {
                    ScannedStockUnit = scannedEntity;
                    CurrentPickedItem = new DispatchOrder_Picked();
                    CurrentPickedItem.DispatchOrderId = SelectedOrderItem.OrderId;
                    CurrentPickedItem.DispatchOrderItemId = SelectedOrderItem.id;
                    // CurrentPickedItem.PalletId = ScannedStockUnit.id;
                    CurrentPickedItem.ProductId = ScannedStockUnit.ProductId;
                }
                else
                {
                    ScannedStockUnit = null;
                    ScanMessage = "WRONG PRODUCT SCANNED";
                }
            }
            else
            {
                ScannedStockUnit = null;
                ScanMessage = "Pallet cannot be found";
            }
        }

        private async void OnKeyPreviewDown(KeyEventArgs e)
        {
            if (CurrentPickedItem != null) return;

            if (UtilitiesShared.IsMyMachine)
            {
                if (e.Key == Key.Enter)
                {
                    var randomPallet = await _stockRepo.GetRandomStockUnitAsync(SelectedOrderItem.ProductId, true);
                    if (randomPallet != null)
                    {
                       await GetStockUnit(randomPallet.id);
                        return;
                    }

                    ScanMessage = "Pallet cannot be found";
                    return;
                }
            }

            if (e.Key != Key.D0 && e.Key != Key.D1 && e.Key != Key.D2 && e.Key != Key.D3 && e.Key != Key.D4 &&
                e.Key != Key.D5 && e.Key != Key.D6 && e.Key != Key.D7 &&
                e.Key != Key.D8 && e.Key != Key.D9 && e.Key != Key.OemOpenBrackets && e.Key != Key.OemCloseBrackets)
            {
                if (e.Key == Key.Return)
                {
                    e.Handled = true;
                    finishedScanning = true;

                    if (sResult == string.Empty)
                    {
                    }
                    else
                    {
                        int stockUnitId = BarcodeHelper.GetEntityId(sResult, (int)eBarcodeType.Pallet);
                        if (stockUnitId == -1)
                        {
                            ScanMessage = "Wrong barcode type scanned";
                            sResult = string.Empty;
                            return;
                        }
                        await GetStockUnit(stockUnitId);
                    }
                    sResult = string.Empty;
                    return;
                }

                sResult = string.Empty;
                return;
            }
            string xChar;
            if (e.Key == Key.OemOpenBrackets)
                xChar = "[";
            else if (e.Key == Key.OemCloseBrackets)
                xChar = "]";
            else
            {
                xChar = mScanKeyConverter.ConvertToString(e.Key);
            }

            sResult += xChar;
        }

        private async void OnSave()
        {
            CurrentPickedItem.Quantity = (int)QuantityToAdd;

            var list = new List<DispatchOrder_Picked> { CurrentPickedItem };
            await _dispatchRepo.AddUpdateDispatchOrderPickedItemList(list);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (QuantityToAdd == null) return false;
            if (QuantityToAdd == 0) return false;
            // if (QuantityToAdd > SelectedOrderItem.QuantityRequired) return false;
            return true;
        }

        public async void Load(FrameworkElement element)
        {
            ScanMessage = "Scan Stock Item";
        }

        public void Unload(FrameworkElement element)
        {
            CurrentPickedItem = null;
            ScannedStockUnit = null;
            ScannedStockUnit = null;
            IsBusy = false;
            DialogResult = null;
            QuantityToAdd = null;
            SelectedStockUnit = null;
        }
    }
}
