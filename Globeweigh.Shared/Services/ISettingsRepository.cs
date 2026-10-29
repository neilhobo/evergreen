using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Globeweigh.Model;
using System.Data.Entity;

namespace Globeweigh.Shared.Services
{
    public interface ISettingsRepository
    {
        Task<Setting> GetSettings();
        Task<Setting> UpdateSettings();

    }

    public class SettingsRepository : ISettingsRepository
    {
        public async Task<Setting> GetSettings()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Settings.Where(a => a.id == 2).FirstOrDefaultAsync();
            }
        }

        public async Task<Setting> UpdateSettings()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var setting = await context.Settings.Where(a => a.id == 2).FirstOrDefaultAsync();

                setting = GlobalVariables.GlobalDbSettings;

                await context.SaveChangesAsync();
                return setting;
            }
        }

    }
}
