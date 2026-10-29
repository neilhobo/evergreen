using System.Collections.Generic;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class WeighbridgeSetupViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private readonly IWeighbridgeRepository _weighbridgeRepo = SimpleIoc.Default.GetInstance<IWeighbridgeRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private List<ReferenceData> _HaulierList;
        public List<ReferenceData> HaulierList
        {
            get { return _HaulierList; }
            private set { Set(ref _HaulierList, value); }
        }

        private List<ReferenceData> _VehicleList;
        public List<ReferenceData> VehicleList
        {
            get { return _VehicleList; }
            private set { Set(ref _VehicleList, value); }
        }

        private ReferenceData _SelectedHaulier;
        public ReferenceData SelectedHaulier
        {
            get { return _SelectedHaulier; }
            set
            {
                Set(ref _SelectedHaulier, value);
                OnHaulierChanged();
            }
        }

        private ReferenceData _SelectedVehicle;
        public ReferenceData SelectedVehicle
        {
            get { return _SelectedVehicle; }
            set { Set(ref _SelectedVehicle, value); }
        }

        #endregion

        #region Commands

        public RelayCommand AddHaulierCommand => new RelayCommand(OnAddHaulier);
        public RelayCommand DeleteHaulierCommand => new RelayCommand(OnDeleteHaulier);
        public RelayCommand EditHaulierCommand => new RelayCommand(OnEditHaulier);
        public RelayCommand AddVehicleCommand => new RelayCommand(OnAddVehicle);
        public RelayCommand DeleteVehicleCommand => new RelayCommand(OnDeleteVehicle);
        public RelayCommand EditVehicleCommand => new RelayCommand(OnEditVehicle);

        #endregion


        private async void OnAddHaulier()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditGenericDataViewModel>();
            dialogVm.LabelText = "Haulier Name:";
            dialogVm.HeaderText = "Add Haulier";
            bool? success = _dialogService.ShowDialog<AddEditGenericDataView>(this, dialogVm);
            if (success == true)
            {
                var newRefData = new ReferenceData();
                newRefData.RefTypeId = (int)eRefDataType.Haulier;
                newRefData.DisplayName = dialogVm.TransactionText;
                await _refDataRepo.AddUpdateRefDataAsync(newRefData);
                await RefreshHaulierList();
            }
        }

        private async void OnDeleteHaulier()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Haulier?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _weighbridgeRepo.DeleteHaulier(SelectedHaulier.id);
            await RefreshHaulierList();
            await RefreshVehicleList();
        }

        private async void OnEditHaulier()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditGenericDataViewModel>();
            dialogVm.LabelText = "Haulier Name:";
            dialogVm.TransactionText = SelectedHaulier.DisplayName;
            dialogVm.HeaderText = "Edit Haulier";
            bool? success = _dialogService.ShowDialog<AddEditGenericDataView>(this, dialogVm);
            if (success == true)
            {
                SelectedHaulier.DisplayName = dialogVm.TransactionText;
                await _refDataRepo.AddUpdateRefDataAsync(SelectedHaulier);
                await RefreshHaulierList();
            }
        }

        private async void OnAddVehicle()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditGenericDataViewModel>();
            dialogVm.LabelText = "Vehicle Reg:";
            dialogVm.HeaderText = "Add Vehicle";
            dialogVm.CharacterCasing = "Upper";
            bool? success = _dialogService.ShowDialog<AddEditGenericDataView>(this, dialogVm);
            if (success == true)
            {
                var newRefData = new ReferenceData();
                // newRefData.RefTypeId = (int)eRefDataType.HaulierVehicle;
                newRefData.DisplayName = dialogVm.TransactionText;
                newRefData.IntData = SelectedHaulier.id;
                await _refDataRepo.AddUpdateRefDataAsync(newRefData);
                await OnHaulierChanged();
            }
        }

        private async void OnDeleteVehicle()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Vehicle?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _weighbridgeRepo.DeleteVehicle(SelectedVehicle.id);
            await RefreshVehicleList();
        }

        private async void OnEditVehicle()
        {
            var dialogVm = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditGenericDataViewModel>();
            dialogVm.CharacterCasing = "Upper";
            dialogVm.LabelText = "Vehicle Reg:";
            dialogVm.TransactionText = SelectedVehicle.DisplayName;
            dialogVm.HeaderText = "Edit Vehicle";
            bool? success = _dialogService.ShowDialog<AddEditGenericDataView>(this, dialogVm);
            if (success == true)
            {
                SelectedVehicle.DisplayName = dialogVm.TransactionText;
                await _refDataRepo.AddUpdateRefDataAsync(SelectedVehicle);
                await RefreshVehicleList();
            }
        }


        private async Task RefreshHaulierList()
        {
            HaulierList = new List<ReferenceData>(await _refDataRepo.GetReferenceData(eRefDataType.Haulier));
            VehicleList = null;
            SelectedHaulier = null;
            SelectedVehicle = null;
        }

        private async Task OnHaulierChanged()
        {
            if (SelectedHaulier == null) return;
            await RefreshVehicleList();
        }

        private async Task RefreshVehicleList()
        {
            if(SelectedHaulier == null)return;
            // VehicleList = new List<ReferenceData>(await _weighbridgeRepo.GetVehiclesByHaulierId(SelectedHaulier.id));
            SelectedVehicle = null;
        }

        public async void Load(FrameworkElement element)
        {
            await RefreshHaulierList();
        }

        public void Unload(FrameworkElement element)
        {
            VehicleList = null;
            HaulierList = null;
            SelectedHaulier = null;
            SelectedVehicle = null;
        }
    }
}
