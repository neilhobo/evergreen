using System;
using System.Collections.Generic;
using System.Linq;
using Globeweigh.Shared;
using Globeweigh.UI.Touch.Model;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using System.Windows.Controls;
using System.Windows.Data;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class AddEditRefDataViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IReferenceDataRepository _refDataRepo =SimpleIoc.Default.GetInstance<IReferenceDataRepository>();
        private List<EntityTypeField> _fieldList; 

        #endregion

        #region Properties

        public int RefDataId { get; set; }
        public EntityType SelectedEntity { get; set; }

        private ReferenceData _CurrentRefData;
        public ReferenceData CurrentRefData
        {
            get { return _CurrentRefData; }
            private set { Set(ref _CurrentRefData, value); }
        }

        private List<object> _ProductList;
        public List<object> ProductList
        {
            get { return _ProductList; }
            set { Set(ref _ProductList, value); }
        }

        private List<GridColumn> _IsUsedColumnList;
        public List<GridColumn> IsUsedColumnList
        {
            get { return _IsUsedColumnList; }
            set { Set(ref _IsUsedColumnList, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            HideShowControls(element);
            _fieldList = new List<EntityTypeField>(await _refDataRepo.GetRequiredEntityTypeFields(SelectedEntity));
            if (RefDataId == 0)
            {
                CurrentRefData = new ReferenceData();
                CurrentRefData.RefTypeId = SelectedEntity.EntityTypeId;
                HeaderText = "Add New Record";
            }
            else
            {
                CurrentRefData = await _refDataRepo.GetReferenceDataById(RefDataId);
                HeaderText = "Edit Record";
            }
        }

        private async void HideShowControls(FrameworkElement element)
        {
            var isUsedFieldList = await _refDataRepo.GetIsUsedColumnList(SelectedEntity);
            var view = (AddEditRefDataView)element;
            foreach (var child in view.ControlsGrid.Children)
            {
                if (child is FrameworkElement)
                {
                    var control = ((FrameworkElement)child);
                    var match = isUsedFieldList.FirstOrDefault(a => a.FieldName == control.Name);
                    if (match == null)
                    {
                        if (control.Name == "")continue;
                        control.Visibility = Visibility.Collapsed;
                        continue;
                    }
//                    else
//                    {
//                        if (control.Name == "AssociatedProductId") ProductList = new List<object>(await _refDataRepo.GetGenericReferenceData((int)eRefDataType.Product, false));
//                    }
                    if (control is Label)
                    {
                        var textBlock = (Label)control;
                        if(match.Header !=null)textBlock.Content = match.Header.ToString();
                    }
                }
            }
        }

        private async void OnSave()
        {
            await _refDataRepo.AddUpdateRefDataAsync(CurrentRefData);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentRefData == null) return false;

//            if (CurrentRefData.RefTypeId == (int)eRefDataType.PalletTare || CurrentRefData.RefTypeId == (int)eRefDataType.CrateTare)
//            {
//                if (!UtilitiesShared.IsNumeric(CurrentRefData.DisplayName)) return false;
//                CurrentRefData.DecimalData = Convert.ToDecimal(CurrentRefData.DisplayName.Trim());
//            }


            if (_fieldList.FirstOrDefault(a => a.FieldName == "Code" && a.IsRequired) != null)
                if (string.IsNullOrEmpty(CurrentRefData.Code)) return false;
            if (_fieldList.FirstOrDefault(a => a.FieldName == "DisplayName" && a.IsRequired) != null)
                if (string.IsNullOrEmpty(CurrentRefData.DisplayName)) return false;

            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentRefData = null;
            RefDataId = 0;
            _fieldList = null;
            DialogResult = null;
        }
    }
}
