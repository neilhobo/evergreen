using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using DevExpress.Xpf.Core;
using GalaSoft.MvvmLight.Ioc;
using MvvmDialogs;
using Globeweigh.Model;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using Globeweigh.Shared;
using NLog;
using DialogService = MvvmDialogs.DialogService;
using DispatcherHelper = GalaSoft.MvvmLight.Threading.DispatcherHelper;

namespace Globeweigh.Touch
{
    public partial class App : Application
    {
        private static readonly Logger _logger = LogManager.GetLogger("App");

        public App()
        {
            try
            {
                var palettetheme = Theme.CreateTheme(PredefinedThemePalettes.CobaltBlue, Theme.VS2017Light);
                Theme.RegisterTheme(palettetheme);
                ApplicationThemeHelper.ApplicationThemeName = palettetheme.Name;

                // DevExpress.Xpf.Core.TouchSettings.IsTouchEnabled = true;
            }
            catch (Exception exc)
            {


                if (exc.Message != null) _logger.ErrorException(exc.Message, exc);
                if (exc.InnerException != null) _logger.ErrorException(exc.InnerException.ToString(), exc);
            }

        }

        protected async override void OnStartup(StartupEventArgs e)
        {
            try
            {
                if (UtilitiesShared.CheckForOtherInstanceOfApplication()) return;
                ThemeManager.Instance.Initialize();
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
            catch (Exception exc)
            {


                if (exc.Message != null) _logger.ErrorException(exc.Message, exc);
                if (exc.InnerException != null) _logger.ErrorException(exc.InnerException.ToString(), exc);
            }

        }

        private async Task<bool> StartupMethod()
        {
            ExceptionHelper.Initialize();
            var success = Database.SetAndTestDbConnection();
            if (!success) return false;

            SimpleIocRegistration();

            GlobalVariables.GlobalDbSettings =
                await SimpleIoc.Default.GetInstance<ISettingsRepository>().GetSettings();

            // Live weight is now served by the GlobeweighService over HTTP.
            // The endpoint is derived from the SQL connection string in Database.cs.
            var scaleService = SimpleIoc.Default.GetInstance<IScaleService>();
            await scaleService.EnsureConnectedAsync();

            DispatcherHelper.Initialize();
            UtilitiesShared.RegisterDevice(SimpleIoc.Default.GetInstance<IDeviceRepository>(), false);
            return true;
        }

        // private async Task<bool> StartupMethod()
        // {
        //     ExceptionHelper.Initialize();
        //     var success = Database.SetAndTestDbConnection();
        //     if (!success){return false;}
        //     SimpleIocRegistration();
        //     GlobalVariables.GlobalDbSettings = await SimpleIoc.Default.GetInstance<ISettingsRepository>().GetSettings();
        //     DispatcherHelper.Initialize();
        //     UtilitiesShared.RegisterDevice(SimpleIoc.Default.GetInstance<IDeviceRepository>(), false);
        //     return true;
        // }


        private void SimpleIocRegistration()
        {
            SimpleIoc.Default.Register<IPalletRepository>(() => new PalletRepository());
            SimpleIoc.Default.Register<IDialogService>(() => new DialogService());
            SimpleIoc.Default.Register<IReferenceDataRepository>(() => new ReferenceDataRepository());
            SimpleIoc.Default.Register<IDeviceRepository>(() => new DeviceRepository());
            SimpleIoc.Default.Register<ICustomerSupplierRepository>(() => new CustomerSupplierRepository());
            SimpleIoc.Default.Register<ISettingsRepository>(() => new SettingsRepository());
            SimpleIoc.Default.Register<IGlobeweighUserRepository>(() => new GlobeweighUserRepository());
            SimpleIoc.Default.Register<IProductRepository>(() => new ProductRepository());
            SimpleIoc.Default.Register<IStockLocationRepository>(() => new StockLocationRepository());
            SimpleIoc.Default.Register<IStockRepository>(() => new StockRepository());
            SimpleIoc.Default.Register<IDispatchOrderRepository>(() => new DispatchOrderRepository());
            SimpleIoc.Default.Register<IWeighbridgeRepository>(() => new WeighbridgeRepository());
            SimpleIoc.Default.Register<IHaulierRepository>(() => new HaulierRepository());
            SimpleIoc.Default.Register<IScaleService, ScaleService>(true);
            SimpleIoc.Default.Register<IRelayService, RelayService>(true);
        }


    }
}
