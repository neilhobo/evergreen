using System;
using System.Collections.Generic;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;

namespace Globeweigh.Admin
{
    public class EditCompletedDispatchViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IDispatchOrderRepository _repo = SimpleIoc.Default.GetInstance<IDispatchOrderRepository>();

        #endregion

        #region Properties

        public int DispatchOrderId { get; set; }

        private DispatchOrder _CurrentDispatchOrder;
        public DispatchOrder CurrentDispatchOrder
        {
            get { return _CurrentDispatchOrder; }
            set { Set(ref _CurrentDispatchOrder, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            CurrentDispatchOrder = await _repo.GetDispatchOrder(DispatchOrderId);
        }

        private async void OnSave()
        {
            await _repo.EditCompletedDispatchedOrder(DispatchOrderId, CurrentDispatchOrder.DateDispatched);
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
