using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Model;
using System.Data.Entity;
using Globeweigh.Shared.Helpers;
using MessageBox = System.Windows.MessageBox;

namespace Globeweigh.Shared.Services
{
    public interface ISageRepository
    {
        Task ConnectToSage();
    }

    public class SageRepository : ISageRepository
    {

        // public async Task ConnectToSage()
        // {
        //     //Declare Variables
        //     List<Customer> customers = new List<Customer>();
        //     SageDataObject280.SDOEngine oSDO = new SageDataObject280.SDOEngine();
        //     SageDataObject280.WorkSpace oWS;
        //     String szDataPath;
        //     //Instantiate WorkSpace
        //     oWS = (SageDataObject280.WorkSpace)oSDO.Workspaces.Add("Example");
        //     //Show select company dialog
        //
        //     if (UtilitiesShared.IsMyMachine)
        //     {
        //         szDataPath = @"C:\ProgramData\Sage\Accounts\2022\DemoData\ACCDATA";
        //         oWS.Connect(szDataPath, "manager", "", "SDO EXAMPLE");
        //         // szDataPath = @"\\192.168.250.3\SAGE\COMPANY.000\ACCDATA";
        //         // oWS.Connect(szDataPath, "globeweigh", "globeweigh", "SDO EXAMPLE");
        //     }
        //     else
        //     {
        //         szDataPath = oSDO.SelectCompany("C:\\ProgramData\\Sage\\Accounts\\2022");
        //         oWS.Connect(szDataPath, "globeweigh", "globeweigh", "SDO EXAMPLE");
        //     }
        //     try
        //     {
        //     
        //         SageDataObject280.SalesRecord oSalesRecord;
        //         oSalesRecord = (SageDataObject280.SalesRecord)oWS.CreateObject("SalesRecord");
        //
        //
        //
        //         // Move to the first customer record
        //         oSalesRecord.MoveFirst();
        //
        //         // Loop through the customers and populate the list
        //         while (!oSalesRecord.IsEOF())
        //         {
        //             customers.Add(new Customer
        //             {
        //                 AccountRef = oSalesRecord.Fields.Item("ACCOUNT_REF").Value,
        //                 Name = oSalesRecord.Fields.Item("NAME").Value,
        //                 AddressLine1 = oSalesRecord.Fields.Item("AddressLine1").Value
        //                 // Balance = salesLedger.Balance
        //             });
        //
        //             oSalesRecord.MoveNext();
        //         }
        //
        //         // Disconnect when done
        //         MessageBox.Show("Connected!");
        //         //Disconnect
        //         oWS.Disconnect();
        //     }
        //     catch (Exception ex)
        //     {
        //         MessageBox.Show("SDO Generated the Following Error: \n\n" + ex.Message, "Error!");
        //     }
        // }

        public async Task ConnectToSage()
        {
            //Declare Variables
            List<Customer> customers = new List<Customer>();
            SageDataObject280.SDOEngine oSDO = new SageDataObject280.SDOEngine();
            SageDataObject280.WorkSpace oWS;
            String szDataPath;
            SageDataObject280.CustomerAddress oCustomerAddress;
            //Instantiate WorkSpace
            oWS = (SageDataObject280.WorkSpace)oSDO.Workspaces.Add("Example");
            //Declare Objects
            SageDataObject280.CustomerAddresses oCustAdds;
            System.Collections.IEnumerator myCustAddsEnum;
            SageDataObject280.Customer oCustomer;

            if (UtilitiesShared.IsMyMachine)
            {
                szDataPath = @"C:\ProgramData\Sage\Accounts\2022\DemoData\ACCDATA";
                oWS.Connect(szDataPath, "manager", "", "SDO EXAMPLE");
                // szDataPath = @"\\192.168.250.3\SAGE\COMPANY.000\ACCDATA";
                // oWS.Connect(szDataPath, "globeweigh", "globeweigh", "SDO EXAMPLE");
            }
            else
            {
                szDataPath = oSDO.SelectCompany("C:\\ProgramData\\Sage\\Accounts\\2022");
                oWS.Connect(szDataPath, "globeweigh", "globeweigh", "SDO EXAMPLE");
            }

            try
            {
                using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
                {
                    var customerList =  await context.CustomerSuppliers.ToListAsync();

                    var addressListToAdd = new List<CustomerSupplier_Address>();

                    //Create the Customer Address Collection
                    oCustAdds = (SageDataObject280.CustomerAddresses)oWS.CreateCustomerAddresses();
                    oCustomer = (SageDataObject280.Customer)oWS.CreateCustomer();

                    //Get the enumerator for the customer addresses collection
                    myCustAddsEnum = oCustAdds.GetEnumerator();

                    myCustAddsEnum.Reset();

                    //Use the enumerator to move through each item in the collection
                    while (myCustAddsEnum.MoveNext())

                    {
                        Object myEnum = ((SageDataObject280.CustomerAddress)myCustAddsEnum.Current).Customer;

                        var match = customerList.FirstOrDefault(a => a.Code == Convert.ToString(((SageDataObject280.Customer)myEnum).Reference));
                        if (match != null)
                        {
                            var newAddress = new CustomerSupplier_Address();
                            newAddress.CustomerSupplierId = match.id;
                            newAddress.Address1 = ((SageDataObject280.CustomerAddress)myCustAddsEnum.Current).AddressLine1;
                            newAddress.Address2 = ((SageDataObject280.CustomerAddress)myCustAddsEnum.Current).AddressLine2;
                            newAddress.Address3 = ((SageDataObject280.CustomerAddress)myCustAddsEnum.Current).AddressLine3;
                            newAddress.Address4 = ((SageDataObject280.CustomerAddress)myCustAddsEnum.Current).AddressLine4;
                            newAddress.Address5 = ((SageDataObject280.CustomerAddress)myCustAddsEnum.Current).AddressLine5;

                            if(string.IsNullOrEmpty(newAddress.Address1) && string.IsNullOrEmpty(newAddress.Address2) && string.IsNullOrEmpty(newAddress.Address3) && string.IsNullOrEmpty(newAddress.Address4) && string.IsNullOrEmpty(newAddress.Address5))
                            {

                            }
                            else
                            {
                                addressListToAdd.Add(newAddress);
                            }
                      
                        }

                        if (Convert.ToString(((SageDataObject280.Customer)myEnum).Reference) == "AAAA")
                        {
                            //Show the first address line for the current Customer address item
                            // MessageBox.Show("Record AAAA Customer address : " + ((SageDataObject280.CustomerAddress)myCustAddsEnum.Current).AddressLine1);
                        }
                    }

                    context.CustomerSupplier_Address.AddRange(addressListToAdd);
                    await context.SaveChangesAsync();

                }


                oWS.Disconnect();
            }
            catch (Exception ex)
            {
                MessageBox.Show("SDO Generated the Following Error: \n\n" + ex.Message, "Error!");
            }
        }



    }
    // Define a Customer class
    public class Customer
    {
        public string AccountRef { get; set; }
        public string Name { get; set; }
        public double AddressLine1 { get; set; }
    }
}
