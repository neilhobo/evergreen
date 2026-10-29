using Globeweigh.Model;
//using SageDataObject280;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Product = Globeweigh.Model.Product;


namespace Globeweigh.Shared.Services
{
    public interface IProductRepository
    {
        Task<Product> GetProductAsync(int id);
        Task<List<Product>> GetProductsAsync(int productTypeId);
        Task<Product> AddUpdateProductAsync(Product product);
        Task<bool> MarkAsDeletedAsync(int productId);
        Task<List<Product>> GetProductsAsync(eProductType productType1, eProductType productType2);
        // Task<bool> DoesProductCodeAlreadyExist(int currentProductId,string code, int customerId);
        Task<List<ProductType>> GetProductTypeList();
        Task<List<Product>> GetProductsAsync(int productTypeId, bool showDeletes);
        Task<Product> AddUpdateProductAsync(GlobeweighEntities context, Product product);
        Task<Product> GetProductAsync(GlobeweighEntities context, int id);
        Task<List<vwProduct>> GetProductViewsAsync(int productTypeId, bool showDeletes);
        Task<List<vwFinishedProductWithStock>> GetFinishedProductsWithStock();
        Task<List<vwProduct_Supplier>> GetProductSupplierList(int productId);
        Task<bool> RemoveProduct_Supplier(int id);
        Task<bool> AddProduct_Supplier(int productId, int supplierId);
        Task<bool> AddUpdateSupplier_Bog(Supplier_Bog supplierBog);
        Task<bool> RemoveSupplier_Bog(int id);
        Task<List<Supplier_Bog>> GetSupplier_BogList(int supplierId);
        Task<Supplier_Bog> GetSupplierBog(int id);
    }

    public class ProductRepository : IProductRepository
    {
        public async Task<Supplier_Bog> GetSupplierBog(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Supplier_Bog.FirstOrDefaultAsync(c => c.id == id);
            }
        }

