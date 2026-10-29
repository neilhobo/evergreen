using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Odbc;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using Globeweigh.Model;
using Globeweigh.Model.Custom;
// using Globeweigh.Model.ServiceReference1;
using Globeweigh.Shared;
using Globeweigh.Shared.Helpers;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DevExpress.Xpf.Grid;
using Globeweigh.Model;

namespace Globeweigh.Admin.Services
{
    public static class SageService
    {
        public static async Task<List<Globeweigh.Model.ServiceReference1.SageCustomer>> GetSageCustomers()
        {
            try
            {
                Globeweigh.Model.ServiceReference1.Service1Client client = new Globeweigh.Model.ServiceReference1.Service1Client(new BasicHttpBinding(BasicHttpSecurityMode.None), GlobalVariables.WcfEndpoint);
                var result = client.GetSageCustomers();
                return result;
            }
            catch (Exception e)
            {
                ExceptionHelper.LogError(e, "GetOpenSalesOrdersLinkedToDispatchOrders");
                return null;
            }
        }

        public static async Task<bool> GetSageCustomersTemp()
        {
            List<Model.Custom.Sales_Del_Addr> salesDelAddList = new List<Model.Custom.Sales_Del_Addr>();
            using (OdbcConnection con = new OdbcConnection(GlobalVariables.SageODBCConnectionString))
            {

                con.Open();
                OdbcCommand command = new OdbcCommand(@"Select ACCOUNT_REF,NAME,ADDRESS_TYPE_NAME,CONTACT_NAME,DESCRIPTION,ADDRESS_1,ADDRESS_2,ADDRESS_3,ADDRESS_4,
                                                                ADDRESS_5,ADDRESS_REF,E_MAIL,TELEPHONE,TELEPHONE_2
                                                                from SALES_DEL_ADDR WHERE", con);

                command.CommandTimeout = 180;
                using (OdbcDataReader reader = command.ExecuteReader())
                {

                    while (reader.Read())
                    {
                        Model.Custom.Sales_Del_Addr record = new Model.Custom.Sales_Del_Addr();
                        record.ACCOUNT_REF = reader.GetString(reader.GetOrdinal("ACCOUNT_REF")).Trim();
                        record.NAME = reader.GetString(reader.GetOrdinal("NAME")).Trim();
                        record.ADDRESS_TYPE_NAME = reader.GetString(reader.GetOrdinal("ADDRESS_TYPE_NAME")).Trim();
                        record.CONTACT_NAME = reader.GetString(reader.GetOrdinal("CONTACT_NAME")).Trim();
                        record.DESCRIPTION = reader.GetString(reader.GetOrdinal("DESCRIPTION")).Trim();
                        record.ADDRESS_REF = reader.GetInt32(reader.GetOrdinal("ADDRESS_REF"));
                        record.ADDRESS_1 = reader.GetString(reader.GetOrdinal("ADDRESS_1")).Trim();
                        record.ADDRESS_2 = reader.GetString(reader.GetOrdinal("ADDRESS_2")).Trim();
                        record.ADDRESS_3 = reader.GetString(reader.GetOrdinal("ADDRESS_3")).Trim();
                        record.ADDRESS_4 = reader.GetString(reader.GetOrdinal("ADDRESS_4")).Trim();
                        record.ADDRESS_5 = reader.GetString(reader.GetOrdinal("ADDRESS_5")).Trim();
                        record.E_MAIL = reader.GetString(reader.GetOrdinal("E_MAIL")).Trim();
                        record.TELEPHONE = reader.GetString(reader.GetOrdinal("TELEPHONE")).Trim();
                        record.TELEPHONE_2 = reader.GetString(reader.GetOrdinal("TELEPHONE_2")).Trim();

                        if(string.IsNullOrEmpty(record.ACCOUNT_REF))continue;
                        salesDelAddList.Add(record);
                    }
                }
                con.Close();
            }

            var distinctAccountRefList = salesDelAddList.DistinctBy(a => new { a.ACCOUNT_REF});

            var customerList = new List<Model.Custom.SageCustomer>();
            foreach (var accountRef in distinctAccountRefList)
            {
                var customer = new SageCustomer();
                customer.AccountRef = accountRef.ACCOUNT_REF;
         

                var addressList = new List<Sales_Del_Addr>(salesDelAddList.Where(a => a.ACCOUNT_REF == customer.AccountRef).OrderBy(a=>a.ADDRESS_REF));
                if (addressList.Any())
                {
                    customer.Name = addressList.FirstOrDefault().NAME;
                }
                else
                {
                    customer.Name = accountRef.NAME;
                }


                customer.AddressList = new List<Sales_Del_Addr>(addressList);
                customerList.Add(customer);
            }


            // await SynSageCustomerList(customerList);


            return true;
        }

        private static async Task<bool> SynSageCustomerList(List<Model.Custom.SageCustomer> sageCustomerList)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                int customerAdded = 0;
                int addressesAdded = 0;
                var customerListDb = await context.CustomerSuppliers.Where(a=>a.DateDeleted == null).Include(a=>a.CustomerSupplier_Address).ToListAsync();

                foreach (var sageCustomer in sageCustomerList)
                {
                    var match = customerListDb.FirstOrDefault(a => a.Code == sageCustomer.AccountRef);
                    if (match == null)
                    {
                        customerAdded++;
                        var newCustomerDb = new CustomerSupplier();
                        newCustomerDb.Code = sageCustomer.AccountRef;
                        newCustomerDb.Name = sageCustomer.Name;
                        newCustomerDb.TypeId = (int) eEntityType.Customer;
                        newCustomerDb.CustomerSupplier_Address = new ObservableCollection<CustomerSupplier_Address>();
                        foreach (var salesDelAddr in sageCustomer.AddressList)
                        {
                            addressesAdded++;
                            var newAddressDb = new CustomerSupplier_Address();
                            newAddressDb.Address1 = salesDelAddr.ADDRESS_1;
                            newAddressDb.Address2 = salesDelAddr.ADDRESS_2;
                            newAddressDb.Address3 = salesDelAddr.ADDRESS_3;
                            newAddressDb.Address4 = salesDelAddr.ADDRESS_4;
                            newAddressDb.Address5 = salesDelAddr.ADDRESS_5;
                            newAddressDb.AddressName = salesDelAddr.ADDRESS_TYPE_NAME;
                            newAddressDb.AddressSageRef = salesDelAddr.ADDRESS_REF;
                            newAddressDb.ContactName = salesDelAddr.CONTACT_NAME;
                            newAddressDb.Email = salesDelAddr.E_MAIL;
                            newAddressDb.Telephone = salesDelAddr.TELEPHONE;
                            newAddressDb.Telephone2 = salesDelAddr.TELEPHONE_2;
                            newAddressDb.SageDescription = salesDelAddr.DESCRIPTION;
                            newCustomerDb.CustomerSupplier_Address.Add(newAddressDb);
                        }

                        context.CustomerSuppliers.Add(newCustomerDb);
                    }
                    else
                    {
                        if(match.Inactive)continue;
                    }


                }
                await context.SaveChangesAsync();
                return true;
            }


        }


    }
}
