using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Model;

namespace Globeweigh.Shared.Services
{
    public interface IPalletRepository
    {
        Task<List<vwCodicoPallet>> GetProductionPalletsAsync(DateTime dateFrom, DateTime dateTo);
        Task<Pallet> AddUpdatePalletAsync(Pallet pallet);
        int GetNextPalletNo();
        Task<Pallet> GetPalletByPalletNoAsync(int palletNo);
        Task<vwPalletView> GetPalletViewByPalletNoAsync(int palletNo);
        Task<Pallet> GetPalletAsync(int id);
        Task<vwPalletView> GetPalletViewAsync(int id);
        Task<vwPalletView> GetRandomPalletViewAsync();
        Task UpdatePalletsDateOfProduction(List<vwPalletView> selectedPallets, DateTime dateOfProduction);
        Task UpdatePalletDatePrinted(int palletId, DateTime datePrinted);
        Task<List<vwPalletHistory>> GetPalletHistory(int palletId);
        Task<bool> AddAsnPallets(List<Pallet> palletList);
        Task<List<vwPalletView>> GetProductionPalletsByProductId(DateTime date, int productId);
        Task<List<Pallet>> AddPallets(Pallet currentPallet, int noOfLabels);
        Task<List<vwPalletCurrentStock>> GetCurrentStockPalletsByLocationId(int locationId);
        Task<List<vwPalletCurrentStock>> GetCurrentStockPalletsByProductId(int productId);
        Task<bool> UpdatePalletsDateDeleted(List<vwPalletView> selectedPallets, DateTime dateDeleted);
        Task UpdatePalletDateDeleted(int palletId, DateTime dateDeleted);
        Task<List<vwProductionSummaryPalletView>> GetProductionSummaryPallets(DateTime date);
        Task<vwPalletCurrentStock> GetNextAvailablePallet(int productId, int locationId);
        Task<List<GetProductionSummary_Result>> GetProductionSummary(DateTime dateFrom, DateTime dateTo);
        Task<List<GetProductionSummaryByLine_Result>> GetProductionSummaryByLine(DateTime dateFrom, DateTime dateTo);
    }

    public class PalletRepository : IPalletRepository
    {
        public async Task<List<GetProductionSummary_Result>> GetProductionSummary(DateTime dateFrom, DateTime dateTo)
        {
            // Normalize to midnight start
            var from = dateFrom.Date;

            // Normalize to end of day (23:59:59.999)
            var to = dateTo.Date.AddDays(1).AddMilliseconds(-1);

            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Database
                    .SqlQuery<GetProductionSummary_Result>(
                        "EXEC GetProductionSummary @DateFrom, @DateTo",
                        new SqlParameter("@DateFrom", from),
                        new SqlParameter("@DateTo", to)
                    ).ToListAsync();
            }
        }

