using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace Globeweigh.Model
{
    public partial class GlobeweighEntities
    {
        object GetPrimaryKeyValue(DbEntityEntry entry)
        {
            var objectStateEntry = ((IObjectContextAdapter)this).ObjectContext.ObjectStateManager.GetObjectStateEntry(entry.Entity);
            return objectStateEntry.EntityKey.EntityKeyValues[0].Value;
        }

//        public override Task<int> SaveChangesAsync()
//        {
//            try
//            {
//                var modifiedEntities = ChangeTracker.Entries()
//                    .Where(p => p.State == EntityState.Modified).ToList();
//                var now = DateTime.UtcNow;
//
//                foreach (var change in modifiedEntities)
//                {
//                    var entityName = change.Entity.GetType().Name;
//                    var primaryKey = GetPrimaryKeyValue(change);
//
//                    foreach (var prop in change.OriginalValues.PropertyNames)
//                    {
//                        string originalValue = change.OriginalValues[prop] != null ? change.OriginalValues[prop].ToString() : string.Empty;
//                        string currentValue = change.CurrentValues[prop] != null ? change.CurrentValues[prop].ToString() : string.Empty;
//                        if (originalValue != currentValue)
//                        {
//                            ChangeLog log = new ChangeLog()
//                            {
//                                EntityName = entityName,
//                                PrimaryKeyValue = primaryKey.ToString(),
//                                PropertyName = prop,
//                                OldValue = originalValue,
//                                NewValue = currentValue,
//                                DateChanged = now
//                            };
//                            this.ChangeLogs.Add(log);
//                        }
//                    }
//                }
//                return base.SaveChangesAsync();
//            }
//            catch (Exception e)
//            {
//                Console.WriteLine(e);
//                throw;
//            }
//
//
//        }


    }
}
