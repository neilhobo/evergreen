using System;
using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using System.Reflection;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Threading;
using Globeweigh.Admin.Helpers;
using Globeweigh.Admin.Services;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Services;
using IDialogService = MvvmDialogs.IDialogService;
using NLog;
using SageDataObject280;

namespace Globeweigh.Admin
{
    public class MainWindowViewModel : AdminViewModelBase, IViewModel
    {
        #region private fields

        private readonly IGlobeweighUserRepository _GlobeweighUserRepo = SimpleIoc.Default.GetInstance<IGlobeweighUserRepository>();
        private readonly ISageRepository sageRepo = SimpleIoc.Default.GetInstance<ISageRepository>();
        private string _newVersion;
        private readonly IDialogService _dialogService = SimpleIoc.Default.GetInstance<IDialogService>();
        private System.Windows.Threading.DispatcherTimer _dispatcherTimer;

        private static readonly Logger _logger = LogManager.GetLogger("App");

        #endregion

        #region Properties

        public string NewReleaseFullFileName { get; set; }

        private bool _InTestMode;
        public bool InTestMode
        {
            get { return _InTestMode; }
            set { Set(ref _InTestMode, value); }
        }

        private object _CurrentViewModel;
        public object CurrentViewModel
        {
            get { return _CurrentViewModel; }
            set { Set(ref _CurrentViewModel, value); }
        }

        private string _MainHeader;
        public string MainHeader
        {
            get { return _MainHeader; }
            set { Set(ref _MainHeader, value); }
        }

        // private string _LoggedInUserDisplay;
        // public string LoggedInUserDisplay
        // {
        //     get { return _LoggedInUserDisplay; }
        //     set { Set(ref _LoggedInUserDisplay, value); }
        // }

        private string _CurrentVersion;
        public string CurrentVersion
        {
            get { return _CurrentVersion; }
            set { Set(ref _CurrentVersion, value); }
        }

        private bool _IsGlobeweighAdmin;
        public bool IsGlobeweighAdmin
        {
            get { return _IsGlobeweighAdmin; }
            set { Set(ref _IsGlobeweighAdmin, value); }
        }

        private bool _NewVersionAvailable;
        public bool NewVersionAvailable
        {
            get { return _NewVersionAvailable; }
            set { Set(ref _NewVersionAvailable, value); }
        }

        private string _NewVersionAvailableMessage;
        public string NewVersionAvailableMessage
        {
            get { return _NewVersionAvailableMessage; }
            set { Set(ref _NewVersionAvailableMessage, value); }
        }

        private bool _ProductsVisible;
        public bool ProductsVisible
        {
            get { return _ProductsVisible; }
            set { Set(ref _ProductsVisible, value); }
        }

        private bool _CostingVisible;
        public bool CostingVisible
        {
            get { return _CostingVisible; }
            set { Set(ref _CostingVisible, value); }
        }

        private bool _ReferenceDataVisible;
        public bool ReferenceDataVisible
        {
            get { return _ReferenceDataVisible; }
            set { Set(ref _ReferenceDataVisible, value); }
        }

        private GlobeweighUser _CurrentGlobeweighUser;
        public GlobeweighUser CurrentGlobeweighUser
        {
            get { return _CurrentGlobeweighUser; }
            set { Set(ref _CurrentGlobeweighUser, value); }
        }

        private ListBoxItem _SelectedNavItem;
        public ListBoxItem SelectedNavItem
        {
            get { return _SelectedNavItem; }
            set
            {
                Set(ref _SelectedNavItem, null);
                Set(ref _SelectedNavItem, value);
                SetCurrentView();
            }
        }

        #endregion

        #region Commands

        public RelayCommand DownloadNewVersionCommand => new RelayCommand(OnDownloadNewVersion);
        public RelayCommand MyAccountCommand => new RelayCommand(OnMyAccountCommand);
        public RelayCommand ShowAboutCommand => new RelayCommand(OnShowAboutCommand);

        #endregion

        private async void OnShowAboutCommand()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstance<AboutViewModel>();
            bool? success = _dialogService.ShowDialog<AboutView>(this, dialogViewModel);
        }

