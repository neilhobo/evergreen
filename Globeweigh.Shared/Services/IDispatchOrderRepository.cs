using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using DevExpress.Mvvm.Native;
using GalaSoft.MvvmLight.Ioc;
using Globeweigh.Model;
using Globeweigh.Model.Custom;

namespace Globeweigh.Shared.Services
{
    public interface IDispatchOrderRepository
    {
        Task<DispatchOrder> GetCustomerOrder(int orderId, GlobeweighEntities context);
        Task<DispatchOrder> GetDispatchOrder(int orderId);
        Task MarkDispatchOrderAsDeletedAsync(int orderId);
        Task<List<vwDispatchOrder>> GetOpenDispatchOrderViews();
        Task<List<vwDispatchOrder>> GetCompletedDispatchOrderViews(DateTime dateFrom, DateTime dateTo);
        Task<int> GetNextCustomerOrderNo(int customerId);
        Task<DispatchOrder> GetDispatchOrder(int orderId, GlobeweighEntities context);
        Task SaveCustomerOrder(DispatchOrder dispatchOrder, GlobeweighEntities context);
        Task<List<vwDispatchOrderItem>> GetDispatchOrderItemViews(int orderId);
        Task<bool> UpdateDispatchedDateComplete(int orderId, DateTime? orderCompleteDate);
        Task DeleteDispatchOrdeAsync(int orderId);
        Task<bool> EditCompletedDispatchedOrder(int orderId, DateTime? orderCompleteDate);
        // Task<bool> ApproveClientDispatchOrder(int orderId, DateTime? dateApproved);
        Task<vwDispatchOrder> GetDispatchOrderView(int orderId, bool includeOrderItems, bool includePickedItems);
        Task<int> GetNextOrderNo(bool isTransfer);
        Task<bool> DeletePickingItemFromDispatchOrder(int id);
        Task<List<vwDispatchOrderPicked>> GetDispatchOrderPickedItemsByOrderItemId(int orderItemId);
        Task<List<vwDispatchOrderItem>> GetDispatchOrderItems(int orderId);
        Task<List<DispatchOrder_Picked>> AddUpdateDispatchOrderPickedItemList(List<DispatchOrder_Picked> pickedItemList);
        Task<bool> AddPalletToDispatchOrder(DispatchOrder_Picked pickedItem);
        Task<bool> AddPalletsToDispatchOrder(List<DispatchOrder_Picked> pickedItems);
        Task<List<vwOpenDispatchOrderUnnasigned>> GetOpenDispatchOrderForLinking(int haulierId);
        //Task<List<vwDispatchOrder>> GetOpenDispatchOrderViews(int haulierId);
        Task<List<vwDispatchOrder>> GetDispatchOrderViewsByWeighbridgeRecordId(int weightBridgeRecordId);
        Task<List<vwDispatchOrder>> GetCompletedDispatchOrderViews();
        Task<List<vwWeighbridgePreLoad>> GetWeighbridgePreLoads();
        Task DeleteLoad(int loadId);
        Task<List<vwDispatchOrder>> GetOpenDispatchOrderViewsNoOnOrder(int recordId);
        Task UpdateLoadPicking(int loadId, DateTime datePickingComplete);
        Task<List<vwWeighbridgeRecordsToBeSampled>> GetWeighbridgeRecordsToBeSampled(DateTime date);

    }

    public class DispatchOrderRepository : IDispatchOrderRepository
    {
        public async Task<List<vwWeighbridgeRecordsToBeSampled>> GetWeighbridgeRecordsToBeSampled(DateTime date)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var start = date.Date;
                var end = start.AddDays(1);

