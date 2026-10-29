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
    public class EditProductionPalletViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IPalletRepository _palletRepo = SimpleIoc.Default.GetInstance<IPalletRepository>();

        #endregion

        #region Properties

        public int PalletId { get; set; }

        private Pallet _CurrentPallet;
        public Pallet CurrentPallet
        {
            get { return _CurrentPallet; }
            set { Set(ref _CurrentPallet, value); }
        }

        //        private DateTime? _DateOfIntake;
        //        public DateTime? DateOfIntake
        //        {
        //            get { return _DateOfIntake; }
        //            set { Set(ref _DateOfIntake, value); }
        //        }
        //
        //        private DateTime? _DateOfProduction;
        //        public DateTime? DateOfProduction
        //        {
        //            get { return _DateOfProduction; }
        //            set { Set(ref _DateOfProduction, value); }
        //        }


        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            CurrentPallet = await _palletRepo.GetPalletAsync(PalletId);
        }

        private async void OnSave()
        {
            await _palletRepo.AddUpdatePalletAsync(CurrentPallet);
            DialogResult = true;
        }

        private bool CanSave()
        {
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentPallet = null;
            PalletId = 0;
            DialogResult = null;
        }
    }
}
