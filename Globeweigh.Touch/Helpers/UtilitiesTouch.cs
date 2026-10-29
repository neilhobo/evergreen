using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MahApps.Metro.Controls;
using MvvmDialogs;

namespace Globeweigh.Touch.Helpers
{
    public static class UtilitiesTouch
    {

        private static readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private static readonly IGlobeweighUserRepository _globeUserRepo = SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();

        public static bool ShowOkCancelMessageView(IViewModel parentVM, string message, bool hideCancel)
        {
            var dialogVm = SimpleIoc.Default.GetInstance<OkCancelViewModel>();
            dialogVm.MessageText = message;
            dialogVm.HideCancel = hideCancel;
            var success = _dialogService.ShowDialog<OkCancelView>(parentVM, dialogVm);
            return success != null && (bool) success;
            }


        public static async Task<bool> ShowLoginView(IViewModel parentVM)
        {
            var users = new List<GlobeweighUser>(await _globeUserRepo.GetGlobeweighUsersAsync(eUserRole.Factory));
            var dialogViewModel = SimpleIoc.Default.GetInstance<SelectRefDataViewModel>();
            dialogViewModel.HeaderText = "Login (Select User)";
            var userList = (from t in users select new ReferenceData() { id = t.id, DisplayName = t.FirstName + " " + t.Surname }).ToList();
            dialogViewModel.GenericRefDataList = new List<ReferenceData>(userList);
            var success = _dialogService.ShowDialog<SelectRefDataView>(parentVM, dialogViewModel);
            if ((bool)success)
            {
                GlobalVariables.CurrentGlobeweighUser = await _globeUserRepo.GetGlobeweighUserAsync(dialogViewModel.SelectedRefData.id);
                return true;
            }
            return false;
        }


    }
}
