using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class SelectRolesViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IReferenceDataRepository _refDataRepo = SimpleIoc.Default.GetInstance<IReferenceDataRepository>();

        #endregion

        #region Properties

        private GlobeweighUser _CurrentUser;
        public GlobeweighUser CurrentUser
        {
            get { return _CurrentUser; }
            set { Set(ref _CurrentUser, value); }
        }

        private ReferenceData _SelectedRole;
        public ReferenceData SelectedRole
        {
            get { return _SelectedRole; }
            set { Set(ref _SelectedRole, value); }
        }

        private List<ReferenceData> _RoleList;
        public List<ReferenceData> RoleList
        {
            get { return _RoleList; }
            set { Set(ref _RoleList, value); }
        }


        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);

        #endregion

        private async Task RefreshRoleList()
        {
            var roleList = EnumHelper.GetReferenceDataListFromEnum(typeof(eUserRole));

            // var roleList = new List<ReferenceData>(await _refDataRepo.GetReferenceData(eUserRole));

            foreach (var alreadySelectedRole in CurrentUser.Globeweigh_Role)
            {
                var match = roleList.FirstOrDefault(a => a.id == alreadySelectedRole.RoleId);
                if (match != null)
                {
                    roleList.Remove(match);
                }
            }
            RoleList = new List<ReferenceData>(roleList);
        }

        public async void Load(FrameworkElement element)
        {
            await RefreshRoleList();
        }

        private async void OnSave()
        {
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (SelectedRole == null) return false;
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            SelectedRole = null;
            RoleList = null;
            DialogResult = null;
        }
    }
}
