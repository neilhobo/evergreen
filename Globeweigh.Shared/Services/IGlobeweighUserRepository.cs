using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Globeweigh.Model;

namespace Globeweigh.Shared.Services
{
    public interface IGlobeweighUserRepository
    {
        Task<GlobeweighUser> GetGlobeweighUserAsync(int id);
        Task<GlobeweighUser> GetGlobeweighUserAsync(string email, string password);
        Task<GlobeweighUser> AddUpdateGlobeweighUserAsync(GlobeweighUser globeweighUser);
        Task<bool> MarkAsDeletedAsync(int globeweighUserId);
        Task<List<GlobeweighUser>> GetGlobeweighUsersAsync();
        Task<List<GlobeweighUser>> GetGlobeweighUsersAsync(eUserRole userRole);
        Task<bool> UpdatePassword(int userId, string password, string passcode);
        Task<GlobeweighUser> GetGlobeweighUserAsync(GlobeweighEntities context, int userId);
        Task<GlobeweighUser> AddUpdateGlobeweighUserAsync(GlobeweighEntities context, GlobeweighUser GlobeweighUser);
        Task<List<GlobeweighUser>> GetGlobeweighUsersAsync(bool excludeSuperAdmin);
        Task<GlobeweighUser> LoginTouch(string username, string passcode);
        Task<GlobeweighUser> LoginTouch(int userId, string passcode);
        Task<List<vwGlobeweighUserFactory>> GetGlobeweighFacoryUsers();
    }



    public class GlobeweighUserRepository : IGlobeweighUserRepository
    {

        public async Task<bool> UpdatePassword(int userId, string password, string passcode)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var user = await context.GlobeweighUsers.FirstOrDefaultAsync(a => a.id == userId);

                if (user != null)
                {
                    if (!string.IsNullOrEmpty(password))
                    {
                        if (user.Password != password)
                        {
                            user.Password = password;
                        }
                    }

                    if (!string.IsNullOrEmpty(passcode))
                    {
                        if (user.Passcode != passcode)
                        {
                            user.Passcode = passcode;
                        }
                    }
                }

                await context.SaveChangesAsync();
                return true;
            }
        }

        public async Task<List<GlobeweighUser>> GetGlobeweighUsersAsync()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.GlobeweighUsers.Where(a => a.RoleId != (int) eUserRole.GlobeweighAdmin && a.DateDeleted == null).ToListAsync();
            }
        }

        public async Task<List<vwGlobeweighUserFactory>> GetGlobeweighFacoryUsers()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                //if(excludeSuperAdmin) return await context.GlobeweighUsers.Where(a => a.RoleId != (int)eUserRole.SuperAdmin && a.DateDeleted == null).ToListAsync();
                return await context.vwGlobeweighUserFactories.OrderBy(a=>a.FullName).ToListAsync();
            }
        }

        public async Task<List<GlobeweighUser>> GetGlobeweighUsersAsync(bool excludeSuperAdmin)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                //if(excludeSuperAdmin) return await context.GlobeweighUsers.Where(a => a.RoleId != (int)eUserRole.SuperAdmin && a.DateDeleted == null).ToListAsync();
                return await context.GlobeweighUsers.Include(a => a.Globeweigh_Role).Where(a => a.DateDeleted == null && a.id != 1).ToListAsync();
            }
        }

        public async Task<List<GlobeweighUser>> GetGlobeweighUsersAsync(eUserRole userRole)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.GlobeweighUsers.Where(a => a.RoleId == (int)userRole && a.DateDeleted == null).ToListAsync();
            }
        }

        public async Task<GlobeweighUser> LoginTouch(int userId, string passcode)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var user = await context.GlobeweighUsers.Include(a => a.Globeweigh_Role).FirstOrDefaultAsync(c => c.id == userId);

                if (user != null)
                {
                    if (string.IsNullOrWhiteSpace(user.Passcode))
                    {
                        if (string.IsNullOrWhiteSpace(passcode)) return user;
                        return null;
                    }
                    if (passcode.Equals(user.Passcode, StringComparison.OrdinalIgnoreCase)) return user;
                }
                return null;
            }
        }

        public async Task<GlobeweighUser> LoginTouch(string username, string passcode)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var user = await context.GlobeweighUsers.Include(a => a.Globeweigh_Role).FirstOrDefaultAsync(c => c.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (user != null)
                {
                    if (string.IsNullOrWhiteSpace(user.Passcode))
                    {
                        if (string.IsNullOrWhiteSpace(passcode)) return user;
                        return null;
                    }
                    if (passcode.Equals(user.Passcode, StringComparison.OrdinalIgnoreCase)) return user;
                }
                return null;
            }
        }

        public async Task<GlobeweighUser> GetGlobeweighUserAsync(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.GlobeweighUsers.FirstOrDefaultAsync(c => c.id == id);
            }
        }

        public async Task<GlobeweighUser> GetGlobeweighUserAsync(string username, string password)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var user = await context.GlobeweighUsers.Include(a=>a.Globeweigh_Role).FirstOrDefaultAsync(c => c.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

                if (user != null)
                {
                    if (string.IsNullOrWhiteSpace(user.Password))
                    {
                        if (string.IsNullOrWhiteSpace(password)) return user;
                        return null;
                    }
                    if (password.Equals(user.Password, StringComparison.OrdinalIgnoreCase)) return user;
                }
                return null;
            }
        }

        public async Task<GlobeweighUser> GetGlobeweighUserAsync(GlobeweighEntities context, int userId)
        {
            return await context.GlobeweighUsers.Include(a => a.Globeweigh_Role).FirstOrDefaultAsync(c => c.id == userId);
        }

        public async Task<GlobeweighUser> AddUpdateGlobeweighUserAsync(GlobeweighUser GlobeweighUser)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (GlobeweighUser.id == 0)
                {
                    GlobeweighUser.DateCreated = DateTime.Now;
                    context.GlobeweighUsers.Add(GlobeweighUser);
                }
                else
                {
                    context.GlobeweighUsers.Attach(GlobeweighUser);
                    context.Entry(GlobeweighUser).State = EntityState.Modified;
                }
                await context.SaveChangesAsync();
                return GlobeweighUser;
            }
        }

        public async Task<GlobeweighUser> AddUpdateGlobeweighUserAsync(GlobeweighEntities context, GlobeweighUser GlobeweighUser)
        {
            if (GlobeweighUser.id == 0)
            {
                GlobeweighUser.DateCreated = DateTime.Now;
                context.GlobeweighUsers.Add(GlobeweighUser);
            }
            else
            {
                // context.GlobeweighUsers.Attach(GlobeweighUser);
                context.Entry(GlobeweighUser).State = EntityState.Modified;
            }
            await context.SaveChangesAsync();
            return GlobeweighUser;
        }

        public async Task<bool> MarkAsDeletedAsync(int GlobeweighUserId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var userToUpdate = await context.GlobeweighUsers.FirstOrDefaultAsync(a => a.id == GlobeweighUserId);
                if (userToUpdate != null)
                {
                    userToUpdate.DateDeleted = DateTime.Now;
                    context.GlobeweighUsers.Attach(userToUpdate);
                    context.Entry(userToUpdate).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
                return true;
            }
        }

    }
}
