using System.Collections.Generic;
using System.Collections.ObjectModel;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using Globeweigh.Model;
using MvvmDialogs;
using System.Windows;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.CommandWpf;
using System.Linq;
using System.Windows.Controls;

namespace Globeweigh.Touch
{
    public class SelectRefDataViewModel : BindableBase, IModalDialogViewModel, IViewModel
    {
        private readonly IReferenceDataRepository _refRepo;

        #region private fields

        #endregion

        #region Properties
        public int EntityTypeId { get; set; }

        private ObservableCollection<GridColumn> _VisibleColumnList;
        public ObservableCollection<GridColumn> VisibleColumnList
        {
            get { return _VisibleColumnList; }
            set { Set(ref _VisibleColumnList, value); }
        }

        private List<ReferenceData> _GenericRefDataList;
        public List<ReferenceData> GenericRefDataList
        {
            get { return _GenericRefDataList; }
            set { Set(ref _GenericRefDataList, value); }
        }

        private ReferenceData _SelectedRefData;
        public ReferenceData SelectedRefData
        {
            get { return _SelectedRefData; }
            set { Set(ref _SelectedRefData, value); }
        }

        private string _HeaderText;
        public string HeaderText
        {
            get { return _HeaderText; }
            set { Set(ref _HeaderText, value); }
        }

        private bool? _DialogResult;

        public bool? DialogResult
        {
            get { return _DialogResult; }
            private set { Set(ref _DialogResult, value); }
        }

        private RefDataGridTypeEnum? _GridTemplateContent;
        public RefDataGridTypeEnum? GridTemplateContent
        {
            get { return _GridTemplateContent; }
            set { Set(ref _GridTemplateContent, value); }
        }

        #endregion

        #region Commands

        public RelayCommand SelectCommand { get; private set; }
        public RelayCommand CancelCommand { get; private set; }

        #endregion

        public SelectRefDataViewModel(IReferenceDataRepository refRepo)
        {
            _refRepo = refRepo;
            SelectCommand = new RelayCommand(OnSelect, CanSelect);
            CancelCommand = new RelayCommand(OnCancel);
        }

        public async void Load(FrameworkElement element)
        {
            GridTemplateContent = RefDataGridTypeEnum.GridControlTemplate;
            if (EntityTypeId == 0)
            {
                VisibleColumnList = new ObservableCollection<GridColumn> { new GridColumn { FieldName = "DisplayName", Header = "" } };
            }
            else if (EntityTypeId == (int)eRefDataType.Product)
            {
                VisibleColumnList = new ObservableCollection<GridColumn>
                {
                    new GridColumn { FieldName = "Code", Header = "Code" },
                    new GridColumn { FieldName = "DisplayName", Header = "Description" }
                };
            }
            else
            {
                VisibleColumnList = new ObservableCollection<GridColumn>(await _refRepo.GetVisibleColumnList(EntityTypeId));

            }
//            if (EntityTypeId == (int)eRefDataType.CrateTare) GenericRefDataList = new List<ReferenceData>(await _refRepo.GetReferenceData(eRefDataType.CrateTare)).OrderBy(a => a.DecimalData).ToList(); ;
            SelectedRefData = null;
        }
        private async void OnSelect()
        {
            DialogResult = true;
        }

        private bool CanSelect()
        {
            if (SelectedRefData == null) return false;
            return true;
        }

        private void OnCancel()
        {
            DialogResult = false;
        }

        public void Unload(FrameworkElement element)
        {
            //            var vm = (SelectRefDataView)element;
            //            vm.RefDataParentGrid.Children.Remove(vm.grdRefData);
            //            vm.RefDataParentGrid.Children.RemoveAt(0);

            EntityTypeId = 0;
            GenericRefDataList = null;
            SelectedRefData = null;
            DialogResult = null;
            VisibleColumnList = new ObservableCollection<GridColumn>();
        }
    }
    public enum RefDataGridTypeEnum { GridControlTemplate, ListBoxTemplate };
    public class RefDataGridTypeSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var pm = item as RefDataGridTypeEnum?;
            if (pm == null) return null;
            var w = ((FrameworkElement)container);
            var v = pm.Value;
            switch (v)
            {
                case RefDataGridTypeEnum.GridControlTemplate: return w.TryFindResource("GridControlTemplate") as DataTemplate;
                case RefDataGridTypeEnum.ListBoxTemplate: return w.TryFindResource("ListBoxTemplate") as DataTemplate;
            }
            return null;
        }
    }
}
