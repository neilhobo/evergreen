using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Model;

namespace Globeweigh.Shared.Services
{
    public interface IStockRepository
    {
        Task<List<StockTake>> GetStockTakeList();
        Task AddUpdateStockTake(StockTake stockTake);
        Task<bool> DeleteStockTake(int stockTakeId);
        Task<bool> DeleteStockTakePallet(int palletId);
        Task<List<vwPalletCurrentStock>> GetCurrentStock();
        Task CompleteStockTake(int stockTakeId);
        Task<List<spStockTakeSummary_Result>> GetStockTakeSummary(int stockTakeId);
        // Task<List<vwStockTakeSummary>> GetStockTakeSummaryView(int stockTakeId);
        Task UpdatePalletsOnHoldReleasedAsync(int palletId, bool currentOnHoldStatus);
        //Task<List<vwPalletCurrentStock>> GetCurrentStock(int warehouseId);
        Task<List<vwCurrentStockSummary>> GetCurrentStockSummaryList();
        Task<List<vwCurrentStockLocation>> GetCurrentStockLocationList();
        Task<List<vwCurrentStockSummaryByLocation>> GetCurrentStockByLocation(int locationId);
        Task<List<vwPalletCurrentStock>> GetCurrentStockPalletsByLocation(int locationId);
        Task<List<vwPalletCurrentStock>> GetCurrentStockPalletsByProductId(int productId);
        Task<vwPalletView> GetRandomStockUnitAsync(int productId, bool inStock);
        Task<List<StockHistory>> GetStockHistory(int productId);
    }

    public class StockRepository : IStockRepository
    {
        public async Task<List<vwCurrentStockSummaryByLocation>> GetCurrentStockByLocation(int locationId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwCurrentStockSummaryByLocations.Where(a=>a.LocationId == locationId).OrderBy(a => a.LocationName).ToListAsync();
            }
        }

        public async Task<List<vwCurrentStockLocation>> GetCurrentStockLocationList()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwCurrentStockLocations.OrderBy(a => a.Location).ToListAsync();
            }
        }

        public async Task<List<vwCurrentStockSummary>> GetCurrentStockSummaryList()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwCurrentStockSummaries.OrderBy(a => a.ProductName).ToListAsync();
            }
        }

        public async Task UpdatePalletsOnHoldReleasedAsync(int palletId, bool currentOnHoldStatus)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var palletDb = await context.Pallets.Where(a => a.id == palletId).FirstOrDefaultAsync();

                if (palletDb != null)
                {
                    //palletDb.OnHold = !currentOnHoldStatus;
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task<vwPalletView> GetRandomStockUnitAsync(int productId, bool inStock)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (inStock)
                {
                    return await context.vwPalletViews.Where(a => a.ProductId == productId && a.LocationId >= 0).OrderBy(r => Guid.NewGuid()).Take(1).FirstOrDefaultAsync();
                }

                return await context.vwPalletViews.Where(a => a.ProductId == productId && a.LocationId < 0).OrderBy(r => Guid.NewGuid()).Take(1).FirstOrDefaultAsync();
            }
        }

        public async Task<List<StockHistory>> GetStockHistory(int productId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.StockHistories.Where(a => a.ProductId == productId).OrderByDescending(a => a.DateCreated).ToListAsync();
            }
        }

        public async Task<List<vwPalletCurrentStock>> GetCurrentStock()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwPalletCurrentStocks.OrderBy(a => a.LocationId).ThenBy(a => a.PalletNo).ToListAsync();
            }
        }

        public async Task<List<vwPalletCurrentStock>> GetCurrentStockPalletsByLocation(int locationId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwPalletCurrentStocks.Where(a=>a.LocationId == locationId).OrderBy(a => a.PalletNo).ToListAsync();
            }
        }

        public async Task<List<vwPalletCurrentStock>> GetCurrentStockPalletsByProductId(int productId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwPalletCurrentStocks.Where(a => a.ProductId == productId).OrderBy(a => a.PalletNo).ToListAsync();
            }
        }

        //public async Task<List<vwPalletCurrentStock>> GetCurrentStock(int warehouseId)
        //{
        //    using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
        //    {
        //        return await context.vwPalletCurrentStocks.Where(a=>a.WarehouseId == warehouseId).OrderBy(a => a.WarehouseId).ThenBy(a => a.IsleNo).ThenBy(a => a.ColumnNo).ThenBy(a => a.LevelNo).ToListAsync();
        //    }
        //}

        public async Task<List<StockTake>> GetStockTakeList()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.StockTakes.Where(a => a.DateDeleted == null).OrderByDescending(a => a.id).ToListAsync();
            }
        }

        public async Task<List<spStockTakeSummary_Result>> GetStockTakeSummary(int stockTakeId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return context.spStockTakeSummary(stockTakeId).ToList();
            }
        }

        // public async Task<List<vwStockTakeSummary>> GetStockTakeSummaryView(int stockTakeId)
        // {
        //     using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
        //     {
        //         return await context.vwStockTakeSummaries.Where(a => a.StockTakeId == stockTakeId).ToListAsync();
        //     }
        // }

        public async Task AddUpdateStockTake(StockTake stockTake)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (stockTake.id == 0)
                {
                    stockTake.DateCreated = DateTime.Now;
                    context.StockTakes.Add(stockTake);
                }
                else
                {
                    context.StockTakes.Attach(stockTake);
                    context.Entry(stockTake).State = EntityState.Modified;
                }

                await context.SaveChangesAsync();
            }
        }

        public async Task CompleteStockTake(int stockTakeId)
        {
            try
            {
                using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
                {
                    var dateNow = DateTime.Now;
                    var currentStockAllLocations = await context.StockLocations.ToListAsync();
                    var stockTakePalletList = await context.StockTakePallets.ToListAsync();
                    var currentStockTake = await context.StockTakes.Where(a => a.id == stockTakeId).FirstOrDefaultAsync();

                    foreach (var stockTakePallet in stockTakePalletList)
                    {
                        //                    if (currentStockItem.PalletNo == 14405)
                        //                    {
                        //                        var dddd = "";
                        //                    }

                        var match = currentStockAllLocations.FirstOrDefault(a => a.id == stockTakePallet.LocationId);
                        if (match != null)
                        {
                            // stockTakePallet.ExistingPalletId = match.PalletId;
                            // stockTakePallet.ExistingPalletNo = match.PalletNo;
                            // stockTakePallet.ExistingUnits = match.NoOfUnits;
                        }
                    }

                    if (currentStockTake != null) currentStockTake.DateCompleted = dateNow;

                    await context.SaveChangesAsync();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        public async Task<bool> DeleteStockTake(int stockTakeId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var item = await context.StockTakes.FirstOrDefaultAsync(c => c.id == stockTakeId);
                if (item != null)
                {
                    item.DateDeleted = DateTime.Now;
                    context.StockTakes.Attach(item);
                    context.Entry(item).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                    return true;
                }
                return false;
            }
        }

        public async Task<bool> DeleteStockTakePallet(int palletId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var item = await context.StockTakePallets.FirstOrDefaultAsync(c => c.id == palletId);
                if (item != null)
                {
                    context.StockTakePallets.Remove(item);
                    context.Entry(item).State = EntityState.Deleted;
                    await context.SaveChangesAsync();
                    return true;
                }
                return false;
            }
        }




    }
}
