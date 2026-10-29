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
    public class AddEditGlobeweighUserViewModelOld : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IGlobeweighUserRepository _GlobeweighUserRepo =SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();
        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();

        #endregion

        #region Properties

        public int GlobeweighUserId { get; set; }

        private GlobeweighUser _CurrentGlobeweighUser;
        public GlobeweighUser CurrentGlobeweighUser
        {
            get { return _CurrentGlobeweighUser; }
            private set { Set(ref _CurrentGlobeweighUser, value); }
        }

//        private List<ReferenceData> _DeliveryRouteList;
//        public List<ReferenceData> DeliveryRouteList
//        {
//            get { return _DeliveryRouteList; }
//            private set { Set(ref _DeliveryRouteList, value); }
//        }

        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        public async void Load(FrameworkElement element)
        {
            if (GlobeweighUserId == 0)
            {
                CurrentGlobeweighUser = new GlobeweighUser();
            }
            else
            {
                CurrentGlobeweighUser = await _GlobeweighUserRepo.GetGlobeweighUserAsync(GlobeweighUserId);
            }
        }

        private async void OnSave()
        {
            await _GlobeweighUserRepo.AddUpdateGlobeweighUserAsync(CurrentGlobeweighUser);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentGlobeweighUser == null) return false;
            if (string.IsNullOrEmpty(CurrentGlobeweighUser.FirstName)) return false;
            if (string.IsNullOrEmpty(CurrentGlobeweighUser.Surname)) return false;  
            if (CurrentGlobeweighUser.RoleId == 0) return false;

            if (CurrentGlobeweighUser.RoleId != (int) eUserRole.Factory)
            {
                if (string.IsNullOrEmpty(CurrentGlobeweighUser.Username)) return false;
            }



            return true;
        }

        public void Unload(FrameworkElement element)
        {
            CurrentGlobeweighUser = null;
            GlobeweighUserId = 0;
            DialogResult = null;
        }
    }
}
