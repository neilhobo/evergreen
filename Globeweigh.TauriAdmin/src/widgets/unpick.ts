import { confirmDialog, messageDialog } from "../components/confirmDialog";
import { PickedPallet, unpickPallet } from "../api/dispatch";

// The ⋮ Unpick on the load and order detail panels' picked pallets: confirm, then take the pallet
// back off the order. True when it was unpicked, so the caller re-reads its grids.
export async function confirmUnpick(p: PickedPallet): Promise<boolean> {
  const what = p.palletId ? `pallet ${p.palletId}` : "this pick";
  const ok = await confirmDialog(
    `Unpick ${what}${p.productDescription ? ` (${p.productDescription})` : ""} from order ${p.orderNo}?\n\n` +
      "It comes off the order and is free to be picked again.",
  );
  if (!ok) return false;
  try {
    await unpickPallet(p.id);
    return true;
  } catch (err) {
    void messageDialog("Failed to unpick.\n" + (err as Error).message);
    return false;
  }
}
