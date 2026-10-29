using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Globeweigh.Model.Annotations;

namespace Globeweigh.Model
{
    public partial class CostingZone
    {
        public string CurrencyDisplay
        {
            get
            {
                if (this.IsEuro == null) return "Both";
                if ((bool)this.IsEuro) return "Euro";
                return "Sterling";
            }
        }
    }


    public partial class DispatchOrder_Picked
    {
        public string PalletIdDisplay
        {
            get
            {
                if (this.PalletId == "0") return "MANUAL";
                return this.PalletId;
            }
        }
        public bool IsManual => PalletId == "0";

        public int WeightInt
        {
            get
            {

                return Convert.ToInt32(this.Weight);
            }
        }
        //
        //
        //
        // private bool _IsSelected;
        // public bool IsSelected
        // {
        //     get { return _IsSelected; }
        //     set
        //     {
        //         if (_IsSelected != value)
        //         {
        //             _IsSelected = value;
        //             OnPropertyChanged("IsSelected");
        //         }
        //     }
        // }
        //
        // public void RaisePropertiesChangedManual()
        // {
        //     OnPropertyChanged("OnHold");
        // }
    }

    public partial class WeighbridgeRecord
    {
        private List<vwDispatchOrder> _OrderViewList;
        public List<vwDispatchOrder> OrderViewList
        {
            get { return _OrderViewList; }
            set
            {
                if (_OrderViewList != value)
                {
                    _OrderViewList = value;
                    OnPropertyChanged("OrderViewList");
                }
            }
        }

        private List<vwDispatchOrderItem> _OrderItemViewList;
        public List<vwDispatchOrderItem> OrderItemViewList
        {
            get { return _OrderItemViewList; }
            set
            {
                if (_OrderItemViewList != value)
                {
                    _OrderItemViewList = value;
                    OnPropertyChanged("OrderItemViewList");
                }
            }
        }

        public int? NetWeight
        {
            get { return (int?)(WeightIn - WeightOut); }
        }

        public decimal? M3Volume
        {
            get
            {
                if (SampleBoxWeight == null) return null;
                if (NetWeight == 0) return null;
                return (decimal)NetWeight / (SampleBoxWeight * 50);
            }
        }


        public bool WeighComplete
        {
            get
            {
                if (DateWeightOut != null && DateWeightIn != null) return true;
                return false;
            }
        }


        public string WeighType
        {
            get
            {
                if (IsOutgoing) return "Outgoing";
                return "Incoming";
            }
        }

