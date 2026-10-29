import { ReactNode, useEffect, useMemo, useState } from "react";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import Modal from "../../components/Modal";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import {
  addProductSupplier,
  createProduct,
  createSupplierBog,
  deleteProduct,
  deleteSupplierBog,
  getProducts,
  getProductSuppliers,
  getSupplierBogs,
  Product,
  ProductSupplierRef,
  removeProductSupplier,
  SupplierBogRef,
  updateProduct,
  updateSupplierBog,
  RAW_MATERIAL_TYPE_ID,
} from "../../api/products";
import { CustomerSupplier, getCustomersSuppliers } from "../../api/customersSuppliers";

// eEntityType.Supplier — the supplier picker lists supplier-type entities.
const SUPPLIER_ENTITY_TYPE_ID = 2;

// "Raw Materials" — mirrors the WPF RawProductListView: three cascading panels,
// Product -> Supplier(s) -> Bog List. Selecting a product shows its suppliers;
// selecting a supplier shows that supplier's bogs.
export default function RawMaterialsPanel({ typePicker }: { typePicker?: ReactNode }) {
  const [products, setProducts] = useState<Product[]>([]);
  const [selectedProduct, setSelectedProduct] = useState<Product | null>(null);
  const [suppliers, setSuppliers] = useState<ProductSupplierRef[]>([]);
  const [selectedSupplier, setSelectedSupplier] = useState<ProductSupplierRef | null>(null);
  const [bogs, setBogs] = useState<SupplierBogRef[]>([]);
  const [selectedBog, setSelectedBog] = useState<SupplierBogRef | null>(null);
  const [loading, setLoading] = useState(false);

  const [editingProduct, setEditingProduct] = useState<Product | null | "new">(null);
  const [addingSupplier, setAddingSupplier] = useState(false);
  const [editingBog, setEditingBog] = useState<SupplierBogRef | null | "new">(null);

  async function refreshProducts() {
    setLoading(true);
    try {
      const data = await getProducts(RAW_MATERIAL_TYPE_ID);
      // WPF GetProductViewsAsync order: CategoryId then id (creation order), not alphabetical.
      data.sort((a, b) => a.categoryId - b.categoryId || a.id - b.id);
      setProducts(data);
      setSelectedProduct((cur) => (cur ? data.find((r) => r.id === cur.id) ?? null : null));
    } catch (err) {
      void messageDialog("Failed to load products.\n" + (err as Error).message);
    } finally {
      setLoading(false);
    }
  }

  async function refreshSuppliers(productId: number) {
    try {
      setSuppliers(await getProductSuppliers(productId));
    } catch (err) {
      setSuppliers([]);
      void messageDialog("Failed to load suppliers.\n" + (err as Error).message);
    }
  }

  async function refreshBogs(supplierId: number) {
    try {
      setBogs(await getSupplierBogs(supplierId));
    } catch (err) {
      setBogs([]);
      void messageDialog("Failed to load bogs.\n" + (err as Error).message);
    }
  }

  useEffect(() => {
    void refreshProducts();
  }, []);

  // Cascade: product selection drives the supplier list, supplier drives the bog list.
  useEffect(() => {
    setSelectedSupplier(null);
    setSuppliers([]);
    if (selectedProduct) void refreshSuppliers(selectedProduct.id);
  }, [selectedProduct?.id]);

  useEffect(() => {
    setSelectedBog(null);
    setBogs([]);
    if (selectedSupplier) void refreshBogs(selectedSupplier.supplierId);
  }, [selectedSupplier?.id]);

  async function onDeleteProduct() {
    if (!selectedProduct) return;
    if (!(await confirmDialog(`Delete product "${selectedProduct.description}"?`))) return;
    try {
      await deleteProduct(selectedProduct.id);
      setSelectedProduct(null);
      await refreshProducts();
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
    }
  }

  async function onRemoveSupplier() {
    if (!selectedProduct || !selectedSupplier) return;
    if (!(await confirmDialog(`Remove supplier "${selectedSupplier.name}"?`))) return;
    try {
      await removeProductSupplier(selectedSupplier.id);
      setSelectedSupplier(null);
      await refreshSuppliers(selectedProduct.id);
    } catch (err) {
      void messageDialog("Failed to remove supplier.\n" + (err as Error).message);
    }
  }

  async function onDeleteBog() {
    if (!selectedSupplier || !selectedBog) return;
    if (!(await confirmDialog(`Delete bog "${selectedBog.name}"?`))) return;
    try {
      await deleteSupplierBog(selectedBog.id);
      setSelectedBog(null);
      await refreshBogs(selectedSupplier.supplierId);
    } catch (err) {
      void messageDialog("Failed to delete bog.\n" + (err as Error).message);
    }
  }

  const productColumns: Column<Product>[] = [
    { key: "description", header: "Description", render: (r) => r.description },
  ];
  const supplierColumns: Column<ProductSupplierRef>[] = [
    { key: "name", header: "Name", render: (r) => r.name },
  ];
  const bogColumns: Column<SupplierBogRef>[] = [
    { key: "name", header: "Name", render: (r) => r.name },
  ];

  return (
    <div className="raw-cascade">
      <div className="raw-col">
        <div className="toolbar">
          {typePicker}
          <span className="raw-col-title">Product</span>
          {loading && <span className="toolbar-note">Loading…</span>}
          <div className="toolbar-right">
            <button className="btn-primary" onClick={() => setEditingProduct("new")}>
              Add
            </button>
            <button
              className="toolbar-btn"
              disabled={!selectedProduct}
              onClick={() => setEditingProduct(selectedProduct)}
            >
              Edit
            </button>
            <button className="toolbar-btn" disabled={!selectedProduct} onClick={onDeleteProduct}>
              Delete
            </button>
          </div>
        </div>
        <DataTable
          columns={productColumns}
          rows={products}
          rowKey={(r) => r.id}
          selectedKey={selectedProduct?.id ?? null}
          onSelect={setSelectedProduct}
          onDoubleClick={(r) => setEditingProduct(r)}
          emptyText={loading ? "Loading…" : "No products"}
        />
      </div>

      {selectedProduct && (
        <div className="raw-col">
          <div className="toolbar">
            <span className="raw-col-title">Supplier</span>
            <div className="toolbar-right">
              <button className="btn-primary" onClick={() => setAddingSupplier(true)}>
                Add
              </button>
              <button className="toolbar-btn" disabled={!selectedSupplier} onClick={onRemoveSupplier}>
                Delete
              </button>
            </div>
          </div>
          <DataTable
            columns={supplierColumns}
            rows={suppliers}
            rowKey={(r) => r.id}
            selectedKey={selectedSupplier?.id ?? null}
            onSelect={setSelectedSupplier}
            emptyText="No suppliers"
          />
        </div>
      )}

      {selectedProduct && selectedSupplier && (
        <div className="raw-col">
          <div className="toolbar">
            <span className="raw-col-title">Bog List</span>
            <div className="toolbar-right">
              <button className="btn-primary" onClick={() => setEditingBog("new")}>
                Add
              </button>
              <button className="toolbar-btn" disabled={!selectedBog} onClick={() => setEditingBog(selectedBog)}>
                Edit
              </button>
              <button className="toolbar-btn" disabled={!selectedBog} onClick={onDeleteBog}>
                Delete
              </button>
            </div>
          </div>
          <DataTable
            columns={bogColumns}
            rows={bogs}
            rowKey={(r) => r.id}
            selectedKey={selectedBog?.id ?? null}
            onSelect={setSelectedBog}
            onDoubleClick={(r) => setEditingBog(r)}
            emptyText="No bogs"
          />
        </div>
      )}

      {editingProduct !== null && (
        <RawProductModal
          existing={editingProduct === "new" ? null : editingProduct}
          allProducts={products}
          onClose={(saved) => {
            setEditingProduct(null);
            if (saved) void refreshProducts();
          }}
        />
      )}

      {addingSupplier && selectedProduct && (
        <AddSupplierModal
          product={selectedProduct}
          linkedSupplierIds={suppliers.map((s) => s.supplierId)}
          onClose={(saved) => {
            setAddingSupplier(false);
            if (saved) void refreshSuppliers(selectedProduct.id);
          }}
        />
      )}

      {editingBog !== null && selectedSupplier && (
        <BogModal
          supplier={selectedSupplier}
          existing={editingBog === "new" ? null : editingBog}
          onClose={(saved) => {
            setEditingBog(null);
            if (saved) void refreshBogs(selectedSupplier.supplierId);
          }}
        />
      )}
    </div>
  );
}

