import { useRef, useState } from "react";
import Modal from "../../components/Modal";
import { messageDialog } from "../../components/confirmDialog";
import PalletLabel, { SAMPLE_LABEL_DATA } from "../../components/PalletLabel";
import {
  CustomerSupplier,
  CustomerLabelConfig,
  setLabelLogo,
  deleteLabelLogo,
} from "../../api/customersSuppliers";

interface Props {
  customer: CustomerSupplier;
  current: CustomerLabelConfig | null;
  onClose: (saved: boolean) => void;
}

const MAX_LOGO_BYTES = 2 * 1024 * 1024;

export function logoDataUrl(cfg: CustomerLabelConfig | null): string | null {
  if (!cfg?.hasCustomLogo || !cfg.logoBase64) return null;
  return `data:${cfg.logoContentType ?? "image/png"};base64,${cfg.logoBase64}`;
}

// Per-customer label configuration. For now the only override is the logo — pick an
// image to replace the default Evergreen logo on this customer's labels.
export default function LabelConfigModal({ customer, current, onClose }: Props) {
  const fileRef = useRef<HTMLInputElement>(null);
  // Pending change: undefined = untouched, null = revert to default, else new logo.
  const [pending, setPending] = useState<
    { fileName: string; contentType: string; base64: string } | null | undefined
  >(undefined);
  const [busy, setBusy] = useState(false);

  const currentUrl = logoDataUrl(current);
  const previewUrl =
    pending === undefined
      ? currentUrl
      : pending === null
        ? null
        : `data:${pending.contentType};base64,${pending.base64}`;

  const hasCustom = pending === undefined ? current?.hasCustomLogo ?? false : pending !== null;
  const dirty = pending !== undefined && !(pending === null && !current?.hasCustomLogo);

  function pickFile(file: File) {
    if (!file.type.startsWith("image/")) {
      void messageDialog("Please choose an image file (PNG or JPG).");
      return;
    }
    if (file.size > MAX_LOGO_BYTES) {
      void messageDialog("Image is too large — maximum 2 MB.");
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      // result is "data:<type>;base64,<data>" — keep just the base64 part.
      const dataUrl = reader.result as string;
      const base64 = dataUrl.substring(dataUrl.indexOf(",") + 1);
      setPending({ fileName: file.name, contentType: file.type, base64 });
    };
    reader.readAsDataURL(file);
  }

  async function onSave() {
    if (!dirty) {
      onClose(false);
      return;
    }
    setBusy(true);
    try {
      if (pending) await setLabelLogo(customer.id, pending);
      else await deleteLabelLogo(customer.id);
      onClose(true);
    } catch (err) {
      void messageDialog("Failed to save label configuration.\n" + (err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Modal
      title={`Label Configuration — ${customer.name ?? ""}`}
      width={640}
      onClose={() => onClose(false)}
      footer={
        <>
          <button className="toolbar-btn" onClick={() => onClose(false)}>
            Cancel
          </button>
          <button className="btn-blue" disabled={busy || !dirty} onClick={() => void onSave()}>
            {busy ? "Saving…" : "Save"}
          </button>
        </>
      }
    >
      <div style={{ display: "flex", flexDirection: "column", gap: 12 }}>
        <div className="toolbar" style={{ padding: 0 }}>
          <span className="form-label" style={{ margin: 0 }}>
            Logo
          </span>
          <div className="toolbar-right">
            <button className="toolbar-btn" onClick={() => fileRef.current?.click()}>
              Choose Image…
            </button>
            <button
              className="toolbar-btn"
              disabled={!hasCustom}
              onClick={() => setPending(null)}
            >
              Use Default Logo
            </button>
          </div>
          <input
            ref={fileRef}
            type="file"
            accept="image/*"
            style={{ display: "none" }}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) pickFile(f);
              e.target.value = "";
            }}
          />
        </div>
        <p className="toolbar-note" style={{ margin: 0 }}>
          {hasCustom
            ? `Custom logo${
                (pending && pending.fileName) || current?.logoFileName
                  ? `: ${pending ? pending.fileName : current?.logoFileName}`
                  : ""
              }`
            : "Using the default Evergreen logo."}
        </p>
        {/* Live preview of this customer's label with the chosen logo. */}
        <PalletLabel
          data={{ ...SAMPLE_LABEL_DATA, customerName: customer.name ?? "" }}
          logoUrl={previewUrl}
          scale={0.95}
        />
      </div>
    </Modal>
  );
}
