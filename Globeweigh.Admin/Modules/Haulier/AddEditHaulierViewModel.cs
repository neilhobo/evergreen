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
    public class AddEditHaulierViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IHaulierRepository _haulierRepo =SimpleIoc.Default.GetInstance<IHaulierRepository>();
        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();

        #endregion

        #region Properties

        public int HaulierId { get; set; }

        private Haulier _CurrentHaulier;
        public Haulier CurrentHaulier
        {
            get { return _CurrentHaulier; }
            set { Set(ref _CurrentHaulier, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            if (HaulierId == 0)
            {
                CurrentHaulier = new Haulier();
            }
            else
            {
                CurrentHaulier = await _haulierRepo.GetHaulier(HaulierId);
            }
        }

        private async void OnSave()
        {
            await _haulierRepo.AddUpdateHaulier(CurrentHaulier);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentHaulier == null) return false;
            // if (CurrentHaulier.StockAreaId ==0) return false;
            if (string.IsNullOrEmpty(CurrentHaulier.Name)) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentHaulier = null;
            HaulierId = 0;
            DialogResult = null;
        }
    }
}
