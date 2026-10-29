using System.ComponentModel;
using Globeweigh.Model.Custom;

namespace Globeweigh.Model
{

    public enum eProductType
    {
        FinishedProduct = 1,
        RawMaterial = 4,
        Pos = 3,
        Bespoke = 6


    }

    public enum ePalletType
    {
        Pallet = 1,
        Dolly = 2

    }

    public enum eDispatchOrderType
    {
        [Description("Customer")]
        Customer = 0,
        [Description("Transfer")]
        Transfer = 1
    }

    public enum eEntityType
    {
        Customer = 1,
        Supplier = 2,
        PurchaseOrder = 3,
        PurchaseOrderItem = 4,
        Product = 5,
        // CustomerSupplier = 6,
        FinishedProductCategory = 6
    }

    public enum eRefDataType
    {
        Product = 1,
        Warehouse = 2,
        ProductConditionType = 3,
        FinishedProductCategory = 6,
        GlobeweighUser = 4,
        Customer = 5,
        Haulier = 7,
        Supplier = 10,
        Address = 8,
        StockLocation = 9,

        CostingZone = 10
    }

    public enum ePalletDeliveryCondition
    {
        [Description("Ambient")]
        Ambient = 1,
        [Description("Chilled")]
        Chilled = 2,
        [Description("Frozen")]
        Frozen = 3
    }

    public enum eUserType
    {
        [Description("Office")]
        Office = 1,
        [Description("Touch Operator")]
        Touch = 2,
        [Description("Both")]
        Both = 3
    }

    public enum eInvoiceLineType
    {
        Inbound = 1,
        Outbound = 2,
        Storage = 3
    }

    public enum eAddressType
    {
        Delivery = 0,
        Invoice = 1,
    }

    public enum eUserRole
    {
        [Exclude(true)]
        GlobeweighAdmin = 1,
        [Description("Office Admin")]
        OfficeAdmin = 2,
        [Description("Office User")]
        OfficeUser = 3,
        // Retired: the Touch login picker is driven by eUserType (Touch/Both), not this role.
        [Exclude(true)]
        [Description("Factory User")]
        Factory = 4,
        // Retired: no screen was ever gated on it.
        [Exclude(true)]
        [Description("Weighbridge User")]
        Weighbridge = 5,
        [Description("Sales")]
        Sales = 6
    }


    public enum eBarcodeType
    {
        Column = 1,
        Row = 2,
        Pallet = 5,
        DispatchDocket = 6
    }

    public enum eCostingType
    {
        Intake = 0,
        Dispatch = 1,
        Storage = 2

    }



}
