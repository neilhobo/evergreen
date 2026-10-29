import PalletLabel, { SAMPLE_LABEL_DATA } from "../../components/PalletLabel";

// Admin "Customer Label" — the master pallet label template (15 × 10 cm landscape),
// shown with sample data. Every customer prints this template; per-customer
// configuration will be layered on top of it from the customer setup screen.
export default function CustomerLabelPanel() {
  return (
    <div className="po-main">
      <div style={{ padding: 18, overflow: "auto" }}>
        <PalletLabel data={SAMPLE_LABEL_DATA} />
        <p style={{ color: "#64748b", fontSize: 13, marginTop: 14, lineHeight: 1.5, maxWidth: 620 }}>
          The master customer label template, shown at print size (15 × 10 cm) with sample data.
          All customers currently use this template; customer-specific configuration will be added
          from the customer setup screen.
        </p>
      </div>
    </div>
  );
}
