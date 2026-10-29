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
    public class EditMultipleProductionPalletsViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();

        #endregion

        #region Properties

        private DateTime? _DateOfIntake;
        public DateTime? DateOfIntake
        {
            get { return _DateOfIntake; }
            set { Set(ref _DateOfIntake, value); }
        }

        private DateTime? _DateOfProduction;
        public DateTime? DateOfProduction
        {
            get { return _DateOfProduction; }
            set { Set(ref _DateOfProduction, value); }
        }

        private List<vwPalletView> _SelectedPallets;
        public List<vwPalletView> SelectedPallets
        {
            get { return _SelectedPallets; }
            set { Set(ref _SelectedPallets, value); }
        }


        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {

        }

        private async void OnSave()
        {
            await _palletRepo.UpdatePalletsDateOfProduction(SelectedPallets, (DateTime)DateOfProduction);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (DateOfProduction == null) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            SelectedPallets = null;
            DateOfIntake = null;
            DialogResult = null;
        }
    }
}
