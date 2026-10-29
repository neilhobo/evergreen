using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Globeweigh.Model
{
    public partial class CustomerSupplier
    {
        public bool IsCustomer => TypeId == (int) eEntityType.Customer || IsBoth;
        public bool IsSupplier => TypeId == (int) eEntityType.Supplier || IsBoth;


        public void RaisePropertiesChangesManual()
        {
            OnPropertyChanged("IsCustomer");
            OnPropertyChanged("IsSupplier");
        }
    }


}
