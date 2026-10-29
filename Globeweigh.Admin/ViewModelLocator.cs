using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Shared.Helpers;
using Globeweigh.Shared.Services;
using CommonServiceLocator;
using DevExpress.Xpf;

namespace Globeweigh.Admin
{
    public class ViewModelLocator
    {
        /// <summary>
        /// This class contains static references to all the view models in the
        /// application and provides an entry point for the bindings.
        /// </summary>
        private static bool _isInitialized;

        public ViewModelLocator()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            if (UtilitiesShared.InDesignMode) return;

            ServiceLocator.SetLocatorProvider(() => SimpleIoc.Default);
            SimpleIoc.Default.Register<MainWindowViewModel>();
            SimpleIoc.Default.Register<EntitiesViewModel>();
            SimpleIoc.Default.Register<ReferenceDataViewModel>();
            SimpleIoc.Default.Register<AddEditRefDataViewModel>();
            SimpleIoc.Default.Register<CustomerSupplierListViewModel>();
            SimpleIoc.Default.Register<AddEditCustomerSupplierViewModel>();
            SimpleIoc.Default.Register<PrintExportOptionsViewModel>();
            SimpleIoc.Default.Register<AddEditGenericDataViewModel>();
            SimpleIoc.Default.Register<DownloadNewUpdateViewModel>();
            SimpleIoc.Default.Register<ProductionPalletListViewModel>();
            SimpleIoc.Default.Register<ReferenceDataMasterViewModel>();
            SimpleIoc.Default.Register<GlobeweighUserListViewModel>();
            SimpleIoc.Default.Register<AddEditGlobeweighUserViewModel>();
            SimpleIoc.Default.Register<ProductConditionTypeViewModel>();
            SimpleIoc.Default.Register<AddEditProductContitionTypeViewModel>();
            SimpleIoc.Default.Register<AddEditProductViewModel>();
            SimpleIoc.Default.Register<ProductListViewModel>();
            SimpleIoc.Default.Register<StockHistoryViewModel>();
            SimpleIoc.Default.Register<StockTakeViewModel>();
            SimpleIoc.Default.Register<StockMasterViewModel>();
            SimpleIoc.Default.Register<CurrentStockListViewModel>();
            SimpleIoc.Default.Register<EditProductionPalletViewModel>();
            SimpleIoc.Default.Register<DispatchOrderListViewModel>();
            SimpleIoc.Default.Register<CreateDispatchOrderViewModel>();
            SimpleIoc.Default.Register<SelectPalletViewModel>();
            SimpleIoc.Default.Register<EditMultipleProductionPalletsViewModel>();
            SimpleIoc.Default.Register<SelectAddressViewModel>();
            SimpleIoc.Default.Register<AddEditAddressViewModel>();
            SimpleIoc.Default.Register<AddressListViewModel>();
            SimpleIoc.Default.Register<ReportsMasterViewModel>();
            SimpleIoc.Default.Register<ReportDispatchInStockViewModel>();
            SimpleIoc.Default.Register<ReportStockSummaryViewModel>();
            SimpleIoc.Default.Register<EditCompletedDispatchViewModel>();
            SimpleIoc.Default.Register<ReportDispatchRecordsViewModel>();
            SimpleIoc.Default.Register<PalletHistoryViewModel>();
            SimpleIoc.Default.Register<MyAccountViewModel>();
            SimpleIoc.Default.Register<SelectRolesViewModel>();
            SimpleIoc.Default.Register<FullDispatchOrderViewModel>();
            SimpleIoc.Default.Register<AddEditLocationViewModel>();
            SimpleIoc.Default.Register<StockLocationListViewModel>();
            SimpleIoc.Default.Register<CurrentStockLocationViewModel>();
            SimpleIoc.Default.Register<CurrentStockSummaryViewModel>();
            SimpleIoc.Default.Register<SelectBulkPalletViewModel>();
            SimpleIoc.Default.Register<AddProductionPalletsViewModel>();
            SimpleIoc.Default.Register<HaulierListViewModel>();
            SimpleIoc.Default.Register<AddEditHaulierViewModel>();
            SimpleIoc.Default.Register<AddEditDispatchOrderItemViewModel>();
            SimpleIoc.Default.Register<WeighbridgeMasterViewModel>();
            SimpleIoc.Default.Register<AddIncomingViewModel>();
            SimpleIoc.Default.Register<WeighbridgeViewModel>();
            SimpleIoc.Default.Register<FinishedProductStockMasterViewModel>();
            SimpleIoc.Default.Register<AutoPickViewModel>();
            SimpleIoc.Default.Register<EditWeighbrideRecordViewModel>();
            SimpleIoc.Default.Register<SelectSalesOrderViewModel>();
            SimpleIoc.Default.Register<AboutViewModel>();
            SimpleIoc.Default.Register<AddOutgoingViewModel>();
            SimpleIoc.Default.Register<EditOutgoingViewModel>();
            SimpleIoc.Default.Register<CostingMasterViewModel>();
            SimpleIoc.Default.Register<CostingPriceListViewModel>();
            SimpleIoc.Default.Register<EditZonePriceViewModel>();
            SimpleIoc.Default.Register<CostingZoneListViewModel>();
            SimpleIoc.Default.Register<AddEditCostingZoneViewModel>();
            SimpleIoc.Default.Register<ProductionMasterViewModel>();
            SimpleIoc.Default.Register<ProductionSummaryViewModel>();
            SimpleIoc.Default.Register<WeighOutViewModel>();
            SimpleIoc.Default.Register<CompletedOrderListViewModel>();
            SimpleIoc.Default.Register<CreateLoadViewModel>();
            SimpleIoc.Default.Register<ViewLoadDetailsViewModel>();
            SimpleIoc.Default.Register<ProductsMasterViewModel>();
            SimpleIoc.Default.Register<RawProductListViewModel>();
            SimpleIoc.Default.Register<PosProductListViewModel>();
            SimpleIoc.Default.Register<AddRawSupplierViewModel>();
            SimpleIoc.Default.Register<AddRawProductViewModel>();
            SimpleIoc.Default.Register<AddBogViewModel>();
            SimpleIoc.Default.Register<WeighbridgeRecordsViewModel>();
            SimpleIoc.Default.Register<AddManualWeighbridgeViewModel>();
            SimpleIoc.Default.Register<GateBarrierViewModel>();
            SimpleIoc.Default.Register<GateBarrierWebViewModel>();
            




        }

