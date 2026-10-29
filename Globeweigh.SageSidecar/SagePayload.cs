using System;
using System.Collections.Generic;

namespace Globeweigh.SageSidecar
{
    // Mirror of Globeweigh.Api\Services\SageExport.SalesOrderPayload / SalesOrderLine — the
    // JSON the API stores in SageExportJob.Payload and hands out on claim. Change both
    // together. (Newtonsoft matches the camelCase names case-insensitively.)
    public class SalesOrderPayload
    {
        public int OrderId { get; set; }
        public int OrderNo { get; set; }
        public string Reference { get; set; }              // "EVG-<order no>"
        public DateTime OrderDate { get; set; }            // when it was dispatched
        public DateTime? CreatedAt { get; set; }
        public DateTime? DeliveryDate { get; set; }
        public string PoNumber { get; set; }
        public string AccountRef { get; set; }             // Sage A/C ref
        public string CustomerName { get; set; }
        public string ContactName { get; set; }
        public string Telephone { get; set; }
        public string DeliveryName { get; set; }
        public List<string> DeliveryAddress { get; set; }
        public string Comments { get; set; }
        public string HaulierName { get; set; }
        public string VehicleReg { get; set; }
        public string TrailerNo { get; set; }
        public List<SalesOrderLine> Lines { get; set; }
    }

    public class SalesOrderLine
    {
        public int ProductId { get; set; }
        public string StockCode { get; set; }              // Sage STOCK_CODE (Evergreen SKU)
        public string Gtin { get; set; }
        public string Description { get; set; }
        public int? Size { get; set; }
        public int Upp { get; set; }                       // bags per pallet
        public bool IsBulkBag { get; set; }
        public int PalletsOrdered { get; set; }
        public int PalletsPicked { get; set; }
        public int Pallets { get; set; }                   // what actually left
        public int Units { get; set; }                     // Pallets × Upp
        public decimal WeightKg { get; set; }
        public decimal? UnitPrice { get; set; }            // per pallet; null = unknown
        public string PriceSource { get; set; }
    }
}
