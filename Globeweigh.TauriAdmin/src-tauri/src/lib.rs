// Launch the locally-installed VNC viewer against a host, mirroring the WPF
// LaunchRealVnc (Process.Start with the host as the sole argument). Used by the
// Deliveries/Incoming "Remote Connect" TOUCH-IN / TOUCH-OUT links.
#[tauri::command]
fn launch_vnc(host: String) -> Result<(), String> {
    use std::path::Path;
    use std::process::Command;

    const CANDIDATES: [&str; 2] = [
        r"C:\Program Files\RealVNC\VNC Viewer\vncviewer.exe",
        r"C:\Program Files (x86)\RealVNC\VNC Viewer\vncviewer.exe",
    ];

    let viewer = CANDIDATES.iter().find(|p| Path::new(p).exists());
    match viewer {
        Some(path) => {
            Command::new(path)
                .arg(&host)
                .spawn()
                .map_err(|e| format!("Could not start VNC Viewer: {e}"))?;
            Ok(())
        }
        None => Err("RealVNC Viewer not found. Please install it on this PC.".into()),
    }
}

// Show the classic Windows print dialog for the current page (WebView2 ShowPrintUI
// with the SYSTEM dialog kind) instead of window.print()'s full-window preview.
// Kept for completeness; the delivery-note preview no longer uses it (it prints
// silently via print_note — the system dialog has no way to express print settings,
// so its jobs run with WebView2's defaults, headers/footers included).
#[tauri::command]
fn print_system_dialog(window: tauri::WebviewWindow) -> Result<(), String> {
    window
        .with_webview(|webview| {
            #[cfg(windows)]
            unsafe {
                use webview2_com::Microsoft::Web::WebView2::Win32::{
                    ICoreWebView2_16, COREWEBVIEW2_PRINT_DIALOG_KIND_SYSTEM,
                };
                use windows_core::Interface;
                if let Ok(core) = webview.controller().CoreWebView2() {
                    if let Ok(wv16) = core.cast::<ICoreWebView2_16>() {
                        let _ = wv16.ShowPrintUI(COREWEBVIEW2_PRINT_DIALOG_KIND_SYSTEM);
                    }
                }
            }
        })
        .map_err(|e| e.to_string())
}

// ---- Delivery-note printing ---------------------------------------------------------
//
// Export (PrintToPdf) and Print (ICoreWebView2_16::Print) share one settings object:
// A4, 10mm margins, backgrounds OFF, NO browser header/footer. The 10mm must match
// DeliveryNote.css's @page margin: the note is laid out to the 190mm that leaves, so a
// mismatch here either shifts the sheet off-centre (margin too small) or makes it too
// wide for the page box and scales it down (too big).
//
// Backgrounds must stay OFF. The window's backgroundColor (tauri.conf.json, #0d1624) is
// WebView2's default/base background, and with backgrounds on Chromium paints it over
// the ENTIRE sheet before the content — the note's white page box then covers only the
// page area, leaving a 1cm near-black frame in the margins (seen on paper 2026-09-22,
// confirmed in the PDF ops: a full-page fill in that colour, then a white fill inset by
// the margin). No CSS can reach the margin area. With backgrounds off nothing is
// painted there, and the note keeps its green bands anyway via
// `print-color-adjust: exact` on .dn-doc — the same configuration the Edge print
// dialog used ("Background graphics" unchecked), which printed correctly.
//
// Why Print goes through this API and not window.print(): the Edge print preview that
// window.print() opens has a "Headers and footers" option (date/title at the top,
// URL/page number at the bottom) that defaults ON per profile, and nothing a web page
// can do switches it off — Chromium only hides the option when the page has no
// top/bottom margin, and the note's 10mm page box is exactly what makes it print at
// true size. Print() takes the same settings as PrintToPdf, so the paper comes out
// like the Export PDF: header-free, and the preview modal is the preview.
#[cfg(windows)]
unsafe fn note_print_settings(
    core: &webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2,
) -> Option<webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2PrintSettings> {
    use webview2_com::Microsoft::Web::WebView2::Win32::{ICoreWebView2Environment6, ICoreWebView2_2};
    use windows_core::Interface;

    let settings = core
        .cast::<ICoreWebView2_2>()
        .ok()
        .and_then(|wv2| wv2.Environment().ok())
        .and_then(|env| env.cast::<ICoreWebView2Environment6>().ok())
        .and_then(|env6| env6.CreatePrintSettings().ok())?;
    let _ = settings.SetShouldPrintBackgrounds(false);
    let _ = settings.SetShouldPrintHeaderAndFooter(false);
    const MARGIN_IN: f64 = 10.0 / 25.4; // 10mm, in inches
    let _ = settings.SetMarginTop(MARGIN_IN);
    let _ = settings.SetMarginBottom(MARGIN_IN);
    let _ = settings.SetMarginLeft(MARGIN_IN);
    let _ = settings.SetMarginRight(MARGIN_IN);
    let _ = settings.SetPageWidth(8.27); // A4 inches
    let _ = settings.SetPageHeight(11.69);
    Some(settings)
}

