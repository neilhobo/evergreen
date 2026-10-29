using System.Data.Entity.Infrastructure;

namespace Globeweigh.Model
{
    public partial class GlobeweighEntities
    {
        public GlobeweighEntities(string nameOrConnectionString)
        : base(nameOrConnectionString)
        {
//            var objectContext = (this as IObjectContextAdapter).ObjectContext;
            // Sets the command timeout for all the commands
//            objectContext.CommandTimeout = 120;
        }
    }
}
