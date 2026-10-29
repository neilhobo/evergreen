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
    public class AddEditCostingZoneViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly ICostingRepository _costRepo = SimpleIoc.Default.GetInstance<ICostingRepository>();

        #endregion

        #region Properties

        public int CostingZoneId { get; set; }

        private CostingZone _CurrentCostingZone;
        public CostingZone CurrentCostingZone
        {
            get { return _CurrentCostingZone; }
            set { Set(ref _CurrentCostingZone, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion


        public async void Load(FrameworkElement element)
        {
            CurrentCostingZone = await _costRepo.GetCostingZone(CostingZoneId);
        }

        private async void OnSave()
        {
            await _costRepo.AddUpdateCostingZone(CurrentCostingZone);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentCostingZone == null) return false;
            if (string.IsNullOrEmpty(CurrentCostingZone.Description)) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentCostingZone = null;
            DialogResult = null;
        }
    }
}
