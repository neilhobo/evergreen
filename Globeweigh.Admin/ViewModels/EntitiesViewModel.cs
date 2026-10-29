using System.Collections.Generic;
using System.Collections.ObjectModel;
using Globeweigh.Shared.Services;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Shared.Helpers;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;

namespace Globeweigh.Admin
{
    public class EntitiesViewModel : AdminViewModelBase, IViewModel
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
                RefreshFieldList();
            }
        }

        private List<EntityType> _EntityList;
        public List<EntityType> EntityList
        {
            get { return _EntityList; }
            set { Set(ref _EntityList, value); }
        }

        private ObservableCollection<EntityTypeField> _EntityFieldList;
        public ObservableCollection<EntityTypeField> EntityFieldList
        {
            get { return _EntityFieldList; }
            set { Set(ref _EntityFieldList, value); }
        }

        private EntityTypeField _SelectedEntityField;
        public EntityTypeField SelectedEntityField
        {
            get { return _SelectedEntityField; }
            set
            {
                Set(ref _SelectedEntityField, value);

            }
        }

        #endregion

        #region Commands

        public RelayCommand AddNewCommand => new RelayCommand(OnAdd);
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand<object> RowUpdatedCommand => new RelayCommand<object>(OnRowUpdated);

        #endregion

        private async void OnRowUpdated(object parameter)
        {
            await _refRepo.UpdateEntityTypeFieldAsync((EntityTypeField)parameter);
        }

        private void OnAdd()
        {
        }
        private void OnEdit()
        {

        }

        private async void RefreshEntityList()
        {
            EntityList = new List<EntityType>(await _refRepo.GetAllEntityTypes(true));
        }

        private async void RefreshFieldList()
        {
            if (SelectedEntity == null) return;
            EntityFieldList = new ObservableCollection<EntityTypeField>(await _refRepo.GetAndSyncEntityTypeFields(SelectedEntity));
        }


        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            RefreshEntityList();
        }

        public void Unload(FrameworkElement element)
        {
        }
    }
}
