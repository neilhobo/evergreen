using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MvvmDialogs;
using System.Windows;
using System.Windows.Threading;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Network;
using Globeweigh.Shared.Services;
using System.Net;
using System.Text;
using System.Net.Sockets;
using System.Collections.ObjectModel;
using System.Linq;
using DevExpress.Mvvm.Native;
using Globeweigh.Model.Custom;
using System.ComponentModel;

namespace Globeweigh.Admin
{
    public class CreateLoadViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IHaulierRepository _haulierRepo = SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IDispatchOrderRepository _dispatchRepo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();

        #endregion

        #region Properties

        public int LoadId { get; set; }


        private ObservableCollection<vwDispatchOrder> _DispatchOrderList;
        public ObservableCollection<vwDispatchOrder> DispatchOrderList
        {
            get { return _DispatchOrderList; }
            set { Set(ref _DispatchOrderList, value); }
        }

        private bool _InEditMode;
        public bool InEditMode
        {
            get { return _InEditMode; }
            set { Set(ref _InEditMode, value); }
        }

        private WeighbridgeRecord _CurrentWeighbridgeRecord;
        public WeighbridgeRecord CurrentWeighbridgeRecord
        {
            get { return _CurrentWeighbridgeRecord; }
            private set { Set(ref _CurrentWeighbridgeRecord, value); }
        }

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
            set
            {
                Set(ref _SelectedHaulier, value);
            }
        }

        private ObservableCollection<SortOrderItem> _SortOrderList;
        public ObservableCollection<SortOrderItem> SortOrderList
        {
            get => _SortOrderList;
            set => Set(ref _SortOrderList, value);
        }


        #endregion

        #region Commands

        // public RelayCommand EditSaveCommand => new RelayCommand(OnEditSave);
        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        private async Task RefreshDispatchOrders()
        {
            try
            {
                IsBusy = true;
                var list = await _dispatchRepo.GetOpenDispatchOrderViewsNoOnOrder(LoadId);

                if (CurrentWeighbridgeRecord.OrderViewList != null)
                {
                    foreach (var alreadySelected in CurrentWeighbridgeRecord.OrderViewList)
                    {
                        var match = list.FirstOrDefault(a => a.id == alreadySelected.id);
                        if (match != null) match.IsSelected = true;
                    }
                }

                foreach (var item in list)
                {
                    item.PropertyChanged += DispatchOrder_PropertyChanged;
                }

                DispatchOrderList = new ObservableCollection<vwDispatchOrder>(list.OrderByDescending(a => a.IsSelected));
                UpdateSortOrderList();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void DispatchOrder_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is vwDispatchOrder order && e.PropertyName == nameof(vwDispatchOrder.IsSelected))
            {
                if (!order.IsSelected)
                {
                    order.SortOrder = null;
                }

                UpdateSortOrderList();
            }
        }


        private void UpdateSortOrderList()
        {
            var selectedCount = DispatchOrderList?.Count(o => o.IsSelected) ?? 0;
            var newList = new ObservableCollection<SortOrderItem>();

            for (int i = 0; i < selectedCount; i++)
            {
                newList.Add(new SortOrderItem
                {
                    Value = i + 1, // 1-based index
                    Description = ((char)('A' + i)).ToString()
                });
            }

            SortOrderList = newList;
        }



        // private async void OnEditSave()
        // {
        //     await _weighbridgeRepo.AddUpdateAsync(CurrentWeighbridgeRecord);
        //     DialogResult = true;
        // }

        private async void OnSave()
        {
            // Loads raised from Dispatch are always Collections (outgoing).
            CurrentWeighbridgeRecord.IsOutgoing = true;

            await _weighbridgeRepo.AddUpdateOutgoing(CurrentWeighbridgeRecord, DispatchOrderList.Where(a => a.IsSelected).ToList());

            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentWeighbridgeRecord == null) return false;
            if (CurrentWeighbridgeRecord.HaulierId == 0) return false;
            if (SelectedHaulier == null) return false;
            return true;
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            HaulierList = new List<Haulier>(await _haulierRepo.GetHaulierList());

            if (LoadId == 0)
            {
                CurrentWeighbridgeRecord = new WeighbridgeRecord();
            }
            else
            {
                CurrentWeighbridgeRecord = await _weighbridgeRepo.GetWeighbridgeRecord(LoadId,  true, false);
            }

            await RefreshDispatchOrders();
        }

        public void Unload(FrameworkElement element)
        {
            CurrentWeighbridgeRecord = null;
            LoadId = 0;
            DialogResult = null;
            SelectedHaulier = null;
            InEditMode = false;
        }

    }
}