// Add/edit a raw-material product — mirrors the WPF AddRawProductView (Description only).
function RawProductModal({
  existing,
  allProducts,
  onClose,
}: {
  existing: Product | null;
  allProducts: Product[];
  onClose: (saved: boolean) => void;
}) {
  const isNew = existing === null;
  const [description, setDescription] = useState(existing?.description ?? "");
  const [busy, setBusy] = useState(false);

  const trimmedName = description.trim().toLowerCase();
  const nameExists =
    !!trimmedName &&
    allProducts.some(
      (p) => p.id !== existing?.id && p.description?.trim().toLowerCase() === trimmedName,
    );

  async function onSave() {
    if (!description.trim() || nameExists) return;
    setBusy(true);
    try {
      if (isNew) {
        await createProduct({
          description,
          skuCode: null,
          gtin: null,
          size: null,
          upp: 0,
          commodityCode: null,
          productTypeId: RAW_MATERIAL_TYPE_ID,
          categoryId: 0,
          posId: null,
          isBulkBag: false,
          averageWeight: null,
          bagWeightGrams: null,
          soldBy: "Pallet",
        });
      } else {
        // Only Description is editable; carry the row's other values through unchanged.
        await updateProduct(existing!.id, {
          description,
          skuCode: existing!.skuCode,
          gtin: existing!.gtin,
          size: existing!.size,
          upp: existing!.upp,
          commodityCode: existing!.commodityCode,
          productTypeId: existing!.productTypeId,
          categoryId: existing!.categoryId,
          posId: existing!.posId,
          isBulkBag: existing!.isBulkBag,
          averageWeight: existing!.averageWeight,
          bagWeightGrams: existing!.bagWeightGrams,
          soldBy: existing!.soldBy,
        });
      }
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={isNew ? "Add New Product" : "Edit Product"}
      width={480}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button
            className="btn-blue"
            disabled={busy || !description.trim() || nameExists}
            onClick={onSave}
          >
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-field form-field-inline">
        <label className="form-label">Product Description *</label>
        <input
          className="form-input"
          value={description}
          autoFocus
          onChange={(e) => setDescription(e.target.value)}
        />
      </div>
      {nameExists && <div className="form-error">A product with this name already exists.</div>}
    </Modal>
  );
}

// Link a supplier to a raw-material product — mirrors the WPF AddRawSupplierView
// (pick from the supplier list, Save creates the Product_Supplier link).
// Suppliers already linked to the product are left out of the picker.
function AddSupplierModal({
  product,
  linkedSupplierIds,
  onClose,
}: {
  product: Product;
  linkedSupplierIds: number[];
  onClose: (saved: boolean) => void;
}) {
  const [suppliers, setSuppliers] = useState<CustomerSupplier[]>([]);
  const [selected, setSelected] = useState<CustomerSupplier | null>(null);
  const [search, setSearch] = useState("");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        setSuppliers(await getCustomersSuppliers(SUPPLIER_ENTITY_TYPE_ID));
      } catch (err) {
        void messageDialog("Failed to load suppliers.\n" + (err as Error).message);
      }
    })();
  }, []);

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    const available = suppliers.filter((s) => !linkedSupplierIds.includes(s.id));
    if (!term) return available;
    return available.filter((s) => s.name?.toLowerCase().includes(term));
  }, [suppliers, linkedSupplierIds, term]);

  const columns: Column<CustomerSupplier>[] = [
    { key: "name", header: "Name", render: (r) => <Highlight text={r.name} term={term} /> },
  ];

  async function onSave(pick: CustomerSupplier | null = selected) {
    if (!pick) return;
    setBusy(true);
    try {
      await addProductSupplier(product.id, pick.id);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to add supplier.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title="Add Supplier"
      width={420}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !selected} onClick={() => void onSave()}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-field form-field-inline">
        <label className="form-label">Product</label>
        <span>{product.description}</span>
      </div>
      <input
        className="form-input"
        placeholder="Search suppliers…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        style={{ marginBottom: 8 }}
      />
      <div className="raw-supplier-pick">
        <DataTable
          columns={columns}
          rows={filtered}
          rowKey={(r) => r.id}
          selectedKey={selected?.id ?? null}
          onSelect={setSelected}
          onDoubleClick={(r) => {
            setSelected(r);
            void onSave(r);
          }}
          emptyText="No suppliers"
        />
      </div>
    </Modal>
  );
}

// Add/edit a supplier bog — mirrors the WPF AddBogView (Bog Name only).
function BogModal({
  supplier,
  existing,
  onClose,
}: {
  supplier: ProductSupplierRef;
  existing: SupplierBogRef | null;
  onClose: (saved: boolean) => void;
}) {
  const isNew = existing === null;
  const [name, setName] = useState(existing?.name ?? "");
  const [busy, setBusy] = useState(false);

  async function onSave() {
    if (!name.trim()) return;
    setBusy(true);
    try {
      if (isNew) await createSupplierBog(supplier.supplierId, name.trim());
      else await updateSupplierBog(existing!.id, name.trim());
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={isNew ? "Add Bog" : "Edit Bog"}
      width={420}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !name.trim()} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div className="form-field form-field-inline">
        <label className="form-label">Bog Name *</label>
        <input className="form-input" value={name} autoFocus onChange={(e) => setName(e.target.value)} />
      </div>
    </Modal>
  );
}
