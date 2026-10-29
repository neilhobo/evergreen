using System.Threading.Tasks;

namespace Globeweigh.Model
{
    public partial class ErrorLog
    {
        public async Task<ErrorLog> Save(ErrorLog errorLog)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                context.ErrorLogs.Add(errorLog);
                await context.SaveChangesAsync();
            }
            return errorLog;
        }
    }
}