// Export the current page to a PDF at `path` (WebView2 PrintToPdf) — the delivery-note
// preview's Export button.
#[tauri::command]
fn export_pdf(window: tauri::WebviewWindow, path: String) -> Result<(), String> {
    window
        .with_webview(move |webview| {
            #[cfg(windows)]
            unsafe {
                use webview2_com::Microsoft::Web::WebView2::Win32::ICoreWebView2_7;
                use webview2_com::PrintToPdfCompletedHandler;
                use windows_core::{Interface, HSTRING};

                let Ok(core) = webview.controller().CoreWebView2() else { return };
                let Ok(wv7) = core.cast::<ICoreWebView2_7>() else { return };
                let settings = note_print_settings(&core);
                let handler = PrintToPdfCompletedHandler::create(Box::new(|_hr, _ok| Ok(())));
                let _ = wv7.PrintToPdf(&HSTRING::from(path.as_str()), settings.as_ref(), &handler);
            }
        })
        .map_err(|e| e.to_string())
}

// Print the current page (the delivery note) straight to `printer` — no Edge print
// preview, no system dialog — with the shared settings plus `copies`, collated,
// single-sided (as the server's auto-print does). An empty printer name means the
// Windows default printer. Resolves once WebView2 has handed the job to the spooler,
// or with the reason it could not.
#[tauri::command]
async fn print_note(window: tauri::WebviewWindow, printer: String, copies: i32) -> Result<(), String> {
    let (tx, rx) = std::sync::mpsc::channel::<Result<(), String>>();
    window
        .with_webview(move |webview| {
            #[cfg(windows)]
            unsafe {
                use webview2_com::Microsoft::Web::WebView2::Win32::{
                    ICoreWebView2PrintSettings2, ICoreWebView2_16, COREWEBVIEW2_PRINT_COLLATION_COLLATED,
                    COREWEBVIEW2_PRINT_DUPLEX_ONE_SIDED, COREWEBVIEW2_PRINT_STATUS_PRINTER_UNAVAILABLE,
                    COREWEBVIEW2_PRINT_STATUS_SUCCEEDED,
                };
                use webview2_com::PrintCompletedHandler;
                use windows_core::{Interface, HSTRING};

                let Ok(core) = webview.controller().CoreWebView2() else {
                    let _ = tx.send(Err("WebView2 is not available.".into()));
                    return;
                };
                let Ok(wv16) = core.cast::<ICoreWebView2_16>() else {
                    let _ = tx.send(Err(
                        "This WebView2 runtime cannot print silently — update Microsoft Edge WebView2 on this PC.".into(),
                    ));
                    return;
                };
                let Some(settings) = note_print_settings(&core) else {
                    let _ = tx.send(Err("Could not create the print settings.".into()));
                    return;
                };
                if let Ok(s2) = settings.cast::<ICoreWebView2PrintSettings2>() {
                    let _ = s2.SetCopies(copies.clamp(1, 99));
                    let _ = s2.SetCollation(COREWEBVIEW2_PRINT_COLLATION_COLLATED);
                    let _ = s2.SetDuplex(COREWEBVIEW2_PRINT_DUPLEX_ONE_SIDED);
                    if !printer.is_empty() {
                        let _ = s2.SetPrinterName(&HSTRING::from(printer.as_str()));
                    }
                }

                let done = tx.clone();
                // The callback macro hands the HRESULT over as a windows_core::Result.
                let handler = PrintCompletedHandler::create(Box::new(move |hr, status| {
                    let result = match (hr, status.0) {
                        (Ok(()), s) if s == COREWEBVIEW2_PRINT_STATUS_SUCCEEDED.0 => Ok(()),
                        (_, s) if s == COREWEBVIEW2_PRINT_STATUS_PRINTER_UNAVAILABLE.0 => {
                            Err("Printer unavailable — check it is installed and online.".to_string())
                        }
                        (Err(e), s) => Err(format!("Print failed (status {s}): {e}")),
                        (Ok(()), s) => Err(format!("Print failed (status {s}).")),
                    };
                    let _ = done.send(result);
                    Ok(())
                }));
                if let Err(e) = wv16.Print(&settings, &handler) {
                    let _ = tx.send(Err(format!("Print could not start: {e}")));
                }
            }
            #[cfg(not(windows))]
            {
                let _ = (webview, printer, copies);
                let _ = tx.send(Err("Printing is only supported on Windows.".into()));
            }
        })
        .map_err(|e| e.to_string())?;

    // The completion handler fires on the UI thread; wait for it off the async runtime.
    tauri::async_runtime::spawn_blocking(move || {
        rx.recv_timeout(std::time::Duration::from_secs(180))
            .map_err(|_| "Timed out waiting for the printer.".to_string())
            .and_then(|r| r)
    })
    .await
    .map_err(|e| e.to_string())?
}

