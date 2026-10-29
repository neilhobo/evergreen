using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Model;

namespace Globeweigh.Shared.Services
{
    public interface IHaulierRepository
    {
        Task<List<Haulier>> GetHaulierList();
        Task<Haulier> GetHaulier(int id);
        Task<Haulier> AddUpdateHaulier(Haulier haulier);
        Task<bool> MarkAsDeletedAsync(int id);
        Task<List<Haulier>> GetIntakeHaulierList();
    }



    public class HaulierRepository : IHaulierRepository
    {
        public async Task<List<Haulier>> GetIntakeHaulierList()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Hauliers.Where(a => a.DateDeleted == null && a.IntakeHaulier == true).OrderBy(a => a.Name).ToListAsync();
            }
        }

        public async Task<List<Haulier>> GetHaulierList()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Hauliers.Where(a => a.DateDeleted == null).OrderBy(a=>a.Name).ToListAsync();
            }
        }

        public async Task<Haulier> GetHaulier(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Hauliers.FirstOrDefaultAsync(c => c.id == id);
            }
        }

        public async Task<Haulier> AddUpdateHaulier(Haulier haulier)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (haulier.id == 0)
                {
                    haulier.DateCreated = DateTime.Now;
                    context.Hauliers.Add(haulier);
                }
                else
                {
                    context.Hauliers.Attach(haulier);
                    context.Entry(haulier).State = EntityState.Modified;
                }
                await context.SaveChangesAsync();
                return haulier;
            }
        }

        public async Task<bool> MarkAsDeletedAsync(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var userToUpdate = await context.Hauliers.FirstOrDefaultAsync(a => a.id == id);
                if (userToUpdate != null)
                {
                    userToUpdate.DateDeleted = DateTime.Now;
                    await context.SaveChangesAsync();
                }
                return true;
            }
        }

    }
}
