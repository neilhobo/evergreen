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
    public interface IWeighbridgeRepository
    {
        Task<List<WeighbridgeRecord>> GetWeighbridgeRecords();
        Task<List<vwWeighbridgePending>> GetPendingWeighbridgeRecords();
        Task<WeighbridgeRecord> MarkAsDeletedAsync(int entityId);
        Task<WeighbridgeRecord> AddUpdateAsync(WeighbridgeRecord entity);
        // Task<WeighbridgeRecord> GetWeighbridgeRecord(int entityId);
        Task<WeighbridgeRecord> GetWeighbridgeRecord(int recordId, bool includeOrderViews, bool includeOrderItemViews);
        Task<List<ReferenceData>> GetVehiclesByHaulierId(int haulierId);
        Task<ReferenceData> DeleteVehicle(int vehicleId);
        Task<ReferenceData> DeleteHaulier(int haulierId);
        Task<List<WeighbridgeRecord>> GetWeighbridgeRecords(DateTime dateFrom, DateTime dateTo);
        // Task<bool> UpdateRecordAssigned(int recordId, List<vwOpenDispatchOrderUnnasigned> orderList);
        // Task<bool> DeleteWeighbridgeRecordOrder(int entityId);
        Task<List<vwWeighbridgeOrderAssigned>> GetAssignedOrders(int weighbridgeRecordId);
        Task<WeighbridgeRecord> CompleteWeighBridgeRecord(WeighbridgeRecord entity);
        Task<List<vwWeighbridgeComplete>> GetWeighbridgeCompleteRecords(DateTime dateFrom, DateTime dateTo);
        Task<WeighbridgeRecord> AddUpdateOutgoing(WeighbridgeRecord entity, List<vwDispatchOrder> selectedDispatchOrders);
        Task<vwWeighbridgePending> GetWeighbridgeRecordPendingView(int entityId);
        Task<List<vwWeighbridgeComplete>> GetCompletedWeighbridgeRecordViews(DateTime dateFrom, DateTime dateTo);
        Task<vwWeighbridgePreLoad> GetWeighbridgeRecordPendingViewByIdRef(string refId);
        Task<vwWeighbridgePreLoad> GetWeighbridgeRecordPreloadView(int entityId);
        Task<List<vwWeighbridgePending>> GetWeighbridgeRecordsWeighedIn();
        Task<WeighbridgeRecord> AddSampleWeight(int entityId, decimal sampleWeight);
    }

    public class WeighbridgeRepository : IWeighbridgeRepository
    {
        public async Task<WeighbridgeRecord> AddSampleWeight(int entityId, decimal sampleWeight)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var itemToUpdate = await context.WeighbridgeRecords.FirstOrDefaultAsync(a => a.id == entityId);
                if (itemToUpdate != null)
                {
                    itemToUpdate.DateSampleWeightAdded = DateTime.Now;
                    itemToUpdate.SampleBoxWeight = sampleWeight;
                    await context.SaveChangesAsync();
                }
                return itemToUpdate;
            }
        }

        public async Task<List<vwWeighbridgeComplete>> GetCompletedWeighbridgeRecordViews(DateTime dateFrom, DateTime dateTo)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwWeighbridgeCompletes.Where(c => c.DateWeightOut != null && c.DateWeightOut >= dateFrom.Date && c.DateWeightOut <= dateTo).OrderByDescending(a => a.id).ToListAsync();
            }
        }

        public async Task<WeighbridgeRecord> AddUpdateOutgoing(WeighbridgeRecord entity, List<vwDispatchOrder> selectedDispatchOrders)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (entity.id == 0)
                {
                    entity.DateCreated = DateTime.Now;
                    context.WeighbridgeRecords.Add(entity);
                    await context.SaveChangesAsync();

                    if (selectedDispatchOrders != null && selectedDispatchOrders.Any())
                    {
                        foreach (var order in selectedDispatchOrders)
                        {
                            var orderDb = await context.DispatchOrders.FirstOrDefaultAsync(a => a.id == order.id);
                            if (orderDb != null)
                            {
                                orderDb.WeighbridgeRecordId = entity.id;
                                orderDb.SortOrder = order.SortOrder;
                            }
                            // var newLink = new WeighbridgeRecord_Order();
                            // newLink.WeighbridgeRecordId = entity.id;
                            // newLink.OrderId = order.id;
                            // context.WeighbridgeRecord_Order.Add(newLink);
                        }
                        await context.SaveChangesAsync();
                    }
                }
                else
                {
                    context.WeighbridgeRecords.Attach(entity);
                    context.Entry(entity).State = EntityState.Modified;

                    var linkedOrdersDb = await context.DispatchOrders.Where(a => a.WeighbridgeRecordId == entity.id)
                        .ToListAsync();
                    foreach (var orderDb in linkedOrdersDb)
                    {
                        orderDb.WeighbridgeRecordId = 0;
                    }

                    foreach (var order in selectedDispatchOrders)
                    {
                        var match = linkedOrdersDb.FirstOrDefault(a => a.id == order.id);
                        if (match != null)
                        {
                            match.WeighbridgeRecordId = entity.id;
                            match.SortOrder = order.SortOrder;
                        }
                        else
                        {
                            var orderDb = await context.DispatchOrders.FirstOrDefaultAsync(a => a.id == order.id);
                            if (orderDb != null)
                            {
                                orderDb.WeighbridgeRecordId = entity.id;
                            }
                        }
                    }

                    await context.SaveChangesAsync();

                }

            }
            return entity;
        }

        public async Task<WeighbridgeRecord> CompleteWeighBridgeRecord(WeighbridgeRecord entity)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var dispatchOrdersDb = await context.DispatchOrders.Where(a => a.WeighbridgeRecordId == entity.id).ToListAsync();
                foreach (var orderDb in dispatchOrdersDb)
                {
                    orderDb.DateDispatched = DateTime.Now;
                    var orderItems = await context.DispatchOrderItems.Where(a => a.OrderId == orderDb.id).ToListAsync();
                    foreach (var orderItem in orderItems)
                    {
                        var history = new StockHistory();
                        history.EntityId = orderDb.id;
                        history.ProductId = orderItem.ProductId;
                        history.TransactionType = "Dispatch";
                        history.Quantity = (int)orderItem.Quantity;
                        history.DateCreated = DateTime.Now;
                        context.StockHistories.Add(history);
                    }

                }
                context.WeighbridgeRecords.Attach(entity);
                context.Entry(entity).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }
            return entity;
        }

        public async Task<List<vwWeighbridgeOrderAssigned>> GetAssignedOrders(int weighbridgeRecordId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwWeighbridgeOrderAssigneds.Where(a => a.WeighbridgeRecordId == weighbridgeRecordId).OrderBy(a => a.Sequence).AsNoTracking().ToListAsync();
            }
        }

        // public async Task<bool> DeleteWeighbridgeRecordOrder(int entityId)
        // {
        //     using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
        //     {
        //         var itemDb = await context.WeighbridgeRecord_Order.FirstOrDefaultAsync(a => a.id == entityId);
        //         if (itemDb != null)
        //         {
        //             context.Entry(itemDb).State = EntityState.Deleted;
        //             await context.SaveChangesAsync();
        //             return true;
        //         }
        //     }
        //     return false;
        // }

        public async Task<List<vwWeighbridgePending>> GetPendingWeighbridgeRecords()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwWeighbridgePendings.AsNoTracking().ToListAsync();
            }
        }

        public async Task<List<vwWeighbridgePending>> GetWeighbridgeRecordsWeighedIn()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwWeighbridgePendings.Where(a=>a.DateWeightIn !=null).AsNoTracking().ToListAsync();
            }
        }

        public async Task<List<WeighbridgeRecord>> GetWeighbridgeRecords()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.WeighbridgeRecords.Where(a => a.DateWeightOut != null && a.DateDeleted == null).OrderByDescending(a => a.DateWeightOut).ToListAsync();
            }
        }

        public async Task<List<WeighbridgeRecord>> GetWeighbridgeRecords(DateTime dateFrom, DateTime dateTo)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.WeighbridgeRecords.Where(a => a.DateWeightOut != null && a.DateWeightIn != null && a.DateDeleted == null && (a.DateWeightOut > dateFrom.Date && a.DateWeightOut < dateTo)).OrderByDescending(a => a.DateWeightOut).ToListAsync();
            }
        }

        public async Task<List<vwWeighbridgeComplete>> GetWeighbridgeCompleteRecords(DateTime dateFrom, DateTime dateTo)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwWeighbridgeCompletes.OrderByDescending(a => a.DateWeightOut).ToListAsync();
            }
        }

        public async Task<WeighbridgeRecord> MarkAsDeletedAsync(int entityId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var itemToUpdate = await context.WeighbridgeRecords.FirstOrDefaultAsync(a => a.id == entityId);
                if (itemToUpdate != null)
                {
                    itemToUpdate.DateDeleted = DateTime.Now;
                    context.WeighbridgeRecords.Attach(itemToUpdate);
                    context.Entry(itemToUpdate).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
                return itemToUpdate;
            }
        }

        public async Task<ReferenceData> DeleteVehicle(int vehicleId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var itemToUpdate = await context.ReferenceDatas.FirstOrDefaultAsync(a => a.id == vehicleId);
                if (itemToUpdate != null)
                {
                    itemToUpdate.DateDeleted = DateTime.Now;
                    context.ReferenceDatas.Attach(itemToUpdate);
                    context.Entry(itemToUpdate).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
                return itemToUpdate;
            }
        }

        public async Task<ReferenceData> DeleteHaulier(int haulierId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var itemToUpdate = await context.ReferenceDatas.FirstOrDefaultAsync(a => a.id == haulierId);
                if (itemToUpdate != null)
                {
                    var linkedVehicles = await context.ReferenceDatas.Where(a => a.IntData == haulierId).ToListAsync();
                    foreach (var linkedVehicle in linkedVehicles)
                    {
                        linkedVehicle.DateDeleted = DateTime.Now;
                        context.ReferenceDatas.Attach(linkedVehicle);
                        context.Entry(itemToUpdate).State = EntityState.Modified;
                    }


                    itemToUpdate.DateDeleted = DateTime.Now;
                    context.ReferenceDatas.Attach(itemToUpdate);
                    context.Entry(itemToUpdate).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
                return itemToUpdate;
            }
        }

        public async Task<WeighbridgeRecord> GetWeighbridgeRecord(int entityId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                // return await context.WeighbridgeRecords.Include(a => a.WeighbridgeRecord_Order).Where(a => a.id == entityId).FirstOrDefaultAsync();
                return await context.WeighbridgeRecords.Where(a => a.id == entityId).FirstOrDefaultAsync();
            }
        }

        public async Task<WeighbridgeRecord> GetWeighbridgeRecord(int recordId, bool includeOrderViews, bool includeOrderItemViews)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                WeighbridgeRecord itemDb;
                itemDb = await context.WeighbridgeRecords.Where(a => a.id == recordId).FirstOrDefaultAsync();
                if (itemDb == null) return null;

                if (includeOrderViews)
                {
                    itemDb.OrderViewList = new List<vwDispatchOrder>(await context.vwDispatchOrders.Where(a => a.WeighbridgeRecordId == recordId).ToListAsync());
                }
                if (includeOrderViews)
                {
                    itemDb.OrderItemViewList = new List<vwDispatchOrderItem>(await context.vwDispatchOrderItems.Where(a => a.WeighbridgeRecordId == recordId).OrderBy(a=>a.OrderNo).ToListAsync());
                }

                return itemDb;
            }
        }

        public async Task<vwWeighbridgePreLoad> GetWeighbridgeRecordPendingViewByIdRef(string refId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwWeighbridgePreLoads.FirstOrDefaultAsync(a => a.IdRef == refId);
            }
        }

        public async Task<vwWeighbridgePending> GetWeighbridgeRecordPendingView(int entityId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwWeighbridgePendings.FirstOrDefaultAsync(a => a.id == entityId);
            }
        }

        public async Task<vwWeighbridgePreLoad> GetWeighbridgeRecordPreloadView(int entityId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwWeighbridgePreLoads.FirstOrDefaultAsync(a => a.id == entityId);
            }
        }

        public async Task<List<ReferenceData>> GetVehiclesByHaulierId(int haulierId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.ReferenceDatas.Where(a => a.IntData == haulierId && a.DateDeleted == null).ToListAsync();
            }
        }

        public async Task<WeighbridgeRecord> AddUpdateAsync(WeighbridgeRecord entity)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (entity.id == 0)
                {
                    entity.DateCreated = DateTime.Now;
                    context.WeighbridgeRecords.Add(entity);
                }
                else
                {
                    if (entity.SampleBoxWeight != null && entity.DateSampleWeightAdded == null)
                    {
                        entity.DateSampleWeightAdded = DateTime.Now;
                    }

                    if (entity.WeightIn != 0 && entity.DateWeightIn == null)
                    {
                        entity.DateWeightIn = DateTime.Now;
                    }
                    if (entity.WeightOut != 0 && entity.DateWeightOut == null)
                    {
                        entity.DateWeightOut = DateTime.Now;
                    }
                    if (entity.WeightOut == 0 && entity.DateWeightOut != null)
                    {
                        entity.DateWeightOut = null;
                    }
      

                    context.WeighbridgeRecords.Attach(entity);
                    context.Entry(entity).State = EntityState.Modified;
                }

                await context.SaveChangesAsync();
            }
            return entity;
        }

        // public async Task<bool> UpdateRecordAssigned(int recordId, List<vwOpenDispatchOrderUnnasigned> orderList)
        // {
        //     using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
        //     {
        //         foreach (var order in orderList)
        //         {
        //             var newLink = new WeighbridgeRecord_Order();
        //             newLink.WeighbridgeRecordId = recordId;
        //             newLink.OrderId = order.id;
        //             context.WeighbridgeRecord_Order.Add(newLink);
        //         }
        //         await context.SaveChangesAsync();
        //     }
        //     return true;
        // }

    }
}
