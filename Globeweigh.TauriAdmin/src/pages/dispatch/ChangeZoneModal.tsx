import { useState } from "react";
import Modal from "../../components/Modal";
import DataTable, { Column } from "../../components/DataTable";
import { ZoneChoice } from "../../api/costing";

interface Props {
  productName: string;
  choices: ZoneChoice[];
  customerZoneId: number;
  // The zone the line is priced from now (the customer's when it has not been changed).
  currentZoneId: number;
  onPick: (choice: ZoneChoice) => void;
  onClose: () => void;
}

// A sales rep's Change beside Costing Zone in Add Order Item: every costing zone with the
// product's price in it. Picking one prices this line from it, for this order only — the
// customer's own zone is not touched. A zone with no price for the product cannot be picked.
export default function ChangeZoneModal({ productName, choices, customerZoneId, currentZoneId, onPick, onClose }: Props) {
  const [selected, setSelected] = useState<ZoneChoice | null>(
    choices.find((c) => c.costingZoneId === currentZoneId) ?? null,
  );

  function pick(c: ZoneChoice | null) {
    if (!c || c.price == null) return;
    onPick(c);
    onClose();
  }

  const columns: Column<ZoneChoice>[] = [
    {
      key: "zone",
      header: "Costing Zone",
      render: (c) => (
        <span style={c.price == null ? { color: "var(--text-secondary)" } : undefined}>
          {c.description ?? `Zone #${c.costingZoneId}`}
          {c.secondaryDescription ? ` (${c.secondaryDescription})` : ""}
          {c.costingZoneId === customerZoneId && <span className="zone-choice-tag">Customer's zone</span>}
        </span>
      ),
    },
    {
      key: "price",
      header: "Price",
      width: 90,
      align: "right",
      render: (c) => (c.price != null ? c.price.toFixed(2) : <span style={{ color: "var(--text-secondary)" }}>No price</span>),
    },
  ];

  return (
    <Modal
      title={`Change Costing Zone — ${productName}`}
      width={520}
      height={560}
      onClose={onClose}
      footer={
        <>
          <button className="toolbar-btn" onClick={onClose}>
            Cancel
          </button>
          <button className="btn-blue" disabled={!selected || selected.price == null} onClick={() => pick(selected)}>
            Use this zone
          </button>
        </>
      }
    >
      <p style={{ margin: "0 0 8px", fontSize: 12, color: "var(--text-secondary)" }}>
        Prices this line from the zone you pick, on this order only. The customer's costing zone stays as it is.
      </p>
      <DataTable
        columns={columns}
        rows={choices}
        rowKey={(c) => c.costingZoneId}
        selectedKey={selected?.costingZoneId ?? null}
        onSelect={setSelected}
        onDoubleClick={pick}
        emptyText="No costing zones"
      />
    </Modal>
  );
}
