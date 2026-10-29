fn main() {
    // Generate ACL permissions (allow-<command>) for the app's own commands so the
    // capability can grant them to the SERVER-HOSTED UI. Without this, app commands are
    // only callable from local windows — the remote page's invoke() rejects, which is why
    // production screens (remote UI) saw no COM ports/printers while dev (localhost) did.
    tauri_build::try_build(tauri_build::Attributes::new().app_manifest(
        tauri_build::AppManifest::new().commands(&[
            "list_com_ports",
            "probe_com_ports",
            "list_printers",
            "print_raw",
            "print_image",
            "weighscale_connect",
            "weighscale_disconnect",
            "probe_relay_ports",
            "relay_connect",
            "relay_set",
            "relay_disconnect",
        ]),
    ))
    .expect("failed to run tauri-build");
}