#[derive(serde::Serialize)]
struct PrinterList {
    printers: Vec<String>,
    #[serde(rename = "default")]
    default_printer: Option<String>,
}

// Installed printers (local + network connections) for the delivery-note preview's
// printer picker, plus the Windows default printer.
#[tauri::command]
fn list_printers() -> Result<PrinterList, String> {
    #[cfg(windows)]
    unsafe {
        use windows::Win32::Graphics::Printing::{
            EnumPrintersW, GetDefaultPrinterW, PRINTER_ENUM_CONNECTIONS, PRINTER_ENUM_LOCAL, PRINTER_INFO_4W,
        };
        use windows_core::{PCWSTR, PWSTR};

        let flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;
        let (mut needed, mut returned) = (0u32, 0u32);
        // Sizing call: fails with ERROR_INSUFFICIENT_BUFFER by design and fills `needed`.
        let _ = EnumPrintersW(flags, PCWSTR::null(), 4, None, &mut needed, &mut returned);
        let mut printers = Vec::new();
        if needed > 0 {
            // u64-backed so the PRINTER_INFO_4W array (pointer-aligned) is properly aligned.
            let mut backing = vec![0u64; (needed as usize).div_ceil(8)];
            let buf = std::slice::from_raw_parts_mut(backing.as_mut_ptr() as *mut u8, needed as usize);
            EnumPrintersW(flags, PCWSTR::null(), 4, Some(buf), &mut needed, &mut returned)
                .map_err(|e| format!("EnumPrinters failed: {e}"))?;
            let infos = std::slice::from_raw_parts(backing.as_ptr() as *const PRINTER_INFO_4W, returned as usize);
            for info in infos {
                if !info.pPrinterName.is_null() {
                    if let Ok(name) = info.pPrinterName.to_string() {
                        if !name.is_empty() {
                            printers.push(name);
                        }
                    }
                }
            }
        }
        printers.sort_by_key(|p| p.to_lowercase());

        let mut len = 0u32;
        let _ = GetDefaultPrinterW(None, &mut len);
        let mut default_printer = None;
        if len > 0 {
            let mut wbuf = vec![0u16; len as usize];
            if GetDefaultPrinterW(Some(PWSTR(wbuf.as_mut_ptr())), &mut len).as_bool() {
                let end = wbuf.iter().position(|&c| c == 0).unwrap_or(wbuf.len());
                default_printer = Some(String::from_utf16_lossy(&wbuf[..end]));
            }
        }
        Ok(PrinterList { printers, default_printer })
    }
    #[cfg(not(windows))]
    Ok(PrinterList { printers: Vec::new(), default_printer: None })
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_http::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_process::init())
        .plugin(tauri_plugin_dialog::init())
        // fs plugin: the "Export for Supplier" flow writes the xlsx to the dialog-chosen path
        // via fs.writeFile. (Anything invoked from the server-hosted UI — plugin command or
        // app command — must be granted in the capability; app commands additionally need
        // declaring in build.rs to generate their permission.)
        .plugin(tauri_plugin_fs::init())
        // shell plugin: the Devices page launches adb / scrcpy to remote-control Android
        // devices. A plugin (not a custom command) is required so it works from the
        // server-hosted (remote) UI; the allowed binaries are scoped in capabilities.
        .plugin(tauri_plugin_shell::init())
        .invoke_handler(tauri::generate_handler![
            launch_vnc,
            print_system_dialog,
            export_pdf,
            print_note,
            list_printers
        ])
        .setup(|_app| {
            // Production builds open maximized; dev keeps the configured 1800x1024 window.
            #[cfg(not(debug_assertions))]
            {
                use tauri::Manager;
                if let Some(win) = _app.get_webview_window("main") {
                    let _ = win.maximize();
                }
            }
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