        public async Task<List<GetProductionSummaryByLine_Result>> GetProductionSummaryByLine(DateTime dateFrom, DateTime dateTo)
        {
            // Normalize to midnight start
            var from = dateFrom.Date;

            // Normalize to end of day (23:59:59.999)
            var to = dateTo.Date.AddDays(1).AddMilliseconds(-1);

            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Database
                    .SqlQuery<GetProductionSummaryByLine_Result>(
                        "EXEC GetProductionSummaryByLine @DateFrom, @DateTo",
                        new SqlParameter("@DateFrom", from),
                        new SqlParameter("@DateTo", to)
                    ).ToListAsync();
            }
        }



        public async Task<List<Pallet>> AddPallets(Pallet currentPallet, int noOfLabels)
        {
            var dateNow = DateTime.Now;
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var listToAdd = new List<Pallet>();
                int nextPalletNo = await GetNextPalletNoAsync(context);
                for (int i = 0; i < noOfLabels; i++)
                {
                    var newBox = new Pallet();
                    newBox.PalletNo = nextPalletNo;
                    nextPalletNo++;
                    newBox.DateCreated = dateNow;
                    newBox.DateOfProduction = currentPallet.DateOfProduction;
                    newBox.ProductId = currentPallet.ProductId;
                    newBox.LocationId = currentPallet.LocationId;
                    newBox.NoOfUnits = currentPallet.NoOfUnits;
                    listToAdd.Add(newBox);
                }
                context.Pallets.AddRange(listToAdd);
                await context.SaveChangesAsync();
                return listToAdd;
            }
        }

        public async Task<List<vwPalletHistory>> GetPalletHistory(int palletId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwPalletHistories.Where(c => c.PalletId == palletId).OrderBy(a => a.DateOfLocationChange).ToListAsync();
            }
        }

        public async Task<Pallet> GetPalletByPalletNoAsync(int palletNo)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Pallets.FirstOrDefaultAsync(c => c.PalletNo == palletNo);
            }
        }

        public async Task<vwPalletView> GetPalletViewByPalletNoAsync(int palletNo)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwPalletViews.FirstOrDefaultAsync(c => c.PalletNo == palletNo);
            }
        }

        public async Task<List<vwProductionSummaryPalletView>> GetProductionSummaryPallets(DateTime date)
        {
            DateTime date1 = date;
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwProductionSummaryPalletViews.Where(c => c.DateOfProduction == date1).OrderBy(a => a.ProductName).ToListAsync();
            }
        }



        public async Task<List<vwCodicoPallet>> GetProductionPalletsAsync(DateTime dateFrom, DateTime dateTo)
        {
            DateTime dateFrom1 = dateFrom;
            DateTime dateTo1 = dateTo;
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwCodicoPallets.Where(c => (c.DateCreated >= dateFrom1 && c.DateCreated <= dateTo1)).OrderByDescending(a => a.DateCreated).ToListAsync();
            }
        }

        public async Task<List<vwPalletView>> GetProductionPalletsByProductId(DateTime date, int productId)
        {

            DateTime date1 = date;
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwPalletViews.Where(c => c.DateOfProduction == date1 && c.ProductId == productId).OrderBy(a => a.PalletNo).ToListAsync();
            }   
        }

        public async Task<List<vwPalletCurrentStock>> GetCurrentStockPalletsByLocationId(int locationId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwPalletCurrentStocks.Where(c => c.LocationId== locationId).OrderBy(a => a.PalletNo).ToListAsync();
            }
        }

        public async Task<List<vwPalletCurrentStock>> GetCurrentStockPalletsByProductId(int productId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwPalletCurrentStocks.Where(c => c.ProductId == productId).OrderBy(a => a.PalletNo).ToListAsync();
            }
        }

        public async Task<Pallet> GetPalletAsync(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Pallets.FirstOrDefaultAsync(c => c.id == id);
            }
        }

        public async Task<vwPalletView> GetPalletViewAsync(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwPalletViews.FirstOrDefaultAsync(c => c.id == id);
            }
        }


        public async Task<vwPalletView> GetRandomPalletViewAsync()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var pallet = await context.vwPalletViews.OrderBy(r => Guid.NewGuid()).Take(1).FirstOrDefaultAsync();
                return pallet;
            }
        }

        public async Task<vwPalletCurrentStock> GetNextAvailablePallet(int productId, int locationId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var pallet = await context.vwPalletCurrentStocks.Where(a=>a.ProductId == productId && a.PickedOrderId == null && a.LocationId == locationId).OrderBy(r => r.PalletNo).Take(1).FirstOrDefaultAsync();
                return pallet;
            }
        }

        public async Task<bool> AddAsnPallets(List<Pallet> palletList)
        {
            try
            {
                using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
                {
                    foreach (var pallet in palletList)
                    {
                        pallet.PalletNo = await GetNextPalletNoAsync(context);
                        context.Pallets.Add(pallet);

                        await context.SaveChangesAsync();
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public async Task<int> GetNextPalletNoAsync(GlobeweighEntities context)
        {
            if (!context.Pallets.Any())
                return 1001;
            return await (from a in context.Pallets select a.PalletNo).MaxAsync() + 1;
        }

        public int GetNextPalletNo()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (!context.Pallets.Any())
                    return 1;
                return (from a in context.Pallets select a.PalletNo).Max() + 1;
            }
        }

        public async Task<Pallet> AddUpdatePalletAsync(Pallet pallet)
        {
            try
            {
                using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
                {
                    if (pallet.id == 0)
                    {
                        DateTime dateNow = DateTime.Now;
                        pallet.PalletNo = GetNextPalletNo();
                        pallet.DateCreated = dateNow;
                        //pallet.OriginalNoOfUnits = pallet.NoOfUnits;
                        context.Pallets.Add(pallet);
                    }
                    else
                    {
                        context.Pallets.Attach(pallet);
                        context.Entry(pallet).State = EntityState.Modified;
                    }
                    await context.SaveChangesAsync();
                    return pallet;
                }
            }
            catch (Exception e)
            {
                return null;
            }
        }


        public async Task UpdatePalletsDateOfProduction(List<vwPalletView> selectedPallets, DateTime dateOfProduction)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                foreach (var pallet in selectedPallets)
                {
                    var palletToUpdate = await context.Pallets.FirstOrDefaultAsync(a => a.id == pallet.id);
                    if (palletToUpdate != null)
                    {
                        palletToUpdate.DateOfProduction = dateOfProduction;
                    }
                }
                await context.SaveChangesAsync();
            }
        }


        public async Task UpdatePalletDatePrinted(int palletId, DateTime datePrinted)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var palletToUpdate = await context.Pallets.FirstOrDefaultAsync(a => a.id == palletId);
                if (palletToUpdate != null)
                {
                    palletToUpdate.DatePrinted = datePrinted;
                    context.Pallets.Attach(palletToUpdate);
                    context.Entry(palletToUpdate).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task UpdatePalletDateDeleted(int palletId, DateTime dateDeleted)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var palletToUpdate = await context.Pallets.FirstOrDefaultAsync(a => a.id == palletId);
                if (palletToUpdate != null)
                {
                    palletToUpdate.DateDeleted = dateDeleted;
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task<bool> UpdatePalletsDateDeleted(List<vwPalletView> selectedPallets, DateTime dateDeleted)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                foreach (var pallet in selectedPallets)
                {
                    var palletToUpdate = await context.Pallets.FirstOrDefaultAsync(a => a.id == pallet.id);
                    if (palletToUpdate != null)
                    {
                        palletToUpdate.DateDeleted = dateDeleted;
                    }
                }
                await context.SaveChangesAsync();
                return true;
            }
        }

    }
}