        private async void OnMyAccountCommand()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstance<MyAccountViewModel>();
            bool? success = _dialogService.ShowDialog<MyAccountView>(this, dialogViewModel);
            if (success == true)
            {
                GlobalVariables.CurrentGlobeweighUser = await _GlobeweighUserRepo.GetGlobeweighUserAsync(CurrentGlobeweighUser.id);
                // CurrentGlobeweighUser = GlobalVariables.CurrentGlobeweighUser;
            }
        }

        private void SetCurrentView()
        {
            switch (SelectedNavItem.Name)
            {
                case "Production":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<ProductionMasterViewModel>();
                    MainHeader = "Production";
                    break;
                case "Stock":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<FinishedProductStockMasterViewModel>();
                    MainHeader = "Stock";
                    break;
                case "CompletedOrders":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<CompletedOrderListViewModel>();
                    MainHeader = "Completed Orders";
                    break;
                case "Products":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<ProductsMasterViewModel>();
                    MainHeader = "Products";
                    break;
                case "GateBarrier":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<GateBarrierViewModel>();
                    MainHeader = "Hauliers";
                    break;
                case "GateBarrierWeb":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<GateBarrierWebViewModel>();
                    MainHeader = "Gate/Barrier (Web)";
                    break;
                case "Hauliers":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<HaulierListViewModel>();
                    MainHeader = "Hauliers";
                    break;
                case "Weighbridge":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<WeighbridgeRecordsViewModel>();
                    MainHeader = "Weighbridge";
                    break;
                case "Costing":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<CostingPriceListViewModel>();
                    MainHeader = "Weighbridge";
                    break;
                case "Admin":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<ReferenceDataMasterViewModel>();
                    MainHeader = "Admin";
                    break;
                case "CustomersSuppliers":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<CustomerSupplierListViewModel>();
                    MainHeader = "Customers";
                    break;
                case "Dispatch":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<DispatchOrderListViewModel>();
                    MainHeader = "Dispatch";
                    break;
                case "Reports":
                    CurrentViewModel = SimpleIoc.Default.GetInstance<ReportsMasterViewModel>();
                    MainHeader = "Reports";
                    break;
                case "devices":
                    CurrentViewModel = null;
                    MainHeader = "Devices";
                    break;
                case "entities":
                    CurrentViewModel = SimpleIoc.Default.GetInstanceWithoutCaching<EntitiesViewModel>();
                    MainHeader = "Entities";
                    break;
                default:
                    CurrentViewModel = null;
                    break;
            }
        }

        public async void Load(FrameworkElement element)
        {
            // await sageRepo.ConnectToSage();
            // await ExcelProductImporter.MatchProducts();


            var dialogViewModel = SimpleIoc.Default.GetInstance<LoginViewModel>();
            bool? success = _dialogService.ShowDialog<LoginView>(this, dialogViewModel);
            if (success == true)
            {
            }
            else
            {
                App.Current.Shutdown();
                return;
            }

            CurrentGlobeweighUser = GlobalVariables.CurrentGlobeweighUser;
            if (AuthorisationHelper.CurrentUserHasRole(GlobalVariables.CurrentGlobeweighUser, eUserRole.OfficeAdmin, eUserRole.GlobeweighAdmin))
            {
                CostingVisible = true;
            }

            if (UtilitiesShared.IsMyMachine)
            {
                IsGlobeweighAdmin = true;
                CurrentVersion = "1.0.62";
                return;
            }
            InTestMode = GlobalVariables.InTestMode;
            _dispatcherTimer = new System.Windows.Threading.DispatcherTimer();
            _dispatcherTimer.Tick += dispatcherTimer_Tick;
            _dispatcherTimer.Interval = new TimeSpan(0, 1, 0);
            _dispatcherTimer.Start();
            await Task.Run(() => OnTick());
        }

        private async void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            await Task.Run(() => OnTick());
        }

        private async void OnTick()
        {
            try
            {
                CurrentVersion = UtilitiesShared.GetCurrentMsiVersion();
                if (UtilitiesShared.IsMyMachine) return;
                NewReleaseFullFileName = UtilitiesShared.CheckforUpdate(CurrentVersion, "AdminSetup", out _newVersion);
                if (NewReleaseFullFileName != null)
                {
                    NewVersionAvailable = true;
                    NewVersionAvailableMessage = "New update available (" + _newVersion + ")";
                    OnPropertyChanged("NewVersionAvailable");
                    DispatcherHelper.CheckBeginInvokeOnUI(() => OnPropertyChanged("NewVersionAvailable"));
                }
                else
                {
                    NewVersionAvailable = false;
                    _newVersion = null;
                }
            }
            catch (Exception e)
            {
                ExceptionHelper.LogError(e, "Admin MainWindowViewModel OnTick");
            }
        }

        private async void OnDownloadNewVersion()
        {
            var dialogViewModel = SimpleIoc.Default.GetInstance<DownloadNewUpdateViewModel>();
            dialogViewModel.NewReleaseFullFileName = NewReleaseFullFileName;
            bool? success = _dialogService.ShowDialog<DownloadNewUpdateView>(this, dialogViewModel);
            if (success == true)
            {
            }
        }

        public void Unload(FrameworkElement element)
        {
            _dispatcherTimer.Stop();
            _dispatcherTimer.Tick -= dispatcherTimer_Tick;
            _dispatcherTimer = null;
        }
    }
}