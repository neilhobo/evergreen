using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DevExpress.DataProcessing;
// using DevExpress.XtraEditors.Controls;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class ReferenceDataMasterViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IReferenceDataRepository _refRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();

        #endregion

        #region Properties

        private EntityType _SelectedEntity;
        public EntityType SelectedEntity
        {
            get { return _SelectedEntity; }
            set
            {
                Set(ref _SelectedEntity, value);
                SetCurrentView();
            }
        }

        private object _CurrentViewModel;
        public object CurrentViewModel
        {
            get { return _CurrentViewModel; }
            set { Set(ref _CurrentViewModel, value); }
        }

        private List<EntityType> _EntityList;
        public List<EntityType> EntityList
        {
            get { return _EntityList; }
            set { Set(ref _EntityList, value); }
        }



        #endregion

        #region Commands

        #endregion

        private async void SetCurrentView()
        {
            if (SelectedEntity == null)
            {
                CurrentViewModel = null;
                return;
            }
            if (SelectedEntity.IsReferenceDataType)
            {
                if (SelectedEntity.EntityTypeId == (int)eRefDataType.Haulier)
                {
                    // var vm = SimpleIoc.Default.GetInstance<ProductConditionTypeViewModel>();
                    // vm.SelectedEntity = SelectedEntity;
                    // CurrentViewModel = vm;
                }
                else
                {
                    var vm = SimpleIoc.Default.GetInstance<ReferenceDataViewModel>();
                    vm.SelectedEntity = SelectedEntity;
                    await vm.RefreshList();
                    CurrentViewModel = vm;
                }
            }
            else
            {
                if (SelectedEntity.EntityTypeId == (int)eRefDataType.Product)
                {
                    var vm = SimpleIoc.Default.GetInstance<ProductListViewModel>();
                    CurrentViewModel = vm;
                }
                else if (SelectedEntity.EntityTypeId == (int)eRefDataType.GlobeweighUser)
                {
                    CurrentViewModel = SimpleIoc.Default.GetInstance<GlobeweighUserListViewModel>();
                }
                else if (SelectedEntity.EntityTypeId == (int)eRefDataType.CostingZone)
                {
                    CurrentViewModel = SimpleIoc.Default.GetInstance<CostingZoneListViewModel>();
                }
                else if (SelectedEntity.EntityTypeId == (int)eRefDataType.Address)
                {
                    CurrentViewModel = SimpleIoc.Default.GetInstance<AddressListViewModel>();
                }
                else if (SelectedEntity.EntityTypeId == (int)eRefDataType.Haulier)
                {
                    CurrentViewModel = SimpleIoc.Default.GetInstance<HaulierListViewModel>();
                }
                else if (SelectedEntity.EntityTypeId == (int)eRefDataType.Customer)
                {
                    var vm = SimpleIoc.Default.GetInstance<CustomerSupplierListViewModel>();
                    vm.EntityTypeId = (int)eRefDataType.Customer;
                    CurrentViewModel = vm;
                }
                else if (SelectedEntity.EntityTypeId == (int)eRefDataType.StockLocation)
                {
                    CurrentViewModel = SimpleIoc.Default.GetInstance<StockLocationListViewModel>();
                }
            }
        }


        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            var entityRefList = new List<EntityType>(await _refRepo.GetAllEntityTypes(true));

            if (AuthorisationHelper.CurrentUserHasRole(GlobalVariables.CurrentGlobeweighUser, eUserRole.OfficeAdmin))
            {
                EntityList = new List<EntityType>(entityRefList);
            }
            else
            {
                EntityList = new List<EntityType>(entityRefList.Where(a=>!a.SuperAdminAccessOnly));
            }
        }

        public void Unload(FrameworkElement element)
        {
            EntityList = null;
            SelectedEntity = null;
            CurrentViewModel = null;
        }

    }
}
