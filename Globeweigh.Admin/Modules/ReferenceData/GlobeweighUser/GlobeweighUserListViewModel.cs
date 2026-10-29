using System;
using System.Collections.Generic;
using Globeweigh.Shared;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using Globeweigh.Model;
using GalaSoft.MvvmLight.Ioc;
using IDialogService = MvvmDialogs.IDialogService;
using System.Threading.Tasks;
using System.Windows.Controls;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;

namespace Globeweigh.Admin
{
    public class GlobeweighUserListViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IGlobeweighUserRepository _globeUserRepo = SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();

        #endregion

        #region Properties

        private List<GlobeweighUser> _GlobeweighUserList;
        public List<GlobeweighUser> GlobeweighUserList
        {
            get { return _GlobeweighUserList; }
            set { Set(ref _GlobeweighUserList, value); }
        }

        private GlobeweighUser _SelectedGlobeweighUser;
        public GlobeweighUser SelectedGlobeweighUser
        {
            get { return _SelectedGlobeweighUser; }
            set { Set(ref _SelectedGlobeweighUser, value); }
        }

        #endregion

        #region Commands

        public RelayCommand AddCommand => new RelayCommand(OnAdd);
        public RelayCommand EditCommand => new RelayCommand(OnEdit);
        public RelayCommand DeleteCommand => new RelayCommand(OnDelete);

        #endregion

        private void OnAdd()
        {
            AddEditUserDialog(0);
        }

        private void OnEdit()
        {
            AddEditUserDialog(SelectedGlobeweighUser.id);
        }

        private async void OnDelete()
        {
            var result = _dialogService.ShowMessageBox(this, "Delete User?", "", MessageBoxButton.OKCancel);
            if (result == MessageBoxResult.Cancel) return;
            await _globeUserRepo.MarkAsDeletedAsync(SelectedGlobeweighUser.id);
            await RefreshList();
            SelectedGlobeweighUser = null;
        }

        private async void AddEditUserDialog(int GlobeweighUserId)
        {
            var dialogViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<AddEditGlobeweighUserViewModel>();
            dialogViewModel.GlobeweighUserId = GlobeweighUserId;
            if (GlobeweighUserId == 0) dialogViewModel.HeaderText = "Add New User";
            else dialogViewModel.HeaderText = "Edit User";
            bool? success = _dialogService.ShowDialog<AddEditGlobeweighUserView>(this, dialogViewModel);
            if (success == true)
            {
                SelectedGlobeweighUser = null;
                await RefreshList();
            }
        }

        private async Task RefreshList()
        {
            GlobeweighUserList = new List<GlobeweighUser>(await _globeUserRepo.GetGlobeweighUsersAsync(true));
        }


        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.InDesignMode) return;
            await RefreshList();
        }

        public void Unload(FrameworkElement element)
        {
            GlobeweighUserList = null;
            SelectedGlobeweighUser = null;
        }

    }
}
