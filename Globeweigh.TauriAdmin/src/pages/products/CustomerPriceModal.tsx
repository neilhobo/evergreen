import { useEffect, useMemo, useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import Highlight from "../../components/Highlight";
import { messageDialog } from "../../components/confirmDialog";
import { addCustomerPrice, CostingZonePrice, CustomerPrice, updateCustomerPrice } from "../../api/costing";
import { CustomerSupplier, getCustomersSuppliers } from "../../api/customersSuppliers";
import { Product } from "../../api/products";
import { fmtMoney, priceDiff } from "./pricing";
import styles from "./ProductDetail.module.css";

const CUSTOMER_TYPE_ID = 1;

interface Props {
  product: Product;
  // null = Add Customer Pricing: pick the customer first.
  existing: CustomerPrice | null;
  // The product's zone prices by zone id, so the dialog can show what the customer pays today.
  zonePrices: Map<number, CostingZonePrice>;
  // Customers that already have a price for the product — not offered again.
  taken: Set<number>;
  onClose: (saved: boolean) => void;
}

// Add Customer Pricing (pick a customer, enter a price) or change an existing customer's price.
// The customer's costing zone and its price for the product are shown beside the box, since a
// customer price is almost always agreed as a move off that.
export default function CustomerPriceModal({ product, existing, zonePrices, taken, onClose }: Props) {
  const isNew = existing === null;
  const [customers, setCustomers] = useState<CustomerSupplier[]>([]);
  const [loading, setLoading] = useState(isNew);
  const [search, setSearch] = useState("");
  const [picked, setPicked] = useState<CustomerSupplier | null>(null);
  const [price, setPrice] = useState(existing ? existing.price.toFixed(2) : "");
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!isNew) return;
    getCustomersSuppliers(CUSTOMER_TYPE_ID)
      .then((rows) => setCustomers(rows.filter((c) => !taken.has(c.id))))
      .catch((err) => void messageDialog("Failed to load customers.\n" + (err as Error).message))
      .finally(() => setLoading(false));
  }, [isNew, taken]);

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    if (!term) return customers;
    return customers.filter((c) =>
      [c.name, c.code, c.costingZone].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [customers, term]);

  // The customer in question and their zone, whichever way the dialog was opened.
  const zoneId = existing ? existing.costingZoneId : picked?.costingZoneId ?? 0;
  const zoneName = existing ? existing.costingZone : picked?.costingZone ?? null;
  const zonePrice = zoneId > 0 ? zonePrices.get(zoneId) ?? null : null;
  const isEuro = zonePrice?.isEuro ?? existing?.isEuro ?? null;
  const parsed = price.trim() === "" ? NaN : Number(price);
  const diff =
    zonePrice && !zonePrice.na && !Number.isNaN(parsed) ? priceDiff(parsed, zonePrice.price, isEuro) : null;

  async function onSave() {
    if (isNew && !picked) {
      void messageDialog("Pick a customer.");
      return;
    }
    if (Number.isNaN(parsed) || parsed < 0) {
      void messageDialog("Enter a price (a number, 0 or more).");
      return;
    }
    setBusy(true);
    try {
      if (existing) await updateCustomerPrice(existing.id, parsed);
      else await addCustomerPrice(product.id, picked!.id, parsed);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  const columns: Column<CustomerSupplier>[] = [
    { key: "code", header: "Code", width: 90, render: (r) => <Highlight text={r.code} term={term} /> },
    { key: "name", header: "Customer", render: (r) => <Highlight text={r.name} term={term} /> },
    { key: "costingZone", header: "Costing Zone", width: 150, render: (r) => <Highlight text={r.costingZone} term={term} /> },
    {
      key: "zonePrice",
      header: "Zone Price",
      width: 90,
      align: "right",
      render: (r) => {
        const z = zonePrices.get(r.costingZoneId);
        return z ? (z.na ? "N/A" : fmtMoney(z.price, z.isEuro)) : "";
      },
    },
  ];

  const customerName = existing ? existing.customerName : picked?.name;

  return (
    <Modal
      title={isNew ? `Add Customer Pricing — ${product.description ?? ""}` : `Customer Price — ${product.description ?? ""}`}
      width={isNew ? 680 : 460}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || (isNew && !picked)} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      {isNew && (
        <>
          <div className="toolbar">
            <input
              className="form-input toolbar-search"
              placeholder="Search customers…"
              value={search}
              autoFocus
              onChange={(e) => setSearch(e.target.value)}
            />
            {loading && <span className="toolbar-note">Loading…</span>}
          </div>
          <DataTable
            columns={columns}
            rows={filtered}
            rowKey={(r) => r.id}
            selectedKey={picked?.id ?? null}
            onSelect={setPicked}
            maxHeight={300}
            emptyText={loading ? "Loading…" : "No customers"}
          />
        </>
      )}

      <div className={styles.ref}>
        <span className={styles.refLabel}>Customer</span>
        <span>{customerName ?? <span className={styles.diffSame}>Pick a customer above</span>}</span>
        <span className={styles.refLabel}>Costing Zone</span>
        <span>{zoneId > 0 ? zoneName : customerName ? <span className={styles.diffBelow}>None set</span> : "—"}</span>
        <span className={styles.refLabel}>Zone Price</span>
        <span>
          {zonePrice ? (zonePrice.na ? "N/A in this zone" : fmtMoney(zonePrice.price, zonePrice.isEuro)) : "—"}
        </span>
      </div>

      <div className={styles.priceRow}>
        <label className="form-label" htmlFor="customer-price" style={{ width: 110 }}>
          Customer Price
        </label>
        <input
          id="customer-price"
          className="form-input no-spin"
          type="number"
          step="0.01"
          min="0"
          style={{ width: 110 }}
          value={price}
          autoFocus={!isNew}
          placeholder={zonePrice && !zonePrice.na ? zonePrice.price.toFixed(2) : ""}
          onChange={(e) => setPrice(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter") void onSave();
          }}
        />
        {diff && <span className={diff.className}>{diff.text} vs zone</span>}
      </div>
    </Modal>
  );
}
