import { useEffect, useMemo, useRef, useState } from "react";
import Modal from "../../components/Modal";
import Highlight from "../../components/Highlight";
import { confirmDialog, messageDialog } from "../../components/confirmDialog";
import { createSageReport, deleteSageReport, getMemberGroups, SageReport, updateSageReport } from "../../api/reports";
import { getSageCustomers, SageCustomerRow } from "../../api/sage";
import "../dispatch/Dispatch.css";

export type ReportEditorResult = { kind: "saved"; report: SageReport } | { kind: "deleted" } | null;

interface Props {
  // null = a new report.
  report: SageReport | null;
  onClose: (result: ReportEditorResult) => void;
}

interface Member {
  key: number; // local, for React
  id: number; // 0 = new
  name: string;
  refs: string[];
}

// The list stops here; the search narrows it. Every account is still reachable.
const SHOWN = 250;

let nextKey = 1;

// A Sage financial report's name and members. Accounts are picked from the Sage customer
// mirror (Sage Link) rather than typed, so every ref is one Sage knows: tick accounts on the
// left and add them as separate members, together as one member (a co-op's branches), or
// into the selected member on the right. An account already on the report shows which member
// has it, and adding it again moves it. Refs that pre-date the picker and are not in the
// mirror stay, flagged, until removed.
export default function ReportEditorModal({ report, onClose }: Props) {
  const [name, setName] = useState(report?.name ?? "");
  const [members, setMembers] = useState<Member[]>([]);
  const [customers, setCustomers] = useState<SageCustomerRow[] | null>(null);
  const [mirrorError, setMirrorError] = useState<string | null>(null);
  const [loading, setLoading] = useState(!!report);
  const [busy, setBusy] = useState(false);
  const [search, setSearch] = useState("");
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [target, setTarget] = useState<number | null>(null);
  const nameRefs = useRef(new Map<number, HTMLInputElement>());
  const [focusKey, setFocusKey] = useState<number | null>(null);

  useEffect(() => {
    let alive = true;
    if (report) {
      getMemberGroups(report.key)
        .then((groups) => {
          if (!alive) return;
          setMembers(
            groups.map((g) => ({
              key: nextKey++,
              id: g.id,
              name: g.name,
              refs: g.accountRefs.split(",").map((a) => a.trim().toUpperCase()).filter(Boolean),
            })),
          );
        })
        .catch((err) => void messageDialog("Failed to load the members.\n" + (err as Error).message))
        .finally(() => alive && setLoading(false));
    }
    getSageCustomers()
      .then((c) => alive && setCustomers([...c.customers].sort((a, b) => a.accountRef.localeCompare(b.accountRef))))
      .catch((err) => {
        if (!alive) return;
        setCustomers([]);
        setMirrorError((err as Error).message);
      });
    return () => {
      alive = false;
    };
  }, [report]);

  // A member added by the buttons gets its name box focused, ready to rename.
  useEffect(() => {
    if (focusKey == null) return;
    const el = nameRefs.current.get(focusKey);
    if (el) {
      el.focus();
      el.select();
    }
    setFocusKey(null);
  }, [focusKey, members]);

  const byRef = useMemo(() => new Map((customers ?? []).map((c) => [c.accountRef.toUpperCase(), c])), [customers]);
  const memberOf = useMemo(() => {
    const m = new Map<string, Member>();
    for (const mem of members) for (const r of mem.refs) m.set(r, mem);
    return m;
  }, [members]);

  const term = search.trim().toLowerCase();
  const filtered = useMemo(() => {
    const all = customers ?? [];
    if (!term) return all;
    return all.filter((c) =>
      [c.accountRef, c.name, c.address1, c.address3, c.address4, c.address5].some((v) => v?.toLowerCase().includes(term)),
    );
  }, [customers, term]);
  const shown = filtered.slice(0, SHOWN);

  const targetMember = members.find((m) => m.key === target) ?? null;
  const pickedList = [...picked];
  const moving = pickedList.filter((r) => memberOf.has(r)).length;

  function togglePick(ref: string) {
    setPicked((p) => {
      const n = new Set(p);
      if (n.has(ref)) n.delete(ref);
      else n.add(ref);
      return n;
    });
  }

  function pickAllShown() {
    setPicked((p) => {
      const n = new Set(p);
      for (const c of shown) n.add(c.accountRef.toUpperCase());
      return n;
    });
  }

  const customerName = (ref: string) => byRef.get(ref)?.name?.trim() || ref;

  // Takes refs off whichever members have them (dropping a member left with none, unless it is
  // `keep`, the one being added to), then lets `add` put them where they belong.
  function place(refs: string[], add: (rest: Member[]) => Member[], keep?: number) {
    setMembers((ms) => {
      const taken = new Set(refs);
      const rest = ms
        .map((m) => (m.key === keep ? m : { ...m, refs: m.refs.filter((r) => !taken.has(r)) }))
        .filter((m) => m.refs.length > 0 || m.key === keep);
      return add(rest);
    });
    setPicked(new Set());
  }

  function addSeparately() {
    const added = pickedList.map((r) => ({ key: nextKey++, id: 0, name: customerName(r).slice(0, 60), refs: [r] }));
    place(pickedList, (rest) => [...rest, ...added]);
    if (added.length === 1) setFocusKey(added[0].key);
  }

  function addTogether() {
    const m: Member = { key: nextKey++, id: 0, name: customerName(pickedList[0]).slice(0, 60), refs: pickedList };
    place(pickedList, (rest) => [...rest, m]);
    setTarget(m.key);
    setFocusKey(m.key);
  }

  function addToTarget() {
    if (!targetMember) return;
    const refs = pickedList.filter((r) => !targetMember.refs.includes(r));
    place(refs, (rest) => rest.map((m) => (m.key === targetMember.key ? { ...m, refs: [...m.refs, ...refs] } : m)), targetMember.key);
  }

  function updateMember(key: number, patch: Partial<Member>) {
    setMembers((ms) => ms.map((m) => (m.key === key ? { ...m, ...patch } : m)));
  }

  function removeRef(key: number, ref: string) {
    setMembers((ms) => ms.map((m) => (m.key === key ? { ...m, refs: m.refs.filter((r) => r !== ref) } : m)));
  }

  function move(key: number, dir: -1 | 1) {
    setMembers((ms) => {
      const i = ms.findIndex((m) => m.key === key);
      const j = i + dir;
      if (i < 0 || j < 0 || j >= ms.length) return ms;
      const next = [...ms];
      [next[i], next[j]] = [next[j], next[i]];
      return next;
    });
  }

  function validate(): string | null {
    if (!name.trim()) return "Give the report a name.";
    if (members.length === 0) return "Add at least one member.";
    for (const m of members) {
      if (!m.name.trim()) return "Every member needs a name.";
      if (m.refs.length === 0) return `${m.name.trim()} has no Sage accounts.`;
    }
    const names = members.map((m) => m.name.trim().toLowerCase());
    if (new Set(names).size !== names.length) return "Two members have the same name.";
    return null;
  }
  const error = validate();

  async function save() {
    if (error) {
      void messageDialog(error);
      return;
    }
    setBusy(true);
    const input = {
      name: name.trim(),
      groups: members.map((m) => ({ id: m.id, name: m.name.trim(), accountRefs: m.refs.join(",") })),
    };
    try {
      const saved = report ? await updateSageReport(report.key, input) : await createSageReport(input);
      onClose({ kind: "saved", report: saved });
    } catch (err) {
      void messageDialog("Failed to save.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  async function remove() {
    if (!report) return;
    if (!(await confirmDialog(`Delete the report "${report.name}"?\n\nOnly the report and its members go; the Sage figures stay.`))) return;
    setBusy(true);
    try {
      await deleteSageReport(report.key);
      onClose({ kind: "deleted" });
    } catch (err) {
      void messageDialog("Failed to delete.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  const accountCount = members.reduce((s, m) => s + m.refs.length, 0);

  return (
    <Modal
      title={report ? `Edit report — ${report.name}` : "New Sage financial report"}
      width={1120}
      onClose={() => onClose(null)}
      footer={
        <>
          {report && (
            <button className="toolbar-btn rp-ed-delete" onClick={() => void remove()} disabled={busy}>
              Delete report
            </button>
          )}
          <button className="toolbar-btn" onClick={() => onClose(null)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || loading || !!error} onClick={() => void save()} title={error ?? undefined}>
            {busy ? "Saving…" : report ? "Save" : "Create report"}
          </button>
        </>
      }
    >
      <div className="ld-panel rp-ed">
        <label className="rp-ed-name">
          <span>Report name</span>
          <input
            className="ld-input"
            value={name}
            maxLength={80}
            onChange={(e) => setName(e.target.value)}
            placeholder="e.g. Allied Merchant Buying Group"
            autoFocus={!report}
          />
        </label>

        <div className="rp-ed-cols">
          {/* ---- Sage customers ---- */}
          <section className="rp-ed-pane">
            <header className="rp-ed-head">
              <h3>Sage customers</h3>
              <span>{customers ? `${filtered.length.toLocaleString()} of ${customers.length.toLocaleString()}` : ""}</span>
            </header>
            <input
              className="ld-input rp-ed-search"
              placeholder="Search account, name, town or postcode…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <div className="rp-ed-list">
              {customers === null ? (
                <div className="rp-empty">Loading Sage customers…</div>
              ) : customers.length === 0 ? (
                <div className="rp-empty">
                  {mirrorError
                    ? `Could not load the Sage customers: ${mirrorError}`
                    : "No Sage customers yet — press Refresh from Sage on Admin › Sage Link first."}
                </div>
              ) : filtered.length === 0 ? (
                <div className="rp-empty">No customer matches “{search.trim()}”</div>
              ) : (
                <>
                  {shown.map((c) => {
                    const ref = c.accountRef.toUpperCase();
                    const on = picked.has(ref);
                    const owner = memberOf.get(ref);
                    const town = c.address3 || c.address4 || "";
                    return (
                      <label key={ref} className={`rp-ed-cust${on ? " is-picked" : ""}${owner ? " is-used" : ""}`}>
                        <input type="checkbox" checked={on} onChange={() => togglePick(ref)} />
                        <span className="rp-ed-ref">
                          <Highlight text={c.accountRef} term={term} />
                        </span>
                        <span className="rp-ed-cname">
                          <Highlight text={c.name ?? ""} term={term} />
                          {town && (
                            <small>
                              <Highlight text={town} term={term} />
                            </small>
                          )}
                        </span>
                        {owner ? (
                          <span className="rp-ed-owner" title={`Already on this report under ${owner.name || "a member"}`}>
                            {owner.name || "on report"}
                          </span>
                        ) : (
                          (c.onHold || c.inactive) && <span className="rp-ed-flag">{c.onHold ? "On hold" : "Inactive"}</span>
                        )}
                      </label>
                    );
                  })}
                  {filtered.length > SHOWN && (
                    <div className="rp-empty">
                      Showing the first {SHOWN} of {filtered.length.toLocaleString()} — search to narrow the list.
                    </div>
                  )}
                </>
              )}
            </div>
            <footer className="rp-ed-actions">
              <div className="rp-ed-picked">
                {picked.size > 0 ? (
                  <>
                    <strong>{picked.size}</strong> selected
                    {moving > 0 && <span className="rp-ed-moving"> · {moving} already on the report (adding moves {moving === 1 ? "it" : "them"})</span>}
                    <button className="rp-link-btn" onClick={() => setPicked(new Set())}>
                      Clear
                    </button>
                  </>
                ) : (
                  term &&
                  filtered.length > 0 &&
                  filtered.length <= SHOWN && (
                    <button className="rp-link-btn" onClick={pickAllShown}>
                      Select all {filtered.length} shown
                    </button>
                  )
                )}
              </div>
              <div className="rp-ed-buttons">
                <button className="toolbar-btn" disabled={picked.size === 0} onClick={addSeparately} title="Each selected account becomes its own member">
                  {picked.size > 1 ? "Add as separate members" : "Add as member"}
                </button>
                {picked.size > 1 && (
                  <button className="toolbar-btn" onClick={addTogether} title="All the selected accounts become one member, added up as one line">
                    Add as one member
                  </button>
                )}
                {targetMember && (
                  <button className="btn-primary" disabled={picked.size === 0} onClick={addToTarget} title="Add the selected accounts to the member selected on the right">
                    Add to {targetMember.name.trim() || "selected member"}
                  </button>
                )}
              </div>
            </footer>
          </section>

          {/* ---- Members ---- */}
          <section className="rp-ed-pane">
            <header className="rp-ed-head">
              <h3>Members on this report</h3>
              <span>
                {members.length} member{members.length === 1 ? "" : "s"} · {accountCount} account{accountCount === 1 ? "" : "s"}
              </span>
            </header>
            <p className="rp-members-help">
              Each member is one line on the report. A member with several accounts (a co-op’s branches, say) is added up as
              one, with the branch split underneath. Click a member to add accounts to it; the order here is the order on
              the report.
            </p>
            <div className="rp-ed-list rp-ed-members">
              {loading ? (
                <div className="rp-empty">Loading…</div>
              ) : members.length === 0 ? (
                <div className="rp-empty">No members yet — tick Sage customers on the left and add them.</div>
              ) : (
                members.map((m, i) => (
                  <div
                    key={m.key}
                    className={`rp-ed-member${m.key === target ? " is-target" : ""}`}
                    onClick={() => setTarget(m.key === target ? null : m.key)}
                  >
                    <div className="rp-ed-member-head">
                      <input
                        ref={(el) => {
                          if (el) nameRefs.current.set(m.key, el);
                          else nameRefs.current.delete(m.key);
                        }}
                        className="ld-input"
                        value={m.name}
                        maxLength={60}
                        placeholder="Member name"
                        onClick={(e) => {
                          e.stopPropagation();
                          setTarget(m.key);
                        }}
                        onChange={(e) => updateMember(m.key, { name: e.target.value })}
                      />
                      <span className="rp-ed-member-tools" onClick={(e) => e.stopPropagation()}>
                        <button className="rp-link-btn" onClick={() => move(m.key, -1)} disabled={i === 0} title="Move up">
                          ▲
                        </button>
                        <button className="rp-link-btn" onClick={() => move(m.key, 1)} disabled={i === members.length - 1} title="Move down">
                          ▼
                        </button>
                        <button
                          className="rp-link-btn"
                          onClick={() => {
                            setMembers((ms) => ms.filter((x) => x.key !== m.key));
                            if (target === m.key) setTarget(null);
                          }}
                          title="Remove this member from the report"
                        >
                          Remove
                        </button>
                      </span>
                    </div>
                    <div className="rp-ed-chips">
                      {m.refs.length === 0 && <span className="rp-ed-noacc">No accounts — add some, or remove the member</span>}
                      {m.refs.map((r) => {
                        const c = byRef.get(r);
                        const unknown = customers !== null && customers.length > 0 && !c;
                        return (
                          <span
                            key={r}
                            className={`rp-ed-chip${unknown ? " is-unknown" : ""}`}
                            title={unknown ? `${r} is not in the Sage customer list` : `${r} ${c?.name ?? ""}`.trim()}
                          >
                            <b>{r}</b>
                            {c?.name && <span className="rp-ed-chip-name">{c.name}</span>}
                            {unknown && <span className="rp-ed-chip-name">not in Sage</span>}
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                removeRef(m.key, r);
                              }}
                              title={`Take ${r} off ${m.name.trim() || "this member"}`}
                            >
                              ×
                            </button>
                          </span>
                        );
                      })}
                    </div>
                  </div>
                ))
              )}
            </div>
          </section>
        </div>
      </div>
    </Modal>
  );
}
