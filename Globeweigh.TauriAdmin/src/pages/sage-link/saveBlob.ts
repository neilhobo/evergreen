// Save a downloaded file. The Tauri webview cannot download, so inside the shell it is a
// Save-As dialog plus the fs plugin (a permitted plugin command — custom app commands are
// blocked for the server-hosted UI); in a plain browser an anchor download. Same flow as
// weighbridge/exportForSupplier.ts. Returns false if the dialog was cancelled.
export async function saveBlob(blob: Blob, filename: string, extension: string): Promise<boolean> {
  const inTauri = typeof window !== "undefined" && "__TAURI_INTERNALS__" in window;

  if (inTauri) {
    const { save } = await import("@tauri-apps/plugin-dialog");
    const { writeFile } = await import("@tauri-apps/plugin-fs");
    const path = await save({
      defaultPath: filename,
      filters: [{ name: extension.toUpperCase(), extensions: [extension] }],
    });
    if (!path) return false;
    try {
      await writeFile(path, new Uint8Array(await blob.arrayBuffer()));
    } catch (err) {
      const detail = typeof err === "string" ? err : err instanceof Error ? err.message : JSON.stringify(err);
      throw new Error(`Could not write the file.\n${detail}`);
    }
    return true;
  }

  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  setTimeout(() => URL.revokeObjectURL(url), 1000);
  return true;
}
