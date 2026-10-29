using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Model;
using Globeweigh.Model.Custom;

namespace Globeweigh.Shared.Services
{
    public interface IStockLocationRepository
    {
        Task<List<StockLocation>> GetStockLocations();
        Task<StockLocation> GetStockLocation(int id);
        Task<StockLocation> AddUpdateStockLocationAsync(StockLocation stockLocation);
        Task<StockLocation> MarkAsDeletedAsync(int entityId);
    }

    public class StockLocationRepository : IStockLocationRepository
    {
        public async Task<StockLocation> MarkAsDeletedAsync(int entityId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var itemToUpdate = await context.StockLocations.FirstOrDefaultAsync(a => a.id == entityId);
                if (itemToUpdate != null)
                {
                    if (itemToUpdate.DateDeleted == null) itemToUpdate.DateDeleted = DateTime.Now;
                    else
                    {
                        itemToUpdate.DateDeleted = null;
                    }
                    context.StockLocations.Attach(itemToUpdate);
                    context.Entry(itemToUpdate).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
                return itemToUpdate;
            }
        }

        public async Task<StockLocation> AddUpdateStockLocationAsync(StockLocation stockLocation)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (stockLocation.id == 0)
                {
                    stockLocation.DateCreated = DateTime.Now;
                    context.StockLocations.Add(stockLocation);
                }
                else
                {
                    context.StockLocations.Attach(stockLocation);
                    context.Entry(stockLocation).State = EntityState.Modified;
                }
                await context.SaveChangesAsync();
                return stockLocation;
            }
        }

        public async Task<List<StockLocation>> GetStockLocations()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var list = await context.StockLocations.Where(a=>a.DateDeleted == null).ToListAsync();
                return list;
            }
        }

        public async Task<StockLocation> GetStockLocation(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.StockLocations.Where(a => a.id == id).FirstOrDefaultAsync();
            }

        }




    }
}
