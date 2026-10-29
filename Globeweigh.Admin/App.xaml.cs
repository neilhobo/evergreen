using DevExpress.Xpf.Core;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using MvvmDialogs;
using OfficeOpenXml;
using System.Threading.Tasks;
using System.Windows;
using DispatcherHelper = GalaSoft.MvvmLight.Threading.DispatcherHelper;
using OfficeOpenXml;

namespace Globeweigh.Admin
{
    public partial class App : Application
    {
        public App()
        {
            var palettetheme = Theme.CreateTheme(PredefinedThemePalettes.CobaltBlue, Theme.VS2017Light);
            Theme.RegisterTheme(palettetheme);
            ApplicationThemeHelper.ApplicationThemeName = palettetheme.Name;
        }

        protected async override void OnStartup(StartupEventArgs e)
        {
            ExcelPackage.License.SetNonCommercialPersonal("Globeweigh");


            SplashView splash = new SplashView();
            splash.Show();
             var databaseOk = await Task.Run(StartupMethod);
            if (!databaseOk)
            {
                var errorWindow = new DbErrorWindow();
                errorWindow.Show();
                splash.Close();
                return;
            }
            MainWindow main = new MainWindow();
            main.InitializeComponent();
            Application.Current.MainWindow = main;
            splash.Close();
            main.Show();

        }

        private async Task<bool> StartupMethod()
        {
            ExceptionHelper.Initialize();
            var success = Database.SetAndTestDbConnection();
            if (!success){return false;}
            SimpleIocRegistration();
            GlobalVariables.GlobalDbSettings = await SimpleIoc.Default.GetInstance<ISettingsRepository>().GetSettings();
            DispatcherHelper.Initialize();
            UtilitiesShared.RegisterDevice(SimpleIoc.Default.GetInstance<IDeviceRepository>(), false);

            var scaleService = SimpleIoc.Default.GetInstance<IScaleService>();
            await scaleService.EnsureConnectedAsync();
            return true;
        }

        private void SimpleIocRegistration()
        {
            SimpleIoc.Default.Register<LoginViewModel>();
            SimpleIoc.Default.Register<IDeviceRepository>(() => new DeviceRepository());
            SimpleIoc.Default.Register<IDialogService>(() => new MvvmDialogs.DialogService());
            SimpleIoc.Default.Register<IReferenceDataRepository>(() => new ReferenceDataRepository());
            SimpleIoc.Default.Register<IPalletRepository>(() => new PalletRepository());
            SimpleIoc.Default.Register<ICustomerSupplierRepository>(() => new CustomerSupplierRepository());
            SimpleIoc.Default.Register<IGlobeweighUserRepository>(() => new GlobeweighUserRepository());
            SimpleIoc.Default.Register<IStockLocationRepository>(() => new StockLocationRepository());
            SimpleIoc.Default.Register<IProductRepository>(() => new ProductRepository());
            SimpleIoc.Default.Register<IStockRepository>(() => new StockRepository());
            SimpleIoc.Default.Register<IDispatchOrderRepository>(() => new DispatchOrderRepository());
            SimpleIoc.Default.Register<ISettingsRepository>(() => new SettingsRepository());
            SimpleIoc.Default.Register<IReportRepository>(() => new ReportRepository());
            SimpleIoc.Default.Register<IHaulierRepository>(() => new HaulierRepository());
            SimpleIoc.Default.Register<IWeighbridgeRepository>(() => new WeighbridgeRepository());
            SimpleIoc.Default.Register<ICostingRepository>(() => new CostingRepository());
            SimpleIoc.Default.Register<ISageRepository>(() => new SageRepository());
            SimpleIoc.Default.Register<IScaleService, ScaleService>(true);
        }
    }


}
