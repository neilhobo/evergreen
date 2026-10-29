using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DevExpress.Xpf.Grid;
using Globeweigh.Model;

namespace Globeweigh.Shared.Services
{
    public interface ICostingRepository
    {
        Task<List<CostingZonePriceView>> GetProductCostingView();
        Task<List<vwCostingZonePrice>> GetProductCostingZonesView(int productId);
        Task<CostingZonePrice> GetCostZonePrice(int id);
        Task<CostingZonePrice> AddUpdateCostZonePrice(CostingZonePrice costingZonePrice);
        Task<bool> UpdateCostZonePrice(decimal newPrice, vwCostingZonePrice oldCostingZonePrice);
        Task<List<CostingZonePriceHistory>> GetCostingZonePriceHistory(int id);
        Task<List<CostingZone>> GetCostingZones();
        Task<bool> MarkCostingZoneAsDeleted(int id);
        Task<CostingZone> GetCostingZone(int id);
        Task<CostingZone> AddUpdateCostingZone(CostingZone costingZone);
        Task<List<CostingZonePrice>> GetCostingZonePriceList(int costingZoneId);
    }

    public class CostingRepository : ICostingRepository
    {
        public async Task<List<CostingZonePrice>> GetCostingZonePriceList(int costingZoneId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CostingZonePrices.Where(a => a.CostingZoneId== costingZoneId).ToListAsync();
            }
        }

        public async Task<CostingZone> AddUpdateCostingZone(CostingZone costingZone)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (costingZone.id == 0)
                {
                }
                else
                {
                    context.CostingZones.Attach(costingZone);
                    context.Entry(costingZone).State = EntityState.Modified;
                }

                await context.SaveChangesAsync();
                return costingZone;
            }
        }

        public async Task<CostingZone> GetCostingZone(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CostingZones.FirstOrDefaultAsync(a => a.id == id);
            }
        }

        public async Task<List<CostingZone>> GetCostingZones()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CostingZones.Where(a => a.DateDeleted == null).OrderBy(a=>a.id).ToListAsync();
            }
        }

        public async Task<bool> MarkCostingZoneAsDeleted(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var itemDb = await context.CostingZones.FirstOrDefaultAsync(a => a.id == id);
                if (itemDb != null)
                {
                    itemDb.DateDeleted = DateTime.Now;
                    await context.SaveChangesAsync();
                }
                return true;
            }
        }

        public async Task<CostingZonePrice> GetCostZonePrice(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CostingZonePrices.FirstOrDefaultAsync(a => a.id == id);
            }
        }

        public async Task<List<CostingZonePriceHistory>> GetCostingZonePriceHistory(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CostingZonePriceHistories.Where(a=>a.ConstingZonePriceId == id).ToListAsync();
            }
        }

        public async Task<List<CostingZonePriceView>> GetProductCostingView()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CostingZonePriceViews.ToListAsync();
            }
        }

        public async Task<List<vwCostingZonePrice>> GetProductCostingZonesView(int productId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwCostingZonePrices.AsNoTracking().Where(a => a.ProductId == productId).OrderBy(a => a.CostingZoneId).ToListAsync();
            }
        }

        public async Task<CostingZonePrice> AddUpdateCostZonePrice(CostingZonePrice costingZonePrice)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (costingZonePrice.id == 0)
                {
                }
                else
                {
                    context.CostingZonePrices.Attach(costingZonePrice);
                    context.Entry(costingZonePrice).State = EntityState.Modified;
                }

                await context.SaveChangesAsync();
                return costingZonePrice;
            }
        }

        public async Task<bool> UpdateCostZonePrice(decimal newPrice, vwCostingZonePrice oldCostingZonePrice)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var costingPriceDb = await context.CostingZonePrices.FirstOrDefaultAsync(a => a.id == oldCostingZonePrice.id);
                if (costingPriceDb == null) return false;
                if (costingPriceDb.Price == newPrice) return false;
                costingPriceDb.Price = newPrice;
                AddCostingPriceHistoryRecord(newPrice, oldCostingZonePrice, context);
                await context.SaveChangesAsync();
                return true;
            }
        }

        private void AddCostingPriceHistoryRecord(decimal newPrice, vwCostingZonePrice oldCostingZonePrice, GlobeweighEntities context)
        {
            var history = new CostingZonePriceHistory();
            history.NewPrice = newPrice;
            history.OldPrice = oldCostingZonePrice.Price;
            history.ConstingZonePriceId = oldCostingZonePrice.id;
            history.ZoneId = oldCostingZonePrice.CostingZoneId;
            history.ProductId = oldCostingZonePrice.ProductId;
            history.ChangedBy = GlobalVariables.CurrentGlobeweighUser.FullName;
            history.DateCreated = DateTime.Now;
            context.CostingZonePriceHistories.Add(history);
        }
    }
}
