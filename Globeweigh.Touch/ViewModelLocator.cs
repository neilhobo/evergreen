using CommonServiceLocator;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Touch;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Touch
{
    public class ViewModelLocator
    {
        private static bool _isInitialized;

        public ViewModelLocator()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            if (UtilitiesShared.InDesignMode) return;

            ServiceLocator.SetLocatorProvider(() => SimpleIoc.Default);
            SimpleIoc.Default.Register<MainWindowViewModel>();
            SimpleIoc.Default.Register<HomeViewModel>();
            SimpleIoc.Default.Register<SelectRefDataViewModel>();
            SimpleIoc.Default.Register<KeypadViewModel>();
            SimpleIoc.Default.Register<SelectDateViewModel>();
            SimpleIoc.Default.Register<CleanScreenViewModel>();
            SimpleIoc.Default.Register<KeypadViewModel>();
            SimpleIoc.Default.Register<KeyboardViewModel>();
            SimpleIoc.Default.Register<OkCancelViewModel>();
            SimpleIoc.Default.Register<DownloadNewUpdateViewModel>();
            // SimpleIoc.Default.Register<AddCustomLabelViewModel>();
            SimpleIoc.Default.Register<EditSettingsViewModel>();
            SimpleIoc.Default.Register<ProductionPalletListViewModel>();
            SimpleIoc.Default.Register<SelectProductViewModel>();
            SimpleIoc.Default.Register<CaptureWeightViewModel>();
            SimpleIoc.Default.Register<LoginViewModel>();
            SimpleIoc.Default.Register<DispatchOrderListViewModel>();
            SimpleIoc.Default.Register<StockMasterViewModel>();
            SimpleIoc.Default.Register<StockSummaryViewModel>();
            SimpleIoc.Default.Register<StockLocationViewModel>();
            SimpleIoc.Default.Register<DispatchOrderDetailViewModel>();
            SimpleIoc.Default.Register<AddProductionPalletViewModel>();
            SimpleIoc.Default.Register<AddWeighbridgeRecordViewModel>();
            SimpleIoc.Default.Register<WeighbridgeListViewModel>();
            SimpleIoc.Default.Register<SelectHaulierViewModel>();
            SimpleIoc.Default.Register<WeighbridgeInViewModel>();
            SimpleIoc.Default.Register<DeliveryInViewModel>();
            SimpleIoc.Default.Register<CollectionInViewModel>();
            SimpleIoc.Default.Register<CollectionWeighViewModel>();
            SimpleIoc.Default.Register<WeighbridgeWeighViewModel>();
            SimpleIoc.Default.Register<WeighbridgeOutViewModel>();
            SimpleIoc.Default.Register<SampleWeightViewModel>();
            SimpleIoc.Default.Register<ProductionSummaryViewModel>();

        }

        public MainWindowViewModel MainWindow => SimpleIoc.Default.GetInstance<MainWindowViewModel>();
        public HomeViewModel HomeView => SimpleIoc.Default.GetInstance<HomeViewModel>();
        public CleanScreenViewModel CleanScreenView => SimpleIoc.Default.GetInstance<CleanScreenViewModel>();
        public ProductionPalletListViewModel ProductionPalletListView => SimpleIoc.Default.GetInstance<ProductionPalletListViewModel>();
        public LoginViewModel LoginView => SimpleIoc.Default.GetInstance<LoginViewModel>();
        public DispatchOrderListViewModel DispatchOrderListView => SimpleIoc.Default.GetInstance<DispatchOrderListViewModel>();
        public StockMasterViewModel StockMasterView => SimpleIoc.Default.GetInstance<StockMasterViewModel>();
        public StockSummaryViewModel StockSummaryView => SimpleIoc.Default.GetInstance<StockSummaryViewModel>();
        public StockLocationViewModel StockLocationView => SimpleIoc.Default.GetInstance<StockLocationViewModel>();
        public DispatchOrderDetailViewModel DispatchOrderDetailView => SimpleIoc.Default.GetInstance<DispatchOrderDetailViewModel>();
        public WeighbridgeListViewModel WeighbridgeListView => SimpleIoc.Default.GetInstance<WeighbridgeListViewModel>();
        public WeighbridgeInViewModel WeighbridgeInView => SimpleIoc.Default.GetInstance<WeighbridgeInViewModel>();
        public DeliveryInViewModel DeliveryInView => SimpleIoc.Default.GetInstance<DeliveryInViewModel>();
        public CollectionInViewModel CollectionInView => SimpleIoc.Default.GetInstance<CollectionInViewModel>();
        public CollectionWeighViewModel CollectionWeighView => SimpleIoc.Default.GetInstance<CollectionWeighViewModel>();
        public WeighbridgeWeighViewModel WeighbridgeWeighView => SimpleIoc.Default.GetInstance<WeighbridgeWeighViewModel>();
        public WeighbridgeOutViewModel WeighbridgeOutView => SimpleIoc.Default.GetInstance<WeighbridgeOutViewModel>();
        public SampleWeightViewModel SampleWeightView => SimpleIoc.Default.GetInstance<SampleWeightViewModel>();
        public ProductionSummaryViewModel ProductionSummaryView => SimpleIoc.Default.GetInstance<ProductionSummaryViewModel>();
    }
}