using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Entity;
using System.Linq;
using System.Runtime.CompilerServices;
using Globeweigh.Model.Annotations;

namespace Globeweigh.Model.Custom
{
    public class Sales_Del_Addr
    {
        public string ACCOUNT_REF { get; set; }
        public string NAME { get; set; }
        public string ADDRESS_TYPE_NAME { get; set; }
        public string CONTACT_NAME { get; set; }
        public string DESCRIPTION { get; set; }
        public int ADDRESS_REF { get; set; }
        public string ADDRESS_1 { get; set; }
        public string ADDRESS_2 { get; set; }
        public string ADDRESS_3 { get; set; }
        public string ADDRESS_4 { get; set; }
        public string ADDRESS_5 { get; set; }
        public string E_MAIL { get; set; }
        public string TELEPHONE { get; set; }
        public string TELEPHONE_2 { get; set; }

        public bool Synced { get; set; }
    }

    public class SageCustomer
    {
        public List<Sales_Del_Addr> AddressList { get; set; }
        public string AccountRef { get; set; }
        public string Name { get; set; }
        public bool Synced { get; set; }
    }


}
