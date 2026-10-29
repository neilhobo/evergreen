using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Globeweigh.Shared.Services;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Helpers;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using IDialogService = MvvmDialogs.IDialogService;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Globeweigh.Admin
{
    public class CustomerSupplierListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly ICustomerSupplierRepository _customerSupplierRepo = SimpleIoc.Default.GetInstance<ICustomerSupplierRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();

        #endregion

        #region Properties

        private List<vwCustomerSupplier> _EntityList;
        public List<vwCustomerSupplier> EntityList
        {
            get { return _EntityList; }
            set { Set(ref _EntityList, value); }
        }

        private ObservableCollection<vwCustomerSupplier> _SelectedEntities;
        public ObservableCollection<vwCustomerSupplier> SelectedEntities
        {
            get { return _SelectedEntities; }
            set { Set(ref _SelectedEntities, value); }
        }

        private List<ReferenceData> _FunctionList;
        public List<ReferenceData> FunctionList
        {
            get { return _FunctionList; }
            private set { Set(ref _FunctionList, value); }
        }

        private vwCustomerSupplier _SelectedEntity;
        public vwCustomerSupplier SelectedEntity
        {
            get { return _SelectedEntity; }
            set { Set(ref _SelectedEntity, value); }
        }

        private ListBoxItem _SelectedEntityType;
        public ListBoxItem SelectedEntityType
        {
            get { return _SelectedEntityType; }
            set
            {
                Set(ref _SelectedEntityType, value);
                RefreshList();
            }
        }

        private bool _ShowDeletes;
        public bool ShowDeletes
        {
            get { return _ShowDeletes; }
            set
            {
                Set(ref _ShowDeletes, value);
                RefreshList();
            }
        }

        private int _EntityTypeId;
        public int EntityTypeId
        {
            get { return _EntityTypeId; }
            set { Set(ref _EntityTypeId, value); }
        }

        #endregion

        #region Commands

        public RelayCommand AddCommand => new RelayCommand(OnAdd);
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);
        public RelayCommand MarkInactiveCommand => new RelayCommand(OnMarkInactive);

        #endregion

        private async void OnMarkInactive()
        {
            if(SelectedEntities == null || !SelectedEntities.Any())return;
            var dlgRes = MessageBox.Show("Mark Inactive?", "", MessageBoxButton.OKCancel);
            if (dlgRes == MessageBoxResult.OK)
            {
                await _customerSupplierRepo.MarkInactive(SelectedEntities.ToList(), true);
                await RefreshList();
            }
        }

        private void OnAdd()
        {
            AddEditNewRefDataDialog(0);
        }

        private void OnEdit()
        {
            AddEditNewRefDataDialog(SelectedEntity.id);
        }

        private async void OnDelete()
        {
            if (ShowDeletes)
            {
                var result = _dialogService.ShowMessageBox(this, "Restore Record?", "", MessageBoxButton.OKCancel);
                if (result == MessageBoxResult.Cancel) return;
            }
            else
            {
                var result = _dialogService.ShowMessageBox(this, "Delete Record?", "", MessageBoxButton.OKCancel);
                if (result == MessageBoxResult.Cancel) return;
            }

            await _customerSupplierRepo.MarkAsDeletedAsync(SelectedEntity.id);
            await RefreshList();
            SelectedEntity = null;
        }

        private async void AddEditNewRefDataDialog(int entityId)
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditCustomerSupplierViewModel>();
            dialogViewModel.CustomerSupplierId = entityId;
            dialogViewModel.EntityTypeId = EntityTypeId;
            bool? success = _dialogService.ShowDialog<AddEditCustomerSupplierView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedEntity = null;
                await RefreshList();
            }
        }

        private async Task RefreshList()
        {
            if (SelectedEntityType == null) return;
            switch (SelectedEntityType.Name)
            {
                case "customers":
                    EntityList = new List<vwCustomerSupplier>(await _customerSupplierRepo.GetCustomersSupplierViews((int)eEntityType.Customer, false, false));
                    EntityTypeId = (int)eEntityType.Customer;
                    // HeaderText= "Customers";
                    break;
                case "suppliers":
                    EntityList = new List<vwCustomerSupplier>(await _customerSupplierRepo.GetCustomersSupplierViews((int)eEntityType.Supplier, false, false));

                    EntityTypeId = (int)eEntityType.Supplier;
                    // HeaderText = "Suppliers";
                    break;
                default:
                    break;
            }

            SelectedEntities = new ObservableCollection<vwCustomerSupplier>();
        }

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;

            // await RefreshList();
        }

        public void Unload(FrameworkElement element)
        {
            EntityList = null;
            SelectedEntity = null;
        }

    }
}
