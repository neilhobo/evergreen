fn main() {
    // Declaring the app's own commands here generates an `allow-<command>` permission for
    // each. Without them the commands can only be invoked by the bundled loader page —
    // Tauri v2 rejects app commands coming from a remote (server-hosted) URL unless the
    // matching permission is granted in the window capability. That is why Print / Export
    // worked in dev but silently fell back to window.print() in production.
    tauri_build::try_build(
        tauri_build::Attributes::new().app_manifest(
            tauri_build::AppManifest::new().commands(&[
                "launch_vnc",
                "print_system_dialog",
                "export_pdf",
                "print_note",
                "list_printers",
            ]),
        ),
    )
    .expect("failed to run tauri-build");
}
