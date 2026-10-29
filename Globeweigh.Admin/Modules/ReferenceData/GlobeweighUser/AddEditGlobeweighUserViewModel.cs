using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class AddEditGlobeweighUserViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IGlobeweighUserRepository _GlobeweighUserRepo =SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private GlobeweighEntities _context = new GlobeweighEntities(GlobalVariables.ConnectionString);

        #endregion

        #region Properties

        public int GlobeweighUserId { get; set; }

        private GlobeweighUser _CurrentGlobeweighUser;
        public GlobeweighUser CurrentGlobeweighUser
        {
            get { return _CurrentGlobeweighUser; }
            private set { Set(ref _CurrentGlobeweighUser, value); }
        }

        private Globeweigh_Role _SelectedRole;
        public Globeweigh_Role SelectedRole
        {
            get { return _SelectedRole; }
             set { Set(ref _SelectedRole, value); }
        }



        #endregion

        #region Commands

        public RelayCommand SaveCommand => new RelayCommand(OnSave, CanSave);
        public RelayCommand AddRoleCommand => new RelayCommand(OnAddRole);
        public RelayCommand DeleteRoleCommand => new RelayCommand(OnDeleteRole);

        #endregion

        private void OnAddRole()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<SelectRolesViewModel>();
            dialogViewModel.CurrentUser = CurrentGlobeweighUser;
            bool? success = _dialogService.ShowDialog<SelectRolesView>(this, dialogViewModel);
            if (success == true)
            {
                var newRole = new Globeweigh_Role();
                newRole.RoleId = dialogViewModel.SelectedRole.id;
                newRole.DateCreated = DateTime.Now;
                CurrentGlobeweighUser.Globeweigh_Role.Add(newRole);
                SelectedRole = null;
            }

        }

        private void OnDeleteRole()
        {
            if (SelectedRole == null) return;
            _context.Globeweigh_Role.Remove(SelectedRole);
            SelectedRole = null;
        }

        public async void Load(FrameworkElement element)
        {
            if (GlobeweighUserId == 0)
            {
                CurrentGlobeweighUser = new GlobeweighUser();
                CurrentGlobeweighUser.UserTypeId = (int)eUserType.Both;
            }
            else
            {
                CurrentGlobeweighUser = await _GlobeweighUserRepo.GetGlobeweighUserAsync(_context, GlobeweighUserId);
                if (CurrentGlobeweighUser.Globeweigh_Role == null)
                {
                    CurrentGlobeweighUser.Globeweigh_Role = new ObservableCollection<Globeweigh_Role>();
                }
            }
        }

        private async void OnSave()
        {
            await _GlobeweighUserRepo.AddUpdateGlobeweighUserAsync(_context, CurrentGlobeweighUser);
            DialogResult = true;
        }

        private bool CanSave()
        {
            if (CurrentGlobeweighUser == null) return false;
            if (string.IsNullOrEmpty(CurrentGlobeweighUser.FirstName)) return false;
            if (string.IsNullOrEmpty(CurrentGlobeweighUser.Surname)) return false;
            if (CurrentGlobeweighUser.UserTypeId != (int) eUserType.Touch)
            {
                if (string.IsNullOrEmpty(CurrentGlobeweighUser.Username)) return false;
                if (string.IsNullOrEmpty(CurrentGlobeweighUser.Password)) return false;
                if (CurrentGlobeweighUser.Globeweigh_Role == null) return false;
                if (!CurrentGlobeweighUser.Globeweigh_Role.Any()) return false;
            }
            else
            {
                if (string.IsNullOrEmpty(CurrentGlobeweighUser.Passcode)) return false;
            }


            if (!string.IsNullOrEmpty(CurrentGlobeweighUser.Passcode))
            {
                if (CurrentGlobeweighUser.Passcode.Length != 4) return false;
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