        public MainWindowViewModel MainWindow => SimpleIoc.Default.GetInstance<MainWindowViewModel>();
        public GlobeweighUserListViewModel GlobeweighUserListView => SimpleIoc.Default.GetInstance<GlobeweighUserListViewModel>();
        public ReferenceDataViewModel ReferenceDataView => SimpleIoc.Default.GetInstance<ReferenceDataViewModel>();
        public EntitiesViewModel EntitiesView => SimpleIoc.Default.GetInstance<EntitiesViewModel>();
        public CustomerSupplierListViewModel CustomerSupplierListView => SimpleIoc.Default.GetInstance<CustomerSupplierListViewModel>();
        public ProductionPalletListViewModel ProductionPalletListView => SimpleIoc.Default.GetInstance<ProductionPalletListViewModel>();
        public ReferenceDataMasterViewModel ReferenceDataMasterView => SimpleIoc.Default.GetInstance<ReferenceDataMasterViewModel>();
        public ProductConditionTypeViewModel ProductConditionTypeView => SimpleIoc.Default.GetInstance<ProductConditionTypeViewModel>();
        public ProductListViewModel ProductListView => SimpleIoc.Default.GetInstance<ProductListViewModel>();
        public StockMasterViewModel StockMasterView => SimpleIoc.Default.GetInstance<StockMasterViewModel>();
        public StockTakeViewModel StockTakeView => SimpleIoc.Default.GetInstance<StockTakeViewModel>();
        public StockHistoryViewModel StockHistoryView => SimpleIoc.Default.GetInstance<StockHistoryViewModel>();
        public CurrentStockListViewModel CurrentStockListView => SimpleIoc.Default.GetInstance<CurrentStockListViewModel>();
        public DispatchOrderListViewModel DispatchOrderListView => SimpleIoc.Default.GetInstance<DispatchOrderListViewModel>();
        public AddressListViewModel AddressListView => SimpleIoc.Default.GetInstance<AddressListViewModel>();
        public ReportsMasterViewModel ReportsMasterView => SimpleIoc.Default.GetInstance<ReportsMasterViewModel>();
        public ReportDispatchInStockViewModel ReportDispatchInStockView => SimpleIoc.Default.GetInstance<ReportDispatchInStockViewModel>();
        public ReportStockSummaryViewModel ReportStockSummaryView => SimpleIoc.Default.GetInstance<ReportStockSummaryViewModel>();
        public ReportDispatchRecordsViewModel ReportDispatchRecordsView => SimpleIoc.Default.GetInstance<ReportDispatchRecordsViewModel>();
        public StockLocationListViewModel StockLocationListView => SimpleIoc.Default.GetInstance<StockLocationListViewModel>();
        public CurrentStockLocationViewModel CurrentStockLocationView => SimpleIoc.Default.GetInstance<CurrentStockLocationViewModel>();
        public CurrentStockSummaryViewModel CurrentStockSummaryView => SimpleIoc.Default.GetInstance<CurrentStockSummaryViewModel>();
        public HaulierListViewModel HaulierListView => SimpleIoc.Default.GetInstance<HaulierListViewModel>();
        public WeighbridgeMasterViewModel WeighbridgeMasterView => SimpleIoc.Default.GetInstance<WeighbridgeMasterViewModel>();
        public WeighbridgeViewModel WeighbridgeView => SimpleIoc.Default.GetInstance<WeighbridgeViewModel>();
        public FinishedProductStockMasterViewModel FinishedProductStockMasterView => SimpleIoc.Default.GetInstance<FinishedProductStockMasterViewModel>();
        public CostingMasterViewModel CostingMasterView => SimpleIoc.Default.GetInstance<CostingMasterViewModel>();
        public CostingPriceListViewModel CostingPriceListView => SimpleIoc.Default.GetInstance<CostingPriceListViewModel>();
        public CostingZoneListViewModel CostingZoneListView => SimpleIoc.Default.GetInstance<CostingZoneListViewModel>();
        public ProductionMasterViewModel ProductionMasterView => SimpleIoc.Default.GetInstance<ProductionMasterViewModel>();
        public ProductionSummaryViewModel ProductionSummaryView => SimpleIoc.Default.GetInstance<ProductionSummaryViewModel>();
        public CompletedOrderListViewModel CompletedOrderListView => SimpleIoc.Default.GetInstance<CompletedOrderListViewModel>();
        public ProductsMasterViewModel ProductsMasterView => SimpleIoc.Default.GetInstance<ProductsMasterViewModel>();
        public RawProductListViewModel RawProductListView => SimpleIoc.Default.GetInstance<RawProductListViewModel>();
        public PosProductListViewModel PosProductListView => SimpleIoc.Default.GetInstance<PosProductListViewModel>();
        public WeighbridgeRecordsViewModel WeighbridgeRecordsView => SimpleIoc.Default.GetInstance<WeighbridgeRecordsViewModel>();
        public GateBarrierViewModel GateBarrierView => SimpleIoc.Default.GetInstance<GateBarrierViewModel>();
        public GateBarrierWebViewModel GateBarrierWebView => SimpleIoc.Default.GetInstance<GateBarrierWebViewModel>();
    }
}