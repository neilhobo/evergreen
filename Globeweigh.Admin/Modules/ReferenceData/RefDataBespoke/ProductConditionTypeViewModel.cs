using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using Globeweigh.UI.Touch.Model;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;
using System.Windows.Controls;
using System.Windows.Data;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.Ioc;

namespace Globeweigh.Admin
{
    public class ProductConditionTypeViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
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

        private List<object> _GenericRefDataList;
        public List<object> GenericRefDataList
        {
            get { return _GenericRefDataList; }
            set { Set(ref _GenericRefDataList, value); }
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
                refDataId = ((ReferenceData)SelectedRefData).id;

            if (refDataId == 0) return;

            AddEditNewRefDataDialog(refDataId, SelectedEntity.EntityTypeId, SelectedEntity.IsReferenceDataType);

        }
        private async void OnDelete()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete Item?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _refRepo.MarkRefDataAsDeletedAsync(SelectedRefData);
            await RefreshList();
            SelectedRefData = null;
        }

        private async void AddEditNewRefDataDialog(int refDataId, int refDataTypeId, bool isReferenceDataType)
        {
            if (isReferenceDataType)
            {
                var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditProductContitionTypeViewModel>();
                dialogViewModel.RefDataId = refDataId;
                dialogViewModel.SelectedEntity = SelectedEntity;
                bool? success = _dialogService.ShowDialog<AddEditProductContitionTypeView>(this, dialogViewModel);
                if (success == true)
                {
                    SelectedRefData = null;
                    await RefreshList();
                }
            }
        }

        public async Task RefreshList()
        {
            if (SelectedEntity == null) return;
            GenericRefDataList = new List<object>(await _refRepo.GetGenericReferenceData(SelectedEntity.EntityTypeId, SelectedEntity.IsReferenceDataType, false));
            HeaderText = SelectedEntity.DisplayName;
        }


        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            if (SelectedEntity == null) return;
            await RefreshList();
        }

        public void Unload(FrameworkElement element)
        {
            GenericRefDataList = null;
            SelectedRefData = null;
            SelectedEntity = null;
            HeaderText = null;
        }
    }
}
