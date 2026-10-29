using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Globeweigh.Model;

namespace Globeweigh.Shared.Services
{
    public interface IReportRepository
    {
        Task<List<spStockSummaryByCustomer_Result>> GetStockSummary(int customerId);
    }

    public class ReportRepository : IReportRepository
    {
        public async Task<List<spStockSummaryByCustomer_Result>> GetStockSummary(int customerId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return context.spStockSummaryByCustomer(customerId).ToList();
            }
        }


    }
}
