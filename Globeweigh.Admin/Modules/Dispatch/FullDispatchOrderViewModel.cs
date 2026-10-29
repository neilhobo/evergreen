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
    public class FullDispatchOrderViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IDispatchOrderRepository _repo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();

        #endregion

        #region Properties

        public int DispatchOrderId { get; set; }

        private vwDispatchOrder _CurrentDispatchOrder;
        public vwDispatchOrder CurrentDispatchOrder
        {
            get { return _CurrentDispatchOrder; }
            set { Set(ref _CurrentDispatchOrder, value); }
        }

        private List<vwDispatchOrderItem> _DispatchOrderItemList;
        public List<vwDispatchOrderItem> DispatchOrderItemList
        {
            get { return _DispatchOrderItemList; }
            set { Set(ref _DispatchOrderItemList, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            CurrentDispatchOrder = await _repo.GetDispatchOrderView(DispatchOrderId, true, true);
            DispatchOrderItemList = new List<vwDispatchOrderItem>(await _repo.GetDispatchOrderItemViews(DispatchOrderId));

            // DispatchOrderItemList.FirstOrDefault().p
        }

        private async void OnSave()
        {
            DialogResult = true;
        }

        private bool CanSave()
        {
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentDispatchOrder = null;
            DispatchOrderId = 0;
            DialogResult = null;
        }
    }
}
