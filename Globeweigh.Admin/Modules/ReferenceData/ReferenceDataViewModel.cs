using System;
using System.Collections.Generic;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using IDialogService = MvvmDialogs.IDialogService;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Xpf.Editors.Settings;
using DevExpress.Xpf.Grid;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class ReferenceDataViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IReferenceDataRepository _refRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        public int RefDataTypeId { get; set; }

        private string _HeaderText;
        public string HeaderText
        {
            get { return _HeaderText; }
            set { Set(ref _HeaderText, value); }
        }

        private EntityType _SelectedEntity;
        public EntityType SelectedEntity
        {
            get { return _SelectedEntity; }
            set
            {
                Set(ref _SelectedEntity, value);
            }
        }
        
        private ObservableCollection<GridColumn> _VisibleColumnList;
        public ObservableCollection<GridColumn> VisibleColumnList
        {
            get { return _VisibleColumnList; }
            set { Set(ref _VisibleColumnList, value); }
        }

        private List<object> _GenericRefDataList;
        public List<object> GenericRefDataList
        {
            get { return _GenericRefDataList; }
            set { Set(ref _GenericRefDataList, value); }
        }

        private List<ReferenceData> _ForeignRefDataList;
        public List<ReferenceData> ForeignRefDataList
        {
            get { return _ForeignRefDataList; }
            set { Set(ref _ForeignRefDataList, value); }
        }

        private object _SelectedRefData;
        public object SelectedRefData
        {
            get { return _SelectedRefData; }
            set
            {
                Set(ref _SelectedRefData, value);
                if (_SelectedRefData != null) ;
            }
        }

        private string _DisplayName;
        public string DisplayName
        {
            get { return _DisplayName; }
            set { Set(ref _DisplayName, value); }
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


        #endregion

        #region Commands

        public RelayCommand AddNewCommand => new RelayCommand(OnAdd);
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);

        #endregion

        private void OnAdd()
        {
            AddEditNewRefDataDialog(0, SelectedEntity.EntityTypeId, SelectedEntity.IsReferenceDataType);
        }

        private void OnEdit()
        {
            int refDataId = 0;
            if (SelectedEntity.IsReferenceDataType)
                refDataId =((ReferenceData)SelectedRefData).id;
//            else if(SelectedEntity.EntityTypeId == (int)eRefDataType.Product)
//                refDataId = ((vwProduct)SelectedRefData).id;

            if(refDataId == 0)return;

            AddEditNewRefDataDialog(refDataId, SelectedEntity.EntityTypeId, SelectedEntity.IsReferenceDataType);

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


            await _refRepo.MarkRefDataAsDeletedAsync(SelectedRefData);
            await RefreshList();
            SelectedRefData = null;


        }

        private async void AddEditNewRefDataDialog(int refDataId, int refDataTypeId, bool isReferenceDataType)
        {
//            if (SelectedEntity.EntityTypeId == (int)eRefDataType.ProductType)
//            {
//                var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditProductTypeViewModel>();
//                dialogViewModel.RefDataId = refDataId;
//                dialogViewModel.SelectedEntity = SelectedEntity;
//                bool? success = _dialogService.ShowDialog<AddEditProductTypeView>(this, dialogViewModel);
//                if (success == true)
//                {
//                    SelectedRefData = null;
//                    await RefreshList();
//                }
//            }
//
//            else
            {
                var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditRefDataViewModel>();
                dialogViewModel.RefDataId = refDataId;
                dialogViewModel.SelectedEntity = SelectedEntity;
                bool? success = _dialogService.ShowDialog<AddEditRefDataView>(this, dialogViewModel);
                if (success == true)
                {
                    SelectedRefData = null;
                    await RefreshList();
                }
            }
        }

        public async Task RefreshList()
        {
            try
            {
                if (SelectedEntity == null) return;
                VisibleColumnList = new ObservableCollection<GridColumn>(await _refRepo.GetVisibleColumnList(SelectedEntity.EntityTypeId));
                GenericRefDataList = new List<object>(await _refRepo.GetGenericReferenceData(SelectedEntity.EntityTypeId, SelectedEntity.IsReferenceDataType, ShowDeletes));
                HeaderText = SelectedEntity.DisplayName;

                if (ShowDeletes)
                {
                    TextEditSettings textEditSettings = new TextEditSettings { HorizontalContentAlignment = EditSettingsHorizontalAlignment.Left };
                    var deleteColumn = new GridColumn() { FieldName = "DateDeleted", Header = "Date Deleted", EditSettings = textEditSettings };
                    VisibleColumnList.Add(deleteColumn);
                }

                //Add Empty Column
                var emptyColumn = new GridColumn() { Header = " "};
                VisibleColumnList.Add(emptyColumn);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

        }


        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            if(SelectedEntity == null)return;
            await RefreshList();
        }

        public void Unload(FrameworkElement element)
        {
            var vm = (ReferenceDataView) element;
            vm.RefDataParentGrid.Children.Remove(vm.grdRefData);
            VisibleColumnList = new ObservableCollection<GridColumn>();
            GenericRefDataList = null;
            SelectedRefData = null;
            SelectedEntity = null;
            HeaderText = null;
            ForeignRefDataList = null;
        }

    }
}