                return await context.vwWeighbridgeRecordsToBeSampleds
                    .Where(a => a.DateWeightIn >= start && a.DateWeightIn < end && !a.IsOutgoing)
                    .ToListAsync();
            }
        }


        public async Task<List<vwOpenDispatchOrderUnnasigned>> GetOpenDispatchOrderForLinking(int haulierId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if(haulierId ==0) return await context.vwOpenDispatchOrderUnnasigneds.Where(a=>a.AssignedOrderCount == null || a.AssignedOrderCount == 0).ToListAsync();
                return await context.vwOpenDispatchOrderUnnasigneds.Where(a => a.HaulierId == haulierId && (a.AssignedOrderCount == null || a.AssignedOrderCount == 0)).ToListAsync();
            }
        }

        public async Task<bool> DeletePickingItemFromDispatchOrder(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var pickedItem = await context.DispatchOrder_Picked.FirstOrDefaultAsync(a => a.id == id);
                if (pickedItem != null) context.Entry(pickedItem).State = EntityState.Deleted;
                await context.SaveChangesAsync();
                return true;

            }
        }

        public async Task<List<DispatchOrder_Picked>> AddUpdateDispatchOrderPickedItemList(List<DispatchOrder_Picked> pickedItemList)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                foreach (var pickedItem in pickedItemList)
                {
                    if (pickedItem.id == 0)
                    {
                        pickedItem.CreatedById = GlobalVariables.CurrentGlobeweighUserId;
                        pickedItem.DateCreated = DateTime.Now;
                        context.DispatchOrder_Picked.Add(pickedItem);
                    }
                    else
                    {
                        context.DispatchOrder_Picked.Attach(pickedItem);
                        context.Entry(pickedItem).State = EntityState.Modified;
                    }
                }

                await context.SaveChangesAsync();

                // int stockUnitId = pickedItemList.FirstOrDefault().StockUnitId;
                //
                // var stockUnitView = await context.vwStockUnits.Where(a => a.id == stockUnitId).FirstOrDefaultAsync();
                // if (stockUnitView != null && stockUnitView.QuantityAvailable <= 0)
                // {
                //     var stockUnit = await _stockRepo.GetStockUnit(stockUnitId, context, false);
                //     if (stockUnit != null)
                //     {
                //         stockUnit.PreviousLocationId = stockUnit.LocationId;
                //         stockUnit.LocationId = (int)eOutOfStockLocation.Production;
                //         await _stockRepo.AddStockUnitLocationChangeHistory(stockUnit, context);
                //         await context.SaveChangesAsync();
                //     }
                // }

                return pickedItemList;
            }
        }

        public async Task<List<vwDispatchOrderPicked>> GetDispatchOrderPickedItemsByOrderItemId(int orderItemId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwDispatchOrderPickeds.Where(a => a.DispatchOrderItemId == orderItemId).ToListAsync();
            }
        }

        public async Task<List<vwDispatchOrderItem>> GetDispatchOrderItems(int orderId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var list = await context.vwDispatchOrderItems.Where(a => a.OrderId == orderId).ToListAsync();
                return list;
            }
        }

        public async Task<int> GetNextCustomerOrderNo(int customerId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var any = await context.DispatchOrders.Where(a => a.DateDeleted == null).AnyAsync(a => a.CustomerId == customerId);
                if (!any)
                    return 1;

                var largestOrder = await context.DispatchOrders.Where(a => a.CustomerId == customerId).MaxAsync(a => a.OrderNo) + 1;
                return largestOrder;
            }
        }

        public async Task<int> GetNextOrderNo(bool isTransfer)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (isTransfer)
                {
                    var any = await context.DispatchOrders.Where(a => a.DateDeleted == null && a.OrderTypeId == (int)eDispatchOrderType.Transfer).AnyAsync();
                    if (!any)
                        return 1;

                    var largestOrder = await context.DispatchOrders.Where(a => a.OrderTypeId == (int)eDispatchOrderType.Transfer).MaxAsync(a => a.OrderNo) + 1;
                    return largestOrder;
                }
                else
                {
                    var any = await context.DispatchOrders.Where(a => a.DateDeleted == null && a.OrderTypeId == (int)eDispatchOrderType.Customer).AnyAsync();
                    if (!any)
                        return 1;

                    var largestOrder = await context.DispatchOrders.Where(a => a.OrderTypeId == (int)eDispatchOrderType.Customer).MaxAsync(a => a.OrderNo) + 1;
                    return largestOrder;
                }

            }
        }

        // public async Task<int> GetNextOrderNo(GlobeweighEntities context)
        // {
        //     var any = await context.DispatchOrders.Where(a => a.DateDeleted == null).AnyAsync();
        //     if (!any)
        //         return 1;
        //
        //     var largestOrder = await context.DispatchOrders.MaxAsync(a => a.OrderNo) + 1;
        //     return largestOrder;
        //
        // }

        public async Task<DispatchOrder> GetCustomerOrder(int orderId, GlobeweighEntities context)
        {
            return await context.DispatchOrders
                .Include(d => d.DispatchOrderItems)
                .FirstOrDefaultAsync(p => p.id == orderId);
        }

        public async Task SaveCustomerOrder(DispatchOrder dispatchOrder, GlobeweighEntities context)
        {
            if (dispatchOrder.id == 0)
            {
                foreach (var item in dispatchOrder.DispatchOrderItems)
                {
                    if (item.id == 0)
                    {
                        item.DateCreated = DateTime.Now;
                    }
                }
                dispatchOrder.OrderNo = await GetNextOrderNo(dispatchOrder.OrderTypeId == (int)eDispatchOrderType.Transfer);
                dispatchOrder.CreatedById = GlobalVariables.CurrentGlobeweighUserId;
                context.DispatchOrders.Add(dispatchOrder);
            }

            await context.SaveChangesAsync();
        }

        public async Task<vwDispatchOrder> GetDispatchOrderView(int orderId, bool includeOrderItems, bool includePickedItems)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var order = await context.vwDispatchOrders.Where(a => a.id == orderId).FirstOrDefaultAsync();
                if (order == null) return null;
                if (includeOrderItems)
                {
                    var orderItems = await context.vwDispatchOrderItems.Where(a => a.OrderId == orderId).ToListAsync();
                    order.DispatchOrderItems = new List<vwDispatchOrderItem>(orderItems);
                }
                if (includePickedItems)
                {
                    var pickedItems = await context.vwDispatchOrderPickeds.Where(a => a.DispatchOrderId == orderId).ToListAsync();
                    order.PickedItems = new List<vwDispatchOrderPicked>(pickedItems);
                }
                return order;   
            }
        }
        
        public async Task<DispatchOrder> GetDispatchOrder(int orderId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.DispatchOrders.Where(a => a.id == orderId).FirstOrDefaultAsync();
            }
        }

        public async Task<DispatchOrder> GetDispatchOrder(int orderId, GlobeweighEntities context)
        {
            return await context.DispatchOrders
                .Include(d => d.DispatchOrderItems)
                .FirstOrDefaultAsync(p => p.id == orderId);
        }

        public async Task<List<vwDispatchOrder>> GetDispatchOrderViewsByWeighbridgeRecordId(int weightBridgeRecordId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwDispatchOrders.Where(a => a.WeighbridgeRecordId == weightBridgeRecordId).ToListAsync();
            }
        }

        public async Task<List<vwDispatchOrder>> GetOpenDispatchOrderViews()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwDispatchOrders.Where(a => a.DateDispatched == null).ToListAsync();
            }
        }

        public async Task<List<vwDispatchOrder>> GetOpenDispatchOrderViewsNoOnOrder(int recordId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if(recordId ==0) return await context.vwDispatchOrders.Where(a => a.DateDispatched == null && a.WeighbridgeRecordId == 0).ToListAsync();
                return await context.vwDispatchOrders.Where(a => a.DateDispatched == null && (a.WeighbridgeRecordId == 0 || a.WeighbridgeRecordId == recordId)).ToListAsync();
            }
        }

        public async Task<List<vwWeighbridgePreLoad>> GetWeighbridgePreLoads()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwWeighbridgePreLoads.OrderBy(a => a.CollectionDate == null).ToListAsync();
            }
        }

        // public async Task<List<vwDispatchOrder>> GetOpenDispatchOrderViews(int haulierId)
        // {
        //     using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
        //     {
        //         return await context.vwDispatchOrders.Where(a => a.DateDispatched == null && a.WeighbridgeRecordId ==0 && a.HaulierId == haulierId).ToListAsync();
        //     }
        // }

        public async Task<List<vwDispatchOrder>> GetCompletedDispatchOrderViews()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwDispatchOrders.Where(c => c.DateDispatched != null && c.DateDeleted == null).OrderByDescending(a => a.id).ToListAsync();
            }
        }

        public async Task<List<vwDispatchOrder>> GetCompletedDispatchOrderViews(DateTime dateFrom, DateTime dateTo)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwDispatchOrders.Where(c => c.DateDispatched != null && c.DateDeleted == null && c.DateDispatched > dateFrom.Date && c.DateDispatched < dateTo).OrderByDescending(a => a.id).ToListAsync();
            }
        }

        public async Task<List<vwDispatchOrderItem>> GetDispatchOrderItemViews(int orderId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return context.vwDispatchOrderItems.Where(c => c.OrderId == orderId).ToList();
            }
        }

        public async Task<bool> EditCompletedDispatchedOrder(int orderId, DateTime? orderCompleteDate)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var orderToUpdate = await context.DispatchOrders.FirstOrDefaultAsync(a => a.id == orderId);
                if (orderToUpdate != null)
                {
                    orderToUpdate.DateDispatched = orderCompleteDate;
                    context.DispatchOrders.Attach(orderToUpdate);
                    context.Entry(orderToUpdate).State = EntityState.Modified;

                    await context.SaveChangesAsync();
                }
                return true;
            }
        }

        public async Task<bool> UpdateDispatchedDateComplete(int orderId, DateTime? orderCompleteDate)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var orderToUpdate = await context.DispatchOrders.FirstOrDefaultAsync(a => a.id == orderId);
                if (orderToUpdate != null)
                {
                    var orderItems = await context.DispatchOrder_Picked.Where(a => a.DispatchOrderId == orderId).ToListAsync();

                    // foreach (var dispatchOrderItem in orderItems)
                    // {
                    //     var pallet = await context.Pallets.Where(a => a.id == dispatchOrderItem.PalletId).FirstOrDefaultAsync();
                    //
                    //     if (pallet != null)
                    //     {
                    //         if (orderCompleteDate != null)
                    //         {
                    //             await AddPalletHistory(pallet.id, pallet.LocationId, -1, Environment.MachineName, context);
                    //             pallet.LocationIdBeforePicked = pallet.LocationId;
                    //             if(orderToUpdate.OrderTypeId == (int)eDispatchOrderType.Transfer) pallet.LocationId = orderToUpdate.TransferLocationId;
                    //             else pallet.LocationId = -1;
                    //
                    //         }
                    //         else
                    //         {
                    //             await AddPalletHistory(pallet.id, pallet.LocationId, -1, Environment.MachineName, context);
                    //             pallet.LocationId = pallet.LocationIdBeforePicked;
                    //
                    //             if (orderToUpdate.OrderTypeId == (int)eDispatchOrderType.Transfer) pallet.LocationIdBeforePicked = orderToUpdate.TransferLocationId;
                    //             else pallet.LocationIdBeforePicked = -1;
                    //         }
                    //
                    //     }
                    // }


                    orderToUpdate.DateDispatched = orderCompleteDate;
                    context.DispatchOrders.Attach(orderToUpdate);
                    context.Entry(orderToUpdate).State = EntityState.Modified;

                    await context.SaveChangesAsync();
                }
                return true;
            }
        }

        public async Task MarkDispatchOrderAsDeletedAsync(int orderId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var item = await context.DispatchOrders.Where(a => a.id == orderId).FirstOrDefaultAsync();
                if (item != null)
                {
                    item.DateDeleted = DateTime.Now;
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task DeleteDispatchOrdeAsync(int orderId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var item = await context.DispatchOrders.Where(a => a.id == orderId).FirstOrDefaultAsync();
                if (item != null)
                {
                    context.DispatchOrders.Remove(item);
                    context.Entry(item).State = EntityState.Deleted;
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task DeleteLoad(int loadId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var item = await context.WeighbridgeRecords.Where(a => a.id == loadId).FirstOrDefaultAsync();
                if (item != null)
                {
                    var orders = await context.DispatchOrders.Where(a => a.WeighbridgeRecordId == loadId).ToListAsync();
                    foreach (var order in orders)
                    {
                        order.WeighbridgeRecordId = 0;
                    }
                    context.Entry(item).State = EntityState.Deleted;
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task UpdateLoadPicking(int loadId, DateTime datePickingComplete)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var item = await context.WeighbridgeRecords.Where(a => a.id == loadId).FirstOrDefaultAsync();
                if (item != null)
                {
                    item.DatePickingComplete = datePickingComplete;
    
                    await context.SaveChangesAsync();
                }
            }
        }

        public async Task<bool> AddPalletsToDispatchOrder(List<DispatchOrder_Picked> pickedItems)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                foreach (var pickedItem in pickedItems)
                {
                    pickedItem.DateCreated = DateTime.Now;
                    pickedItem.CreatedById = GlobalVariables.CurrentGlobeweighUserId;
                    context.DispatchOrder_Picked.Add(pickedItem);
                }
                await context.SaveChangesAsync();
                return true;
            }
        }

        public async Task<bool> AddPalletToDispatchOrder(DispatchOrder_Picked pickedItem)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                pickedItem.DateCreated = DateTime.Now;
                pickedItem.CreatedById = GlobalVariables.CurrentGlobeweighUserId;
                context.DispatchOrder_Picked.Add(pickedItem);
                await context.SaveChangesAsync();
                return true;
            }
        }

    }
}
