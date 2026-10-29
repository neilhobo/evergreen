using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using System.Windows;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Admin.Properties;
using Globeweigh.Model;
using Globeweigh.Shared.Helpers;


namespace Globeweigh.Admin
{
    public class LoginViewModel : DialogViewModelBase, IModalDialogViewModel, IViewModel
    {
        #region private fields

        private readonly IGlobeweighUserRepository _globeweighUserRepository = SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();

        #endregion

        #region Properties


        private string _Email;
        public string Email
        {
            get { return _Email; }
            set { Set(ref _Email, value); }
        }

        private string _Password;
        public string Password
        {
            get { return _Password; }
            set { Set(ref _Password, value); }
        }

        private bool _SaveDetailsChecked;
        public bool SaveDetailsChecked
        {
            get { return _SaveDetailsChecked; }
            set { Set(ref _SaveDetailsChecked, value); }
        }



        #endregion

        #region Commands

        public RelayCommand LoginCommand => new RelayCommand(OnLogin, CanLogin);

        #endregion

        public async void Load(FrameworkElement element)
        {
            if (UtilitiesShared.IsMyMachine)
            {
                GlobalVariables.CurrentGlobeweighUser = await _globeweighUserRepository.GetGlobeweighUserAsync("neil.hobson@globeweigh.com", "monday25");
                DialogResult = true;
                return;
            }

            Microsoft.Win32.RegistryKey exampleRegistryKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Globeweigh");
            if (exampleRegistryKey == null) return;
            var saveDetailsbool = exampleRegistryKey.GetValue("SaveDetailsChecked");

            if(saveDetailsbool == null)return;
            if (saveDetailsbool.ToString() == "True")
            {
                Email = exampleRegistryKey.GetValue("Email").ToString();
                Password = exampleRegistryKey.GetValue("Password").ToString();
                SaveDetailsChecked = true;
            }
        }

        private async void OnLogin()
        {
            GlobeweighUser client = await _globeweighUserRepository.GetGlobeweighUserAsync(Email, Password);
            if (client == null)
            {
                MessageBox.Show("Invalid details entered");
                return;
            }

            GlobalVariables.CurrentGlobeweighUser = client;

            if (SaveDetailsChecked)
            {
                Microsoft.Win32.RegistryKey exampleRegistryKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Globeweigh");
                if (exampleRegistryKey != null)
                {
                    exampleRegistryKey.SetValue("Email", Email);
                    if (Password == null)
                    {
                        exampleRegistryKey.SetValue("Password", string.Empty);
                    }
                    else
                    {
                        exampleRegistryKey.SetValue("Password", Password);
                    }

                    exampleRegistryKey.SetValue("SaveDetailsChecked", SaveDetailsChecked.ToString());
                    exampleRegistryKey.Close();
                }

            }

            DialogResult = true;
        }

        private bool CanLogin()
        {
            return true;
        }

        public void Unload(FrameworkElement element)
        {
            DialogResult = null;
        }
    }
}
