using Globeweigh.Shared.Helpers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Globeweigh.Model;

namespace Globeweigh.Admin.Helpers
{
    public static class ExcelProductImporter
    {

        public static async Task ImportProducts()
        {
            string sourceFileLocation = @"C:\Globeweigh\ProductListEdited.xlsx";

            var dataTable = ExcelHelper.ExcelToDataTable(sourceFileLocation);


            //Recipe Items
            int rowNo = 0;
            bool headersFound = false;
            var productList = new List<Product>();
            foreach (DataRow row in dataTable.Rows)
            {
                foreach (var cell in row.ItemArray)
                {
                    if (!headersFound)
                    {
                        if (cell is string)
                        {
                            if (cell.ToString().Contains("Product"))
                            {
                                headersFound = true;
                                rowNo++;
                                break;
                            }
                        }
                    }
                }
                rowNo++;


                if(!headersFound) continue;

                if (rowNo == dataTable.Rows.Count) break;
                var product = new Product();
                product.Description = dataTable.Rows[rowNo].ItemArray[0].ToString();
                product.SkuCode = dataTable.Rows[rowNo].ItemArray[1].ToString();
                product.Size = Convert.ToInt32(dataTable.Rows[rowNo].ItemArray[2].ToString());
                product.Upp = Convert.ToInt32(dataTable.Rows[rowNo].ItemArray[3].ToString());
                product.CommodityCode = dataTable.Rows[rowNo].ItemArray[4].ToString();
                product.Gtin = dataTable.Rows[rowNo].ItemArray[5].ToString();
                product.DateCreated = DateTime.Now;
                productList.Add(product);
            }


            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                context.Products.AddRange(productList);
                await context.SaveChangesAsync();
            }






        }

        public static async Task MatchProducts()
        {



            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var productList = await context.Products.ToListAsync();
                var costingZonePriceList = await context.CostingZonePrices.ToListAsync();
                int noMatchCount = 0;


                foreach (var price in costingZonePriceList)
                {
                    var match = productList.FirstOrDefault(a => a.Gtin == price.Gtin);
                    if (match != null)
                    {
                        price.ProductId = match.id;
                    }
                    else
                    {
                        noMatchCount++;
                    }
                }


                await context.SaveChangesAsync();



            }






        }
    }
}