        public int PendingWeight
        {
            get
            {
                if (IsOutgoing) return WeightOut;
                return WeightIn;
            }
        }
        public DateTime? PendingWeighDate
        {
            get
            {
                if (IsOutgoing) return DateWeightIn;
                return DateWeightOut;
            }
        }

        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("PendingWeighDate");
            OnPropertyChanged("VehicleReg");
        }


    }

    public partial class vwFinishedProductWithStock
    {
        private bool _AlreadySelected;
        public bool AlreadySelected
        {
            get { return _AlreadySelected; }
            set
            {
                if (_AlreadySelected != value)
                {
                    _AlreadySelected = value;
                    OnPropertyChanged("AlreadySelected");
                }
            }
        }

        private decimal _Price;
        public decimal Price
        {
            get { return _Price; }
            set
            {
                if (_Price != value)
                {
                    _Price = value;
                    OnPropertyChanged("Price");
                }
            }
        }
        public string CategoryDisplay
        {
            get
            {
                if (this.ProductTypeId == (int)eProductType.Pos) return "POS";
                return CategoryName;
            }
        }
        // public bool Picked => PickedOrderId != null;
        //
        //
        //
        // private bool _IsSelected;
        // public bool IsSelected
        // {
        //     get { return _IsSelected; }
        //     set
        //     {
        //         if (_IsSelected != value)
        //         {
        //             _IsSelected = value;
        //             OnPropertyChanged("IsSelected");
        //         }
        //     }
        // }
        //
        // public void RaisePropertiesChangedManual()
        // {
        //     OnPropertyChanged("OnHold");
        // }
    }

    public partial class ProductType
    {
        public bool IsFinishedProduct
        {
            get
            {
                if (this.id == (int)eProductType.FinishedProduct) return true;
                return false;
            }
        }

        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("IsFinishedProduct");
        }
    }

    public class ClientDispatchOrderMasterObject
    {
        public DispatchOrder DispatchOrder { get; set; }
        public ObservableCollection<DispatchOrderItem> DispatchOrderItems { get; set; }
        public ObservableCollection<vwDispatchOrderItem> DispatchOrderItemViews { get; set; }
    }

    public partial class GlobeweighUser
    {
        public string FullName => this.FirstName + " " + this.Surname;
        public string Initials => this.FirstName.Substring(0, 1).ToUpper() + this.Surname.Substring(0, 1).ToUpper();
        public string RolesDisplay
        {
            get
            {
                string result = string.Empty;
                if (this.Globeweigh_Role == null) return null;
                if (!this.Globeweigh_Role.Any()) return null;

                foreach (var role in Globeweigh_Role)
                {
                    result += EnumHelper.GetEnumDescription((eUserRole)role.RoleId);
                    // result += Enum.GetName(typeof(eUserRole), role.RoleId);
                    result += ", ";

                }

                result = result.Trim();
                return result.TrimEnd(',');

            }
        }
    }

    // public partial class GlobeweighUser
    // {
    //     public string FullName
    //     {
    //         get
    //         {
    //             return (FirstName + " " + Surname).Trim();
    //         }
    //     }
    // }


    public partial class vwPalletView
    {
        // public bool Dispatched => Date != null;

        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("OnHold");
        }
    }


    public partial class Invoice
    {
        public bool DeleteLines { get; set; }

        public string InvoiceNoDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(CustomerCode)) return null;
                return CustomerCode + InvoiceNo.ToString("D4");
            }
        }

        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("InvoiceNoDisplay");
        }
    }

    public partial class InvoiceLine
    {
        public string WeekRangeDisplay
        {
            get
            {
                if (!FirstDateRangeCount) return string.Empty;
                return WeekStart.ToShortDateString() + " - " + WeekEndDisplay.ToShortDateString();
            }

        }

        public bool FirstDateRangeCount { get; set; }


        public DateTime WeekEndDisplay
        {
            get
            {
                return WeekEnd.AddSeconds(-1);
            }
        }

        public int DayCount
        {
            get
            {
                return ((TimeSpan)(WeekEnd - WeekStart)).Days;
            }
        }

        public decimal LineTotal
        {
            get
            {
                return UnitPrice * Quantity;
            }
        }
        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("LineTotal");
        }
    }

    public class AsnMappingObject : INotifyPropertyChanged
    {
        public string KsmHeader { get; set; }
        public string KsmFieldName { get; set; }
        //        public string ClientFieldName { get; set; }

        private string _ClientFieldName;
        public string ClientFieldName
        {
            get { return _ClientFieldName; }
            set
            {
                if (_ClientFieldName != value)
                {
                    _ClientFieldName = value;
                    OnPropertyChanged("ClientFieldName");
                }
            }
        }

        public void RaisePropertyChangedCustom()
        {

        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class vwPalletCurrentStock
    {
        // public bool Picked => PickedOrderId != null;



        private bool _IsSelected;
        public bool IsSelected
        {
            get { return _IsSelected; }
            set
            {
                if (_IsSelected != value)
                {
                    _IsSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("OnHold");
        }
    }


    public partial class DispatchOrder
    {
        // public List<DispatchOrderItem> WcfDispatchOrderItems { get; set; }

        public string CustomerCode { get; set; }

        private decimal? _AverageProductTempActual;
        public decimal? AverageProductTempActual
        {
            get { return _AverageProductTempActual; }
            set
            {
                if (_AverageProductTempActual != value)
                {
                    _AverageProductTempActual = value;
                    OnPropertyChanged("AverageProductTempActual");
                }
            }
        }

        public string OrderNoDisplay
        {
            get
            {
                if (OrderTypeId == (int)eDispatchOrderType.Transfer) return "T-" + TransferOrderNo;
                return OrderNo.ToString();
            }
        }
        //
        // public string OrderDisplay
        // {
        //     get
        //     {
        //         if (string.IsNullOrEmpty(CustomerCode)) return null;
        //         return CustomerCode + OrderNo.ToString("D4");
        //     }
        // }

        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("OrderNoDisplay");
        }
    }

    public partial class DispatchOrderItem
    {
        public int? PosId { get; set; }
        public string PosDescription { get; set; }
        private int? _PosQuantity;
        public int? PosQuantity
        {
            get { return _PosQuantity; }
            set
            {
                if (_PosQuantity != value)
                {
                    _PosQuantity = value;
                    OnPropertyChanged("PosQuantity");
                }
            }
        }

        private string _ProductNameTemp;
        public string ProductNameTemp
        {
            get { return _ProductNameTemp; }
            set
            {
                if (_ProductNameTemp != value)
                {
                    _ProductNameTemp = value;
                    OnPropertyChanged("ProductNameTemp");
                }
            }
        }

        private int? _SizeTemp;
        public int? SizeTemp
        {
            get { return _SizeTemp; }
            set
            {
                if (_SizeTemp != value)
                {
                    _SizeTemp = value;
                    OnPropertyChanged("SizeTemp");
                }
            }
        }

        private int? _UppTemp;
        public int? UppTemp
        {
            get { return _UppTemp; }
            set
            {
                if (_UppTemp != value)
                {
                    _UppTemp = value;
                    OnPropertyChanged("UppTemp");
                }
            }
        }

        // private decimal? _Price;
        // public decimal? Price
        // {
        //     get { return _Price; }
        //     set
        //     {
        //         if (_Price != value)
        //         {
        //             _Price = value;
        //             OnPropertyChanged("Price");
        //         }
        //     }
        // }

        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("TotalPrice");
        }

        public decimal? TotalPrice
        {
            get
            {
                if (this.Quantity == null || this.Price == null || UppTemp == null) return null;
                return Quantity * UppTemp * Price;
            }
        }


    }

    public partial class vwDispatchOrderItem
    {
        public string ProductDeliveryNoteDisplay
        {
            get
            {
                if (this.Size != null)
                {
                    return Size + " " + ProductDescription;
                }
                return ProductDescription;
            }
        }
        public bool TargetMet
        {
            get
            {
                if (PickedQuantity >= Quantity) return true;
                return false;
            }
        }

        public int QuantityRequired
        {
            get
            {
                if (Quantity == null) return 0;
                if (PickedQuantity == null) return (int)Quantity;
                return (int)(Quantity - (int)PickedQuantity);

            }
        }
    }



    public partial class vwOpenDispatchOrderUnnasigned
    {
        private bool _IsSelected;
        public bool IsSelected
        {
            get { return _IsSelected; }
            set
            {
                if (_IsSelected != value)
                {
                    _IsSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

    }

    public partial class vwDispatchOrder
    {

        public List<vwDispatchOrderItem> DispatchOrderItems { get; set; }
        public List<vwDispatchOrderPicked> PickedItems { get; set; }
        public double PercentagePickedDecimal => (double)PickedQuantity / TotalQuantityOrdered;
        public double PercentagePicked => (int)((double)PickedQuantity / TotalQuantityOrdered * 100);

        public byte[] SignatureBytesTempOffice { get; set; }

        public bool TargetMet
        {
            get
            {
                if (PercentagePicked >= 100) return true;
                return false;
            }
        }

        // public bool Assigned
        // {
        //     get
        //     {
        //         if (WeighbridgeRecordId!=0) return true;
        //         return false;
        //     }
        // }

        public string OrderNoDisplay
        {
            get
            {
                if (OrderTypeId == (int)eDispatchOrderType.Transfer) return "T-" + TransferOrderNo;
                return OrderNo.ToString();
            }
        }

        private bool _IsSelected;
        public bool IsSelected
        {
            get { return _IsSelected; }
            set
            {
                if (_IsSelected != value)
                {
                    _IsSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

    }

    public partial class StockTakePallet
    {
        public int LevelPercentageComplete { get; set; }
    }



    public partial class spStockTakeSummary_Result
    {
        public bool HasDifference
        {

            get
            {
                if (this.StockTakePalletId == 0 && this.ExistingPalletId == null) return false;
                if (this.StockTakePalletId == this.ExistingPalletId && this.StockTakePalletUnits != this.ExistingUnits) return true;
                if (this.StockTakePalletId != this.ExistingPalletId) return true;
                return false;
            }
        }
    }

    public partial class Product
    {
        public bool IsFinishedProduct
        {
            get
            {
                if (this.ProductTypeId == (int)eProductType.FinishedProduct) return true;
                return false;
            }
        }
        public string ProductDisplay
        {

            get
            {
                // if (string.IsNullOrEmpty(Code)) return Description;
                // if (string.IsNullOrEmpty(Description)) return Code;
                // return Code + " - " + Description;
                return Description;
            }
        }

        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("IsFinishedProduct");
        }
    }

    public partial class StockTake
    {
        public bool MadeCurrentStock
        {

            get
            {
                return this.DateMadeCurrentStock != null;
            }
        }
    }

    public partial class CustomerSupplier_Address
    {
        public string AddressDisplayOneLine
        {

            get
            {
                return AddressDisplay.Replace(Environment.NewLine, ",");
            }
        }

        public string AddressDisplay
        {
            get
            {
                string result = string.Empty;
                if (!String.IsNullOrEmpty(Address1))
                {
                    result += Address1;
                    result += Environment.NewLine;
                }

                if (!String.IsNullOrEmpty(Address2))
                {
                    result += Address2;
                    result += Environment.NewLine;
                }

                if (!String.IsNullOrEmpty(Address3))
                {
                    result += Address3;
                    result += Environment.NewLine;
                }

                if (!String.IsNullOrEmpty(Address4))
                {
                    result += Address4;
                    result += Environment.NewLine;
                }

                if (!String.IsNullOrEmpty(Address5))
                {
                    result += Address5;
                    result += Environment.NewLine;
                }


                return result;
            }
        }

        public void RaisePropertiesChangedManual()
        {
            OnPropertyChanged("AddressDisplayOneLine");
        }
    }

}

