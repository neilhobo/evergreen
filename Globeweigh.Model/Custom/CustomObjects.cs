using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity;
using System.Linq;
using System.Runtime.CompilerServices;
using Globeweigh.Model.Annotations;
using Globeweigh.Model.Helpers;

namespace Globeweigh.Model.Custom
{
    public class ProductionSummaryItem
    {
        public int? LineNumber { get; set; }

        public int? id { get; set; }
        public string Gtin { get; set; }
        public string Description { get; set; }
        public int? Size { get; set; }
        public int? Upp { get; set; }
        public int RecordCount { get; set; }
        public int BagCount { get; set; }
    }

    public class SortOrderItem
    {
        public int Value { get; set; }           // 1 = A, 2 = B, etc.
        public string Description { get; set; }  // "A", "B", etc.
    }

    public class InvoiceExportItem
    {
        public int id { get; set; }
        public string ContactName { get; set; }
        public string InvoiceNumber { get; set; }
        public DateTime? InvoiceDate { get; set; }
        public DateTime? DueDate { get; set; }
        public string Description { get; set; }
        public decimal? Quantity { get; set; }
        public decimal UnitAmount { get; set; }
        public string AccountCode { get; set; }
        // public string TaxType { get; set; }
        // public decimal TaxAmount { get; set; }

        public bool ExcludeVat { get; set; }

        public string TaxType
        {
            get
            {
                if (ExcludeVat) return "No Tax";
                return "Tax";
            }
        }

        public decimal TaxAmount
        {
            get
            {
                if (ExcludeVat) return 0;
                if (Quantity == null) return 0;
                return (decimal)(Quantity * (decimal)0.2);
            }
        }
    }


    public class IsleObject : INotifyPropertyChanged
    {
        public int IsleNo { get; set; }
        public string IsleDisplay => "Isle " + IsleNo.ToString("D2");
        public string FreeSpaceCountDisplay => @"Free: " + FreeSpaceCount;
        public string TotalSpaceCountDisplay => @"Spaces: " + TotalSpaceCount;
        public int? FreeSpaceCount { get; set; }
        public int? TotalSpaceCount { get; set; }

        public void RaisePropertiesChanged()
        {
            OnPropertyChanged(nameof(FreeSpaceCountDisplay));
            OnPropertyChanged(nameof(TotalSpaceCountDisplay));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RowObject
    {
        public int LevelNo { get; set; }
        public int LocationId { get; set; }
        public string LevelLetterDisplay => Utilities.Number2String(LevelNo, true);
        public bool IsChecked { get; set; }
    }

    public class ColumnObject
    {
        public int ColumnNo { get; set; }
        public int LocationId { get; set; }
        public bool IsChecked { get; set; }
    }

    public class StockRowObject
    {
        public StockPalletObject[] ColumnObject { get; set; }
    }

    public class StockPalletObject
    {
        public int LocationId { get; set; }
        public bool SpaceAvailable { get; set; }
        public int? PalletNo { get; set; }
        public bool IsEmpty { get; set; }
        public string LocationDisplay { get; set; }
        public string LevelDisplay { get; set; }
        public string WarehouseCode { get; set; }
        public string ColumnDisplay { get; set; }
        public string IsleNo { get; set; }
        public bool? IsDisabled { get; set; }
        public string PalletNosVerticalDisplay { get; set; }
    }

}
