using System.Collections.Generic;
using System.Linq;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using System.Windows.Controls;
using DevExpress.Xpf.Grid;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;

namespace Globeweigh.Admin
{
    public class AddEditProductContitionTypeViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();

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

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
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


        private async void OnSave()
        {
            await _refDataRepo.AddUpdateRefDataAsync(CurrentRefData);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentRefData == null) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentRefData = null;
            RefDataId = 0;
            DialogResult = null;
        }
    }
}
