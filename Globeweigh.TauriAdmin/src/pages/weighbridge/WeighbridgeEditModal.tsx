import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import {
  createWeighbridgeRecord,
  getWeighbridgeRecord,
  updateWeighbridgeRecord,
  WeighbridgeRecordInput,
} from "../../api/weighbridge";
import { getHauliers, HaulierRow } from "../../api/hauliers";
import { getProducts, Product } from "../../api/products";
import { getCustomersSuppliers, CustomerSupplier } from "../../api/customersSuppliers";
import { toDateTimeInput } from "../../format";

interface Props {
  id: number | null; // null = add new
  onClose: (saved: boolean) => void;
}

const EMPTY: WeighbridgeRecordInput = {
  isOutgoing: false,
  haulierId: 0,
  vehicleReg: "",
  trailerNo: "",
  docketNumber: "",
  product: "",
  supplier: "",
  bogName: "",
  weightIn: 0,
  dateWeightIn: null,
  weightOut: 0,
  dateWeightOut: null,
  sampleBoxWeight: null,
  comments: "",
};

// datetime-local input value (yyyy-MM-ddTHH:mm) -> ISO for the API, "" -> null.
const fromInput = (v: string) => (v ? new Date(v).toISOString() : null);

// Add / edit a weighbridge record. Mirrors the WPF EditWeighbrideRecordView fields;
// Product/Supplier are stored as plain strings on the record, so the dropdowns are
// populated independently (no Product→Supplier→Bog cascade).
export default function WeighbridgeEditModal({ id, onClose }: Props) {
  const isNew = id == null;
  const [form, setForm] = useState<WeighbridgeRecordInput>(EMPTY);
  const [hauliers, setHauliers] = useState<HaulierRow[]>([]);
  const [products, setProducts] = useState<Product[]>([]);
  const [suppliers, setSuppliers] = useState<CustomerSupplier[]>([]);
  const [loading, setLoading] = useState(!isNew);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void (async () => {
      try {
        const [hs, ps, ss] = await Promise.all([
          getHauliers(),
          getProducts(),
          getCustomersSuppliers(2), // suppliers
        ]);
        setHauliers(hs);
        setProducts(ps);
        setSuppliers(ss);
      } catch (e) {
        void messageDialog("Failed to load lookups.\n" + (e as Error).message);
      }
    })();
  }, []);

  useEffect(() => {
    if (isNew) return;
    void (async () => {
      try {
        const r = await getWeighbridgeRecord(id!);
        setForm({
          isOutgoing: r.isOutgoing,
          haulierId: r.haulierId,
          vehicleReg: r.vehicleReg ?? "",
          trailerNo: r.trailerNo ?? "",
          docketNumber: r.docketNumber ?? "",
          product: r.product ?? "",
          supplier: r.supplier ?? "",
          bogName: r.bogName ?? "",
          weightIn: r.weightIn,
          dateWeightIn: r.dateWeightIn,
          weightOut: r.weightOut,
          dateWeightOut: r.dateWeightOut,
          sampleBoxWeight: r.sampleBoxWeight,
          comments: r.comments ?? "",
        });
      } catch (e) {
        void messageDialog("Failed to load record.\n" + (e as Error).message);
      } finally {
        setLoading(false);
      }
    })();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function set<K extends keyof WeighbridgeRecordInput>(key: K, value: WeighbridgeRecordInput[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  async function onSave() {
    if (!form.haulierId) {
      void messageDialog("Haulier is required.");
      return;
    }
    setBusy(true);
    try {
      if (isNew) await createWeighbridgeRecord(form);
      else await updateWeighbridgeRecord(id!, form);
      onClose(true);
    } catch (e) {
      void messageDialog("Failed to save.\n" + (e as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={isNew ? "Add Weighbridge Record" : "Edit Weighbridge Record"}
      width={760}
      height={550}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || loading || !form.haulierId} onClick={onSave}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      {loading ? (
        <p className="toolbar-note">Loading…</p>
      ) : (
        <div className="form-inline wb-edit">
          <div className="form-cols">
            <div className="form-col">
              <div className="form-field form-field-inline">
                <label className="form-label">Type</label>
                <select
                  className="form-input"
                  value={form.isOutgoing ? "out" : "in"}
                  onChange={(e) => set("isOutgoing", e.target.value === "out")}
                >
                  <option value="in">Incoming</option>
                  <option value="out">Outgoing</option>
                </select>
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Haulier</label>
                <select
                  className="form-input"
                  value={form.haulierId}
                  onChange={(e) => set("haulierId", Number(e.target.value))}
                >
                  <option value={0}>— Select —</option>
                  {hauliers.map((h) => (
                    <option key={h.id} value={h.id}>
                      {h.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Vehicle Reg</label>
                <input
                  className="form-input"
                  value={form.vehicleReg ?? ""}
                  onChange={(e) => set("vehicleReg", e.target.value)}
                />
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Trailer No</label>
                <input
                  className="form-input"
                  value={form.trailerNo ?? ""}
                  onChange={(e) => set("trailerNo", e.target.value)}
                />
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Docket Number</label>
                <input
                  className="form-input"
                  value={form.docketNumber ?? ""}
                  onChange={(e) => set("docketNumber", e.target.value)}
                />
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Sample Weight</label>
                <input
                  type="number"
                  step="0.01"
                  className="form-input"
                  value={form.sampleBoxWeight ?? ""}
                  onChange={(e) =>
                    set("sampleBoxWeight", e.target.value === "" ? null : Number(e.target.value))
                  }
                />
              </div>
            </div>

            <div className="form-col">
              <div className="form-field form-field-inline">
                <label className="form-label">Product</label>
                <select
                  className="form-input"
                  value={form.product ?? ""}
                  onChange={(e) => set("product", e.target.value)}
                >
                  <option value="">— None —</option>
                  {products.map((p) => (
                    <option key={p.id} value={p.description ?? ""}>
                      {p.description}
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Supplier</label>
                <select
                  className="form-input"
                  value={form.supplier ?? ""}
                  onChange={(e) => set("supplier", e.target.value)}
                >
                  <option value="">— None —</option>
                  {suppliers.map((s) => (
                    <option key={s.id} value={s.name ?? ""}>
                      {s.name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Bog</label>
                <input
                  className="form-input"
                  value={form.bogName ?? ""}
                  onChange={(e) => set("bogName", e.target.value)}
                />
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Weighed In</label>
                <input
                  type="datetime-local"
                  className="form-input"
                  value={toDateTimeInput(form.dateWeightIn)}
                  onChange={(e) => set("dateWeightIn", fromInput(e.target.value))}
                />
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Weight In (Kg)</label>
                <input
                  type="number"
                  className="form-input"
                  value={form.weightIn}
                  onChange={(e) => set("weightIn", Number(e.target.value) || 0)}
                />
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Weighed Out</label>
                <input
                  type="datetime-local"
                  className="form-input"
                  value={toDateTimeInput(form.dateWeightOut)}
                  onChange={(e) => set("dateWeightOut", fromInput(e.target.value))}
                />
              </div>
              <div className="form-field form-field-inline">
                <label className="form-label">Weight Out (Kg)</label>
                <input
                  type="number"
                  className="form-input"
                  value={form.weightOut}
                  onChange={(e) => set("weightOut", Number(e.target.value) || 0)}
                />
              </div>
            </div>
          </div>

          <div className="form-field">
            <label className="form-label">Comments</label>
            <textarea
              className="form-input form-textarea"
              style={{ height: 100 }}
              value={form.comments ?? ""}
              onChange={(e) => set("comments", e.target.value)}
            />
          </div>
        </div>
      )}
    </Modal>
  );
}