        public async Task<List<Supplier_Bog>> GetSupplier_BogList(int supplierId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Supplier_Bog.Where(a => a.SupplierId== supplierId).OrderBy(a => a.Name).ToListAsync();
            }
        }

        public async Task<bool> AddUpdateSupplier_Bog(Supplier_Bog supplierBog)
        {

            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (supplierBog.id == 0)
                {
                    context.Supplier_Bog.Add(supplierBog);
                }
                else
                {
                    context.Supplier_Bog.Attach(supplierBog);
                    context.Entry(supplierBog).State = EntityState.Modified;
                }

                await context.SaveChangesAsync();
                return true;
            }
        }

        public async Task<bool> RemoveSupplier_Bog(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var itemDb = await context.Supplier_Bog.FirstOrDefaultAsync(a => a.id == id);
                if (itemDb != null)
                {
                    context.Entry(itemDb).State = EntityState.Deleted;
                    await context.SaveChangesAsync();
                }
                return true;
            }
        }

        public async Task<bool> AddProduct_Supplier(int productId, int supplierId)
        {
            var newProductSupplier = new Product_Supplier();
            newProductSupplier.ProductId = productId;
            newProductSupplier.SupplierId = supplierId;

            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                context.Product_Supplier.Add(newProductSupplier);
                await context.SaveChangesAsync();
                return true;
            }
        }

        public async Task<bool> RemoveProduct_Supplier(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var itemDb = await context.Product_Supplier.FirstOrDefaultAsync(a => a.id == id);
                if (itemDb != null)
                {
                    context.Entry(itemDb).State = EntityState.Deleted;
                    await context.SaveChangesAsync();
                }
                return true;
            }
        }

        public async Task<List<vwProduct_Supplier>> GetProductSupplierList(int productId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwProduct_Supplier.Where(a => a.ProductId == productId).OrderBy(a => a.Name).ToListAsync();
            }
        }

        public async Task<List<ProductType>> GetProductTypeList()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.ProductTypes.Where(a => a.DateDeleted == null).ToListAsync();
            }
        }

        // public async Task<bool> DoesProductCodeAlreadyExist(int currentProductId,string code, int customerId)
        // {
        //     using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
        //     {
        //         return await context.Products.AnyAsync(a=>a.Code.Equals(code, StringComparison.InvariantCultureIgnoreCase) && a.id != currentProductId);
        //     }
        // }


        public async Task<List<Product>> GetProductsAsync(eProductType productType1, eProductType productType2)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Products.Where(a => a.DateDeleted == null && a.ProductTypeId == (int)productType1 || a.ProductTypeId == (int)productType2).ToListAsync();
            }
        }

        public async Task<List<Product>> GetProductsAsync(int productTypeId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Products.Where(a => a.DateDeleted == null && a.ProductTypeId == productTypeId).OrderBy(a => a.CategoryId).ThenBy(a => a.id).ToListAsync();
            }
        }

        public async Task<List<vwFinishedProductWithStock>> GetFinishedProductsWithStock()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.vwFinishedProductWithStocks.OrderBy(a => a.CategoryName).ThenBy(a => a.ProductName).ToListAsync();
            }
        }

        public async Task<List<vwProduct>> GetProductViewsAsync(int productTypeId, bool showDeletes)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (productTypeId == 0)
                {
                    if (showDeletes)
                    {
                        return await context.vwProducts.Where(a => a.DateDeleted != null).OrderByDescending(a => a.DateDeleted).ToListAsync();
                    }
                    return await context.vwProducts.Where(a => a.DateDeleted == null).OrderBy(a => a.CategoryId).ThenBy(a => a.id).ToListAsync();
                }

                if (showDeletes)
                {
                    return await context.vwProducts.Where(a => a.ProductTypeId == productTypeId && a.DateDeleted != null).OrderByDescending(a => a.DateDeleted).ToListAsync();
                }
                return await context.vwProducts.Where(a => a.ProductTypeId == productTypeId && a.DateDeleted == null).OrderBy(a => a.CategoryId).ThenBy(a => a.id).ToListAsync();
            }
        }

        public async Task<List<Product>> GetProductsAsync(int productTypeId, bool showDeletes)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (productTypeId == 0)
                {
                    if (showDeletes)
                    {
                        return await context.Products.Where(a => a.DateDeleted != null).OrderByDescending(a => a.DateDeleted).ToListAsync();
                    }
                    return await context.Products.Where(a => a.DateDeleted == null).OrderBy(a => a.CategoryId).ThenBy(a => a.Description).ToListAsync();
                }

                if (showDeletes)
                {
                    return await context.Products.Where(a => a.ProductTypeId == productTypeId && a.DateDeleted != null).OrderByDescending(a => a.DateDeleted).ToListAsync();
                }
                return await context.Products.Where(a => a.ProductTypeId == productTypeId && a.DateDeleted == null).OrderBy(a => a.CategoryId).ThenBy(a => a.Description).ToListAsync();
            }
        }

        public async Task<Product> GetProductAsync(int id)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Products.FirstOrDefaultAsync(c => c.id == id);
            }
        }

        public async Task<Product> AddUpdateProductAsync(Product Product)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                if (Product.id == 0)
                {
                    Product.DateCreated = DateTime.Now;
                    context.Products.Add(Product);
                }
                else
                {
                    context.Products.Attach(Product);
                    context.Entry(Product).State = EntityState.Modified;
                }
                await context.SaveChangesAsync();
                return Product;
            }
        }

        public async Task<Product> GetProductAsync(GlobeweighEntities context, int id)
        {
            return await context.Products.FirstOrDefaultAsync(c => c.id == id);
        }

        public async Task<Product> AddUpdateProductAsync(GlobeweighEntities context, Product product)
        {
            if (product.id == 0)
            {
                product.DateCreated = DateTime.Now;
                product.CreatedById = GlobalVariables.CurrentGlobeweighUserId;
                context.Products.Add(product);
            }



            await context.SaveChangesAsync();
            return product;
        }

        public async Task<bool> MarkAsDeletedAsync(int ProductId)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                var product = await context.Products.FirstOrDefaultAsync(a => a.id == ProductId);
                if (product != null)
                {
                    if (product.DateDeleted == null) product.DateDeleted = DateTime.Now;
                    else
                    {
                        product.DateDeleted = null;
                    }
                    context.Products.Attach(product);
                    context.Entry(product).State = EntityState.Modified;
                    await context.SaveChangesAsync();
                }
                return true;
            }
        }

    }
}
