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
    public interface ICustomerSupplierRepository
    {
        Task<List<CustomerSupplier>> GetDeletedCustomersSuppliers(int entityTypeId);
        Task<List<CustomerSupplier>> GetCustomersSuppliers(int entityTypeId, bool includeDeliveryPoints, bool inactive);
        Task<CustomerSupplier> MarkAsDeletedAsync(int entityId);
        Task<CustomerSupplier> GetCustomerSupplierAsync(int entityId, GlobeweighEntities context);
        Task<CustomerSupplier> AddUpdateAsync(GlobeweighEntities context, CustomerSupplier entity);
        Task<List<CustomerSupplier>> GetCustomersSuppliers();
        Task<bool> MarkInactive(List<vwCustomerSupplier> entityList, bool inactive);
        Task<List<CustomerSupplier_Address>> GetAddresses(int customerSupplierId);

        Task<List<vwCustomerSupplier>> GetCustomersSupplierViews(int entityTypeId, bool includeDeliveryPoints,
            bool inactive);
    }

    public class CustomerSupplierRepository : ICustomerSupplierRepository
    {
        public async Task<List<CustomerSupplier_Address>> GetAddresses(int customerSupplierId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CustomerSupplier_Address.Where(p => p.CustomerSupplierId == customerSupplierId).ToListAsync();
            }
        }

        public async Task<List<CustomerSupplier>> GetDeletedCustomersSuppliers(int entityTypeId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CustomerSuppliers.Where(a => (a.TypeId == entityTypeId || a.IsBoth) && a.DateDeleted != null).OrderBy(a => a.Name).ToListAsync();
            }
        }



        public async Task<List<CustomerSupplier>> GetCustomersSuppliers()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CustomerSuppliers.ToListAsync();
            }
        }

        public async Task<List<CustomerSupplier>> GetCustomersSuppliers(int entityTypeId, bool includeDeliveryPoints, bool inactive)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (includeDeliveryPoints)
                {
                    return await context.CustomerSuppliers
                        .Include(a => a.CustomerSupplier_Address)
                        .Where(a => (a.TypeId == entityTypeId || a.IsBoth) && a.DateDeleted == null && a.Inactive == inactive).OrderBy(a => a.Code).ToListAsync();

                }
                return await context.CustomerSuppliers.Where(a => (a.TypeId == entityTypeId || a.IsBoth) && a.DateDeleted == null && a.Inactive == inactive).OrderBy(a => a.Code).ToListAsync();
            }
        }

        public async Task<List<vwCustomerSupplier>> GetCustomersSupplierViews(int entityTypeId, bool includeDeliveryPoints, bool inactive)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwCustomerSuppliers.Where(a => a.TypeId == entityTypeId).OrderBy(a => a.Code).ToListAsync();
            }
        }

        public async Task<CustomerSupplier> MarkAsDeletedAsync(int entityId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var itemToUpdate = await context.CustomerSuppliers.FirstOrDefaultAsync(a => a.id == entityId);
                if (itemToUpdate != null)
                {
                    if (itemToUpdate.DateDeleted == null)
                    {
                        itemToUpdate.DeletedById = GlobalVariables.CurrentGlobeweighUserId;
                        itemToUpdate.DateDeleted = DateTime.Now;
                    }
                    else
                    {
                        itemToUpdate.DateDeleted = null;
                    }
                    context.CustomerSuppliers.Attach(itemToUpdate);
                    context.Entry(itemToUpdate).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
                return itemToUpdate;
            }
        }

        public async Task<bool> MarkInactive(List<vwCustomerSupplier> entityList, bool inactive)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                foreach (var customerSupplier in entityList)
                {
                    var itemToUpdate = await context.CustomerSuppliers.FirstOrDefaultAsync(a => a.id == customerSupplier.id);
                    if (itemToUpdate != null)
                    {
                        itemToUpdate.Inactive = inactive;
                        await context.SaveChangesAsync();
                    }
                }

                return true;
            }
        }

        public async Task<CustomerSupplier> GetCustomerSupplierAsync(int entityId, GlobeweighEntities context)
        {
            return await context.CustomerSuppliers
                .Include(a => a.CustomerSupplier_Address)
                .Where(a => a.id == entityId).FirstOrDefaultAsync();
        }

        public async Task<CustomerSupplier> GetCustomerSupplier(int entityId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.CustomerSuppliers.Where(a => a.id == entityId).FirstOrDefaultAsync();
            }
        }

        public async Task<CustomerSupplier> AddUpdateAsync(GlobeweighEntities context, CustomerSupplier entity)
        {
            if (entity.id == 0)
            {
                entity.DateCreated = DateTime.Now;
                entity.CreatedById = GlobalVariables.CurrentGlobeweighUserId;
                context.CustomerSuppliers.Add(entity);
            }

            await context.SaveChangesAsync();
            return entity;
        }
    }
}
