import { useEffect, useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import { getSageMissingPlan, importSageMissing, SageImportPlan } from "../../api/sage";

// Comparison > Missing in Evergreen > "Copy to Evergreen": exactly what will be created —
// every customer with the addresses it gets, and any account held back and why — before
// anything is written. Copy creates just the customers listed here. `only`: the accounts
// picked in the grid (Ctrl/Shift+click); without it, the whole Missing in Evergreen list.
export default function CopyMissingModal({ only, onClose }: { only?: string[]; onClose: (copied: boolean) => void }) {
  const [plan, setPlan] = useState<SageImportPlan | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    getSageMissingPlan()
      .then((p) => {
        if (cancelled) return;
        if (!only) return setPlan(p);
        const pick = new Set(only.map((r) => r.toUpperCase()));
        setPlan({
          customers: p.customers.filter((c) => pick.has(c.accountRef.toUpperCase())),
          skipped: p.skipped.filter((s) => pick.has(s.accountRef.toUpperCase())),
        });
      })
      .catch((err) => {
        if (cancelled) return;
        void messageDialog("Failed to work out what to copy.\n" + (err as Error).message);
        onClose(false);
      });
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const customers = plan?.customers ?? [];
  const addresses = customers.reduce((n, c) => n + c.addresses.length, 0);
  const inactive = customers.filter((c) => c.inactive).length;
  const onHold = customers.filter((c) => c.onHold).length;
  const noAddress = customers.filter((c) => c.addresses.length === 0).length;

  async function onCopy() {
    if (!plan) return;
    setBusy(true);
    try {
      const r = await importSageMissing(customers.map((c) => c.accountRef));
      const extra =
        r.noLongerMissing > 0
          ? `\n${r.noLongerMissing} were left alone: they had been added to Evergreen in the meantime.`
          : "";
      await messageDialog(
        `Copied ${r.customers} customer${r.customers === 1 ? "" : "s"} and ${r.addresses} address${r.addresses === 1 ? "" : "es"} from Sage.${extra}`,
      );
      onClose(true);
    } catch (err) {
      void messageDialog("Copy failed — nothing was created.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={
        only
          ? only.length === 1
            ? `Copy ${only[0]} to Evergreen`
            : `Copy ${only.length} selected to Evergreen`
          : "Copy all to Evergreen"
      }
      width={900}
      height={640}
      onClose={() => onClose(false)}
      footer={
        <>
          <span className="sl-imp-foot-note">
            Costing zone and sales rep are left blank — set them on the Customers page.
          </span>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={!plan || busy || customers.length === 0} onClick={() => void onCopy()}>
            {busy ? "Copying…" : `Copy ${customers.length} customer${customers.length === 1 ? "" : "s"}`}
          </button>
        </>
      }
    >
      {!plan ? (
        <div className="sl-empty">Working out what to copy…</div>
      ) : (
        <div className="sl-imp">
          <div className="sl-imp-stats">
            <Stat n={customers.length} label="customers" />
            <Stat n={addresses} label="addresses" />
            <Stat n={inactive} label="inactive in Sage" muted />
            <Stat n={onHold} label="on hold in Sage" muted />
            {noAddress > 0 && <Stat n={noAddress} label="with no address" muted />}
            {plan.skipped.length > 0 && <Stat n={plan.skipped.length} label="held back" warn />}
          </div>
          <p className="sl-imp-intro">
            Each Sage account below becomes an Evergreen customer under its Sage A/C code, with the
            addresses shown (Sage's address book, one per distinct address). Inactive accounts come
            across inactive. Nothing else in Evergreen is changed.
          </p>

          <div className="sl-imp-list">
            {customers.map((c) => (
              <div key={c.accountRef} className="sl-imp-row">
                <div className="sl-imp-ac">{c.accountRef}</div>
                <div className="sl-imp-main">
                  <div className="sl-imp-name">
                    {c.name}
                    {c.inactive && <span className="sl-badge sl-badge-inactive">Inactive</span>}
                    {c.onHold && <span className="sl-badge sl-badge-hold">On hold</span>}
                  </div>
                  {c.addresses.length === 0 ? (
                    <div className="sl-imp-addr sl-imp-none">No address in Sage</div>
                  ) : (
                    c.addresses.map((a, i) => (
                      <div key={i} className="sl-imp-addr">
                        {a.type && <span className={`sl-type sl-type-${a.type.toLowerCase()}`}>{a.type}</span>}
                        <span>{a.lines.join(", ")}</span>
                        {(a.contact || a.telephone || a.email) && (
                          <span className="sl-imp-meta">
                            {[a.contact, a.telephone, a.email].filter(Boolean).join(" · ")}
                          </span>
                        )}
                      </div>
                    ))
                  )}
                </div>
              </div>
            ))}
            {customers.length === 0 && <div className="sl-empty">Nothing to copy: every Sage account is in Evergreen.</div>}
          </div>

          {plan.skipped.length > 0 && (
            <div className="sl-imp-skipped">
              <b>Held back — not copied:</b>
              {plan.skipped.map((s) => (
                <div key={s.accountRef}>
                  {s.accountRef} {s.name && `— ${s.name}`}: {s.reason}
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}

function Stat({ n, label, muted, warn }: { n: number; label: string; muted?: boolean; warn?: boolean }) {
  return (
    <div className={`sl-imp-stat${muted ? " muted" : ""}${warn ? " warn" : ""}`}>
      <b>{n.toLocaleString()}</b>
      <span>{label}</span>
    </div>
  );
}
