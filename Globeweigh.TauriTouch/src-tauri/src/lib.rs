use std::io::Read;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use tauri::{AppHandle, Emitter, State};

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ComPort {
    pub name: String,        // e.g. "COM3"
    pub description: String, // e.g. "USB Serial Port (FTDI)" — best-effort, may be empty
}

fn port_description(port_type: &serialport::SerialPortType) -> String {
    use serialport::SerialPortType;
    match port_type {
        SerialPortType::UsbPort(usb) => {
            let name = usb.product.clone().or(usb.manufacturer.clone()).unwrap_or_default();
            if name.is_empty() { "USB serial".to_string() } else { name }
        }
        SerialPortType::BluetoothPort => "Bluetooth serial".to_string(),
        SerialPortType::PciPort => "PCI serial".to_string(),
        SerialPortType::Unknown => String::new(),
    }
}

// Natural sort so COM10 comes after COM2.
fn com_sort_key(name: &str) -> (u32, String) {
    let digits: String = name.chars().filter(|c| c.is_ascii_digit()).collect();
    (digits.parse::<u32>().unwrap_or(u32::MAX), name.to_string())
}

// The OS's authoritative list of serial ports that are present RIGHT NOW
// (HKLM\HARDWARE\DEVICEMAP\SERIALCOMM — what Device Manager reflects). Needed because
// serialport::available_ports() only sees drivers that expose the COM-port device
// interface; older USB-serial/PCI/virtual drivers don't, and came back empty on a
// client PC whose ports were plainly in Device Manager.
fn registry_com_ports() -> Vec<String> {
    #[cfg(target_os = "windows")]
    {
        use std::os::windows::process::CommandExt;
        const CREATE_NO_WINDOW: u32 = 0x0800_0000;
        if let Ok(out) = std::process::Command::new("reg")
            .args(["query", r"HKLM\HARDWARE\DEVICEMAP\SERIALCOMM"])
            .creation_flags(CREATE_NO_WINDOW)
            .output()
        {
            if out.status.success() {
                // Lines look like: "    \Device\Serial0    REG_SZ    COM1"
                return String::from_utf8_lossy(&out.stdout)
                    .lines()
                    .filter_map(|l| {
                        let mut parts = l.split_whitespace();
                        let _device = parts.next()?;
                        let reg_type = parts.next()?;
                        let value = parts.next()?;
                        (reg_type.eq_ignore_ascii_case("REG_SZ")
                            && value.to_ascii_uppercase().starts_with("COM"))
                        .then(|| value.to_string())
                    })
                    .collect();
            }
        }
        Vec::new()
    }
    #[cfg(not(target_os = "windows"))]
    {
        Vec::new()
    }
}

// serialport enumeration (rich descriptions) merged with the registry list (complete).
fn all_com_ports() -> Vec<ComPort> {
    let mut ports: Vec<ComPort> = serialport::available_ports()
        .unwrap_or_default()
        .into_iter()
        .map(|p| ComPort { name: p.port_name, description: port_description(&p.port_type) })
        .collect();
    for name in registry_com_ports() {
        if !ports.iter().any(|p| p.name.eq_ignore_ascii_case(&name)) {
            ports.push(ComPort { name, description: String::new() });
        }
    }
    ports.sort_by_key(|p| com_sort_key(&p.name));
    ports
}

// Enumerate the machine's serial ports for the weighscale setup screen. USB-serial
// adapters expose product/manufacturer strings; native/unknown ports just get a type label.
#[tauri::command]
fn list_com_ports() -> Vec<ComPort> {
    all_com_ports()
}

#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PrinterInfo {
    pub name: String,
    pub is_default: bool,
}

// Enumerate the printers installed on this PC (for the Home-bar printer picker).
// Direct winspool EnumPrintersW — the same list Windows' own print dialogs show.
// (The first cut used Get-CimInstance via PowerShell; that came back empty on a
// client Win10 PC, so no WMI / child processes here.)
#[tauri::command]
fn list_printers() -> Result<Vec<PrinterInfo>, String> {
    #[cfg(target_os = "windows")]
    {
        use windows_sys::Win32::Graphics::Printing::{
            EnumPrintersW, GetDefaultPrinterW, PRINTER_ENUM_CONNECTIONS, PRINTER_ENUM_LOCAL,
            PRINTER_INFO_4W,
        };

        fn widestr(ptr: *const u16) -> String {
            if ptr.is_null() {
                return String::new();
            }
            unsafe {
                let mut len = 0usize;
                while *ptr.add(len) != 0 {
                    len += 1;
                }
                String::from_utf16_lossy(std::slice::from_raw_parts(ptr, len))
            }
        }

        unsafe {
            // Level 4 (name + attributes only) covers local printers AND per-user
            // network connections, straight from the registry — no spooler RPC delays.
            let flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;
            let (mut needed, mut returned) = (0u32, 0u32);
            EnumPrintersW(flags, std::ptr::null(), 4, std::ptr::null_mut(), 0, &mut needed, &mut returned);
            if needed == 0 {
                return Ok(Vec::new());
            }
            let mut buf = vec![0u8; needed as usize];
            if EnumPrintersW(flags, std::ptr::null(), 4, buf.as_mut_ptr(), needed, &mut needed, &mut returned) == 0 {
                return Err(format!("EnumPrinters failed: {}", std::io::Error::last_os_error()));
            }

            let mut default_len = 0u32;
            GetDefaultPrinterW(std::ptr::null_mut(), &mut default_len);
            let mut default_buf = vec![0u16; (default_len as usize).max(1)];
            let default_name =
                if default_len > 0 && GetDefaultPrinterW(default_buf.as_mut_ptr(), &mut default_len) != 0 {
                    widestr(default_buf.as_ptr())
                } else {
                    String::new()
                };

            let infos =
                std::slice::from_raw_parts(buf.as_ptr() as *const PRINTER_INFO_4W, returned as usize);
            let mut printers: Vec<PrinterInfo> = infos
                .iter()
                .map(|i| widestr(i.pPrinterName))
                .filter(|n| !n.is_empty())
                .map(|name| {
                    let is_default = !default_name.is_empty() && name == default_name;
                    PrinterInfo { name, is_default }
                })
                .collect();
            printers.sort_by_key(|p| (!p.is_default, p.name.to_lowercase()));
            Ok(printers)
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        Ok(Vec::new())
    }
}

// Send raw bytes (e.g. ZPL) straight to a named Windows printer via the spooler's RAW
// datatype — the standard way to drive label printers without a driver rendering pass.
#[tauri::command]
fn print_raw(printer: String, data: String) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use windows_sys::Win32::Graphics::Printing::{
            ClosePrinter, EndDocPrinter, EndPagePrinter, OpenPrinterW, StartDocPrinterW,
            StartPagePrinter, WritePrinter, DOC_INFO_1W, PRINTER_HANDLE,
        };
        unsafe {
            let name: Vec<u16> = printer.encode_utf16().chain([0]).collect();
            let mut handle = PRINTER_HANDLE::default();
            if OpenPrinterW(name.as_ptr(), &mut handle, std::ptr::null()) == 0 {
                return Err(format!("Could not open printer \"{printer}\": {}", std::io::Error::last_os_error()));
            }
            let doc_name: Vec<u16> = "Globeweigh Label\0".encode_utf16().collect();
            let datatype: Vec<u16> = "RAW\0".encode_utf16().collect();
            let doc = DOC_INFO_1W {
                pDocName: doc_name.as_ptr() as *mut u16,
                pOutputFile: std::ptr::null_mut(),
                pDatatype: datatype.as_ptr() as *mut u16,
            };
            let result = (|| {
                if StartDocPrinterW(handle, 1, &doc) == 0 {
                    return Err(format!("StartDocPrinter failed: {}", std::io::Error::last_os_error()));
                }
                if StartPagePrinter(handle) == 0 {
                    EndDocPrinter(handle);
                    return Err(format!("StartPagePrinter failed: {}", std::io::Error::last_os_error()));
                }
                let bytes = data.as_bytes();
                let mut written = 0u32;
                let ok = WritePrinter(handle, bytes.as_ptr() as *const _, bytes.len() as u32, &mut written);
                EndPagePrinter(handle);
                EndDocPrinter(handle);
                if ok == 0 || written as usize != bytes.len() {
                    return Err(format!("WritePrinter failed ({written}/{} bytes)", bytes.len()));
                }
                Ok(())
            })();
            ClosePrinter(handle);
            result
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = (printer, data);
        Err("Printing is only supported on Windows.".into())
    }
}

// Print a rendered label image through the normal Windows driver path (GDI), exactly like
// printing a document from Word — no ZPL. The UI renders the label to a canvas, rotates it
// to the media orientation, and sends top-down BGRA pixels; we stretch them onto the page.
#[tauri::command]
fn print_image(printer: String, width: i32, height: i32, bgra_base64: String) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        use base64::Engine;
        use windows_sys::Win32::Graphics::Gdi::{
            CreateDCW, DeleteDC, GetDeviceCaps, SetStretchBltMode, StretchDIBits, BITMAPINFO,
            BITMAPINFOHEADER, BI_RGB, DEVMODEW, DIB_RGB_COLORS, DMORIENT_PORTRAIT, DMPAPER_USER,
            DM_IN_BUFFER, DM_ORIENTATION, DM_OUT_BUFFER, DM_PAPERLENGTH, DM_PAPERSIZE,
            DM_PAPERWIDTH, HALFTONE, HORZRES, SRCCOPY, VERTRES,
        };
        use windows_sys::Win32::Graphics::Printing::{
            ClosePrinter, DocumentPropertiesW, OpenPrinterW, PRINTER_HANDLE,
        };
        use windows_sys::Win32::Storage::Xps::{EndDoc, EndPage, StartDocW, StartPage, DOCINFOW};

        // The label media, in tenths of a millimetre (DEVMODE units): 10 cm wide, 15 cm high.
        const PAPER_WIDTH_TMM: i16 = 1000;
        const PAPER_LENGTH_TMM: i16 = 1500;

        if width <= 0 || height <= 0 {
            return Err("Invalid image size.".into());
        }
        let pixels = base64::engine::general_purpose::STANDARD
            .decode(bgra_base64)
            .map_err(|e| format!("Bad image data: {e}"))?;
        if pixels.len() != width as usize * height as usize * 4 {
            return Err("Pixel data does not match the image size.".into());
        }

        unsafe {
            let name: Vec<u16> = printer.encode_utf16().chain([0]).collect();

            // Force the 10 × 15 cm portrait label into the driver's DEVMODE. Without this the
            // DC comes up on the driver's DEFAULT paper (often A4 or a different label), and the
            // scale-to-fit below prints the label at the wrong size. Best-effort: any failure
            // falls back to the driver defaults, exactly as before.
            let mut devmode_buf: Vec<u8> = Vec::new();
            let mut devmode: *const DEVMODEW = std::ptr::null();
            let mut hprinter = PRINTER_HANDLE::default();
            if OpenPrinterW(name.as_ptr(), &mut hprinter, std::ptr::null()) != 0 {
                let size = DocumentPropertiesW(
                    std::ptr::null_mut(), hprinter, name.as_ptr(),
                    std::ptr::null_mut(), std::ptr::null(), 0,
                );
                if size > 0 {
                    devmode_buf = vec![0u8; size as usize];
                    let dm = devmode_buf.as_mut_ptr() as *mut DEVMODEW;
                    // Fill with the driver defaults, then override the paper.
                    if DocumentPropertiesW(
                        std::ptr::null_mut(), hprinter, name.as_ptr(),
                        dm, std::ptr::null(), DM_OUT_BUFFER,
                    ) > 0
                    {
                        (*dm).dmFields |= DM_ORIENTATION | DM_PAPERSIZE | DM_PAPERLENGTH | DM_PAPERWIDTH;
                        (*dm).Anonymous1.Anonymous1.dmOrientation = DMORIENT_PORTRAIT as i16;
                        (*dm).Anonymous1.Anonymous1.dmPaperSize = DMPAPER_USER as i16;
                        (*dm).Anonymous1.Anonymous1.dmPaperWidth = PAPER_WIDTH_TMM;
                        (*dm).Anonymous1.Anonymous1.dmPaperLength = PAPER_LENGTH_TMM;
                        // Let the driver validate/merge the request; keep it only on success.
                        if DocumentPropertiesW(
                            std::ptr::null_mut(), hprinter, name.as_ptr(),
                            dm, dm, DM_IN_BUFFER | DM_OUT_BUFFER,
                        ) > 0
                        {
                            devmode = dm;
                        }
                    }
                }
                ClosePrinter(hprinter);
            }

            let dc = CreateDCW(std::ptr::null(), name.as_ptr(), std::ptr::null(), devmode);
            // Keep the DEVMODE buffer alive for the CreateDCW call above.
            drop(devmode_buf);
            if dc.is_null() {
                return Err(format!("Could not open printer \"{printer}\"."));
            }
            let result = (|| {
                let page_w = GetDeviceCaps(dc, HORZRES as i32);
                let page_h = GetDeviceCaps(dc, VERTRES as i32);
                let doc_name: Vec<u16> = "Globeweigh Label\0".encode_utf16().collect();
                let doc = DOCINFOW {
                    cbSize: std::mem::size_of::<DOCINFOW>() as i32,
                    lpszDocName: doc_name.as_ptr(),
                    lpszOutput: std::ptr::null(),
                    lpszDatatype: std::ptr::null(),
                    fwType: 0,
                };
                if StartDocW(dc, &doc) <= 0 {
                    return Err(format!("StartDoc failed: {}", std::io::Error::last_os_error()));
                }
                if StartPage(dc) <= 0 {
                    EndDoc(dc);
                    return Err(format!("StartPage failed: {}", std::io::Error::last_os_error()));
                }
                SetStretchBltMode(dc, HALFTONE);
                // The UI sends portrait pixels (it assumes 10 cm-wide media), but the
                // driver's page may be set up landscape — rotate to match rather than
                // letting StretchDIBits smear the image sideways across the page.
                let (mut w, mut h) = (width, height);
                let mut pixels = pixels;
                if (page_w > page_h) != (w > h) && w != h {
                    let (sw, sh) = (w as usize, h as usize);
                    let mut rot = vec![0u8; pixels.len()];
                    for r in 0..sh {
                        for c in 0..sw {
                            let src = (r * sw + c) * 4;
                            let dst = (c * sh + (sh - 1 - r)) * 4;
                            rot[dst..dst + 4].copy_from_slice(&pixels[src..src + 4]);
                        }
                    }
                    pixels = rot;
                    std::mem::swap(&mut w, &mut h);
                }
                // Scale to fit the page preserving aspect ratio, centered — never stretch.
                let scale = f64::min(page_w as f64 / w as f64, page_h as f64 / h as f64);
                let dw = ((w as f64) * scale).round() as i32;
                let dh = ((h as f64) * scale).round() as i32;
                let dx = (page_w - dw) / 2;
                let dy = (page_h - dh) / 2;
                let mut bmi: BITMAPINFO = std::mem::zeroed();
                bmi.bmiHeader = BITMAPINFOHEADER {
                    biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                    biWidth: w,
                    biHeight: -h, // negative = top-down rows, matching canvas pixel order
                    biPlanes: 1,
                    biBitCount: 32,
                    biCompression: BI_RGB as u32,
                    biSizeImage: 0,
                    biXPelsPerMeter: 0,
                    biYPelsPerMeter: 0,
                    biClrUsed: 0,
                    biClrImportant: 0,
                };
                let copied = StretchDIBits(
                    dc, dx, dy, dw, dh, 0, 0, w, h,
                    pixels.as_ptr() as *const _, &bmi, DIB_RGB_COLORS, SRCCOPY,
                );
                EndPage(dc);
                EndDoc(dc);
                if copied == 0 {
                    return Err("StretchDIBits failed.".into());
                }
                Ok(())
            })();
            DeleteDC(dc);
            result
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        let _ = (printer, width, height, bgra_base64);
        Err("Printing is only supported on Windows.".into())
    }
}

// One port's probe outcome — the setup screen's answer to "which of the 5 COM ports is the
// scale?". Mirrors what a tech does with HyperTerminal: try to open each port (busy/error =
// the connect would fail) and listen briefly for unsolicited data (continuous-mode scales).
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct PortProbe {
    pub name: String,
    pub description: String,
    // "streaming" (data received) | "silent" (opens fine, nothing sent — on-request scale
    // or nothing attached) | "busy" (in use by another program) | "error"
    pub status: String,
    pub detail: Option<String>, // sample of received data, or the open error
}

#[tauri::command]
fn probe_com_ports(baud: u32) -> Vec<PortProbe> {
    // Same merged enumeration as list_com_ports — registry-only ports must be probed too.
    let handles: Vec<_> = all_com_ports()
        .into_iter()
        .map(|p| {
            // All ports probed in parallel so 5 ports still answer in <1s.
            std::thread::spawn(move || {
                let description = p.description;
                let name = p.name;
                match serialport::new(&name, baud).timeout(Duration::from_millis(120)).open() {
                    Err(e) => {
                        let busy = matches!(
                            e.kind(),
                            serialport::ErrorKind::Io(std::io::ErrorKind::PermissionDenied)
                        );
                        PortProbe {
                            name,
                            description,
                            status: if busy { "busy" } else { "error" }.into(),
                            detail: Some(e.to_string()),
                        }
                    }
                    Ok(mut sp) => {
                        // Listen briefly for continuous-mode data. Even at a mismatched baud
                        // a transmitting scale produces bytes (garbage, but proof of life).
                        let mut buf = [0u8; 512];
                        let mut sample = String::new();
                        let start = std::time::Instant::now();
                        while start.elapsed() < Duration::from_millis(700) && sample.len() < 80 {
                            if let Ok(n) = sp.read(&mut buf) {
                                sample.push_str(&String::from_utf8_lossy(&buf[..n]));
                            }
                        }
                        if sample.is_empty() {
                            PortProbe { name, description, status: "silent".into(), detail: None }
                        } else {
                            let cleaned: String =
                                sample.chars().filter(|c| !c.is_control()).take(48).collect();
                            PortProbe {
                                name,
                                description,
                                status: "streaming".into(),
                                detail: Some(cleaned),
                            }
                        }
                    }
                }
            })
        })
        .collect();
    let mut probes: Vec<PortProbe> = handles.into_iter().filter_map(|h| h.join().ok()).collect();
    probes.sort_by_key(|p| com_sort_key(&p.name));
    probes
}

// ---- Weighscale connection (COM port, or raw TCP for Moxa-style serial servers / the
// dev weighscale-bridge.ps1). One connection at a time; a reader thread streams incoming
// bytes to the UI as `weighscale://data` events, state changes as `weighscale://status`. ----

#[derive(serde::Deserialize)]
#[serde(tag = "transport", rename_all = "lowercase")]
pub enum TransportConfig {
    #[serde(rename_all = "camelCase")]
    Serial { com_port: String, baud: u32 },
    #[serde(rename_all = "camelCase")]
    Tcp { host: String, port: u16 },
}

// On-request scales send nothing until asked: this command (+CR) goes out every interval,
// e.g. "q%" for the TXP-style indicators. Absent = continuous-mode scale, nothing is sent.
#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PollConfig {
    pub command: String,
    pub interval_ms: u64,
}

#[derive(serde::Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScaleConfig {
    #[serde(flatten)]
    pub transport: TransportConfig,
    #[serde(default)]
    pub poll: Option<PollConfig>,
}

// Both transports read AND write (polling); serial ports and TcpStream each satisfy this.
trait Conn: Read + std::io::Write + Send {}
impl<T: Read + std::io::Write + Send> Conn for T {}

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct ScaleStatus {
    pub connected: bool,
    pub error: Option<String>,
}

#[derive(Default)]
pub struct ScaleState {
    // Stop flag of the active reader thread; replaced on each connect.
    stop: Mutex<Option<Arc<AtomicBool>>>,
}

fn stop_current(state: &ScaleState) {
    if let Some(flag) = state.stop.lock().unwrap().take() {
        flag.store(true, Ordering::Relaxed);
    }
}

#[tauri::command]
fn weighscale_connect(app: AppHandle, state: State<ScaleState>, config: ScaleConfig) -> Result<(), String> {
    stop_current(&state);

    // Open synchronously so a bad port/host fails this command rather than an event later.
    let conn: Box<dyn Conn> = match &config.transport {
        TransportConfig::Serial { com_port, baud } => Box::new(
            serialport::new(com_port, *baud)
                .timeout(Duration::from_millis(200))
                .open()
                .map_err(|e| format!("{com_port}: {e}"))?,
        ),
        TransportConfig::Tcp { host, port } => {
            use std::net::{TcpStream, ToSocketAddrs};
            let addr = (host.as_str(), *port)
                .to_socket_addrs()
                .map_err(|e| format!("{host}:{port}: {e}"))?
                .next()
                .ok_or_else(|| format!("{host}:{port}: could not resolve host"))?;
            let stream = TcpStream::connect_timeout(&addr, Duration::from_secs(4))
                .map_err(|e| format!("{host}:{port}: {e}"))?;
            let _ = stream.set_read_timeout(Some(Duration::from_millis(200)));
            let _ = stream.set_nodelay(true);
            Box::new(stream)
        }
    };

    let stop = Arc::new(AtomicBool::new(false));
    *state.stop.lock().unwrap() = Some(stop.clone());
    let _ = app.emit("weighscale://status", ScaleStatus { connected: true, error: None });

    // Poll command with CR appended, ready to send; interval floor stops a hot loop.
    let poll = config
        .poll
        .map(|p| (format!("{}\r", p.command), Duration::from_millis(p.interval_ms.max(100))));

    std::thread::spawn(move || {
        let mut conn = conn;
        let mut buf = [0u8; 1024];
        let mut last_poll: Option<std::time::Instant> = None;
        loop {
            if stop.load(Ordering::Relaxed) {
                return; // disconnected or superseded by a new connect: exit silently
            }
            if let Some((cmd, every)) = &poll {
                // Cadence granularity is the 200ms read timeout below — fine at 250ms+.
                if last_poll.map_or(true, |t| t.elapsed() >= *every) {
                    use std::io::Write;
                    if let Err(e) = conn.write_all(cmd.as_bytes()).and_then(|_| conn.flush()) {
                        if !stop.load(Ordering::Relaxed) {
                            let _ = app.emit("weighscale://status", ScaleStatus {
                                connected: false,
                                error: Some(format!("Poll send failed: {e}")),
                            });
                        }
                        return;
                    }
                    last_poll = Some(std::time::Instant::now());
                }
            }
            match conn.read(&mut buf) {
                Ok(0) => {
                    // EOF: the TCP peer (bridge/Moxa) closed on us.
                    if !stop.load(Ordering::Relaxed) {
                        let _ = app.emit("weighscale://status", ScaleStatus {
                            connected: false,
                            error: Some("Connection closed by the remote end.".into()),
                        });
                    }
                    return;
                }
                Ok(n) => {
                    let _ = app.emit("weighscale://data", String::from_utf8_lossy(&buf[..n]).to_string());
                }
                Err(e) if e.kind() == std::io::ErrorKind::TimedOut || e.kind() == std::io::ErrorKind::WouldBlock => {
                    continue; // idle scale, keep listening
                }
                Err(e) => {
                    if !stop.load(Ordering::Relaxed) {
                        let _ = app.emit("weighscale://status", ScaleStatus {
                            connected: false,
                            error: Some(e.to_string()),
                        });
                    }
                    return;
                }
            }
        }
    });

    Ok(())
}

#[tauri::command]
fn weighscale_disconnect(app: AppHandle, state: State<ScaleState>) {
    stop_current(&state);
    let _ = app.emit("weighscale://status", ScaleStatus { connected: false, error: None });
}

// ---- Relay card (Robot Electronics / Devantech USB-OPTO-RLY88 family). Single-byte
// request/response protocol over the same transports as the weighscale: a command byte
// goes out, a fixed-length reply comes back. The card never transmits unsolicited —
// which is also how we detect it: a port that answers GET_VERSION with [module id,
// version] is the relay card (exactly what the Robot Electronics test app does). ----

const RELAY_GET_VERSION: u8 = 0x5a; // -> [module id, software version]
const RELAY_GET_STATES: u8 = 0x5b; // -> [relay bitmap], bit 0 = relay 1
// Input states are 0x19 on the USB-OPTO-RLY88 (per Robot Electronics' own example code).
// 0x5D is NOT a command on this card — it ignores it, which read as a poll timeout.
const RELAY_GET_INPUTS: u8 = 0x19; // -> [input bitmap], bit 0 = input 1
const RELAY_ON_BASE: u8 = 0x64; // +n = relay n on (0x65..0x6C); 0x64 alone = all on
const RELAY_OFF_BASE: u8 = 0x6e; // +n = relay n off (0x6F..0x76); 0x6E alone = all off

fn relay_model_name(id: u8) -> String {
    match id {
        8 => "USB-RLY08".into(),
        9 => "USB-RLY16".into(),
        10 => "USB-RLY16L".into(),
        // Verified against the client PC's card: it answers GET_VERSION with id 12,
        // and the Robot Electronics utility identifies it as a USB-OPTO-RLY88.
        12 => "USB-OPTO-RLY88".into(),
        _ => format!("Relay module (id {id})"),
    }
}

// Send one command byte and read an exact-length reply within the deadline.
fn relay_exchange(
    conn: &mut dyn Conn,
    cmd: u8,
    reply_len: usize,
    deadline: Duration,
) -> std::io::Result<Vec<u8>> {
    conn.write_all(&[cmd])?;
    conn.flush()?;
    let mut out = vec![0u8; reply_len];
    let mut got = 0usize;
    let start = std::time::Instant::now();
    while got < reply_len && start.elapsed() < deadline {
        match conn.read(&mut out[got..]) {
            Ok(0) => return Err(std::io::ErrorKind::UnexpectedEof.into()),
            Ok(n) => got += n,
            Err(e)
                if e.kind() == std::io::ErrorKind::TimedOut
                    || e.kind() == std::io::ErrorKind::WouldBlock => {}
            Err(e) => return Err(e),
        }
    }
    if got == reply_len {
        Ok(out)
    } else {
        Err(std::io::ErrorKind::TimedOut.into())
    }
}

// One port's relay-probe outcome: "found" identifies the card (with its model name).
// A port streaming unsolicited data (a scale) is definitively NOT the relay card.
#[derive(serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RelayProbe {
    pub name: String,
    pub description: String,
    // "found" | "silent" (no reply) | "streaming" (something else) | "busy" | "error"
    pub status: String,
    pub model: Option<String>, // e.g. "USB-OPTO-RLY88", when found
    pub detail: Option<String>, // firmware version, or the open error
}

#[tauri::command]
fn probe_relay_ports() -> Vec<RelayProbe> {
    let handles: Vec<_> = all_com_ports()
        .into_iter()
        .map(|p| {
            std::thread::spawn(move || {
                let ComPort { name, description } = p;
                // Baud is irrelevant — the card is a USB virtual COM port.
                match serialport::new(&name, 19200).timeout(Duration::from_millis(80)).open() {
                    Err(e) => {
                        let busy = matches!(
                            e.kind(),
                            serialport::ErrorKind::Io(std::io::ErrorKind::PermissionDenied)
                        );
                        RelayProbe {
                            name,
                            description,
                            status: if busy { "busy" } else { "error" }.into(),
                            model: None,
                            detail: Some(e.to_string()),
                        }
                    }
                    Ok(mut sp) => {
                        // Anything transmitting on its own (a scale) is not the relay card.
                        let mut buf = [0u8; 64];
                        let start = std::time::Instant::now();
                        let mut unsolicited = false;
                        while start.elapsed() < Duration::from_millis(250) {
                            if matches!(sp.read(&mut buf), Ok(n) if n > 0) {
                                unsolicited = true;
                                break;
                            }
                        }
                        if unsolicited {
                            return RelayProbe {
                                name,
                                description,
                                status: "streaming".into(),
                                model: None,
                                detail: None,
                            };
                        }
                        match relay_exchange(
                            &mut sp,
                            RELAY_GET_VERSION,
                            2,
                            Duration::from_millis(600),
                        ) {
                            Ok(reply) => RelayProbe {
                                name,
                                description,
                                status: "found".into(),
                                model: Some(relay_model_name(reply[0])),
                                detail: Some(format!("firmware v{}", reply[1])),
                            },
                            Err(_) => RelayProbe {
                                name,
                                description,
                                status: "silent".into(),
                                model: None,
                                detail: None,
                            },
                        }
                    }
                }
            })
        })
        .collect();
    let mut probes: Vec<RelayProbe> = handles.into_iter().filter_map(|h| h.join().ok()).collect();
    probes.sort_by_key(|p| com_sort_key(&p.name));
    probes
}

#[derive(Clone, serde::Serialize)]
#[serde(rename_all = "camelCase")]
pub struct RelayCardStatus {
    pub connected: bool,
    pub relays: u8, // bitmap, bit 0 = relay 1
    pub inputs: u8, // bitmap, bit 0 = input 1
    pub model: Option<String>,
    pub error: Option<String>,
}

// The open connection is shared between the poll thread (reads states) and relay_set
// (writes a toggle); the Mutex serialises the exchanges on the wire.
#[derive(Default)]
pub struct RelayCardState {
    conn: Arc<Mutex<Option<Box<dyn Conn>>>>,
    stop: Mutex<Option<Arc<AtomicBool>>>,
}

fn relay_stop_current(state: &RelayCardState) {
    if let Some(flag) = state.stop.lock().unwrap().take() {
        flag.store(true, Ordering::Relaxed);
    }
    *state.conn.lock().unwrap() = None;
}

// Connect and handshake (GET_VERSION identifies the card — failure = wrong port/host).
// Returns the model name; a poll thread then emits `relay://state` every 300ms.
#[tauri::command]
fn relay_connect(
    app: AppHandle,
    state: State<RelayCardState>,
    config: TransportConfig,
) -> Result<String, String> {
    relay_stop_current(&state);

    let mut conn: Box<dyn Conn> = match &config {
        TransportConfig::Serial { com_port, baud } => Box::new(
            serialport::new(com_port, *baud)
                .timeout(Duration::from_millis(200))
                .open()
                .map_err(|e| format!("{com_port}: {e}"))?,
        ),
        TransportConfig::Tcp { host, port } => {
            use std::net::{TcpStream, ToSocketAddrs};
            let addr = (host.as_str(), *port)
                .to_socket_addrs()
                .map_err(|e| format!("{host}:{port}: {e}"))?
                .next()
                .ok_or_else(|| format!("{host}:{port}: could not resolve host"))?;
            let stream = TcpStream::connect_timeout(&addr, Duration::from_secs(4))
                .map_err(|e| format!("{host}:{port}: {e}"))?;
            let _ = stream.set_read_timeout(Some(Duration::from_millis(200)));
            let _ = stream.set_nodelay(true);
            Box::new(stream)
        }
    };

    let reply = relay_exchange(conn.as_mut(), RELAY_GET_VERSION, 2, Duration::from_millis(800))
        .map_err(|_| "Port opened but no relay card answered — is this the right port?".to_string())?;
    let model = relay_model_name(reply[0]);

    let stop = Arc::new(AtomicBool::new(false));
    *state.stop.lock().unwrap() = Some(stop.clone());
    *state.conn.lock().unwrap() = Some(conn);

    let conn_arc = state.conn.clone();
    let model_for_thread = model.clone();
    std::thread::spawn(move || {
        // A single missed poll is tolerated (VPN/bridge latency blips); only give up
        // after several in a row — replies are fixed-length, so the stream re-syncs.
        let mut failures = 0u8;
        loop {
            if stop.load(Ordering::Relaxed) {
                return; // disconnected or superseded: exit silently
            }
            let polled = {
                let mut guard = conn_arc.lock().unwrap();
                let Some(conn) = guard.as_mut() else { return };
                let relays = relay_exchange(conn.as_mut(), RELAY_GET_STATES, 1, Duration::from_millis(800));
                let inputs = relay_exchange(conn.as_mut(), RELAY_GET_INPUTS, 1, Duration::from_millis(800));
                match (relays, inputs) {
                    (Ok(r), Ok(i)) => Ok((r[0], i[0])),
                    (Err(e), _) | (_, Err(e)) => {
                        failures += 1;
                        if failures >= 3 {
                            *guard = None;
                        }
                        Err(e)
                    }
                }
            };
            match polled {
                Ok((relays, inputs)) => {
                    failures = 0;
                    let _ = app.emit("relay://state", RelayCardStatus {
                        connected: true,
                        relays,
                        inputs,
                        model: Some(model_for_thread.clone()),
                        error: None,
                    });
                }
                Err(e) if failures >= 3 => {
                    if !stop.load(Ordering::Relaxed) {
                        let _ = app.emit("relay://state", RelayCardStatus {
                            connected: false,
                            relays: 0,
                            inputs: 0,
                            model: Some(model_for_thread.clone()),
                            error: Some(format!("Lost the relay card: {e}")),
                        });
                    }
                    return;
                }
                Err(_) => {} // transient: keep the last emitted state, retry
            }
            std::thread::sleep(Duration::from_millis(300));
        }
    });

    Ok(model)
}

// Switch one relay (1-8). The card sends no ack; the next poll reflects the new state.
#[tauri::command]
fn relay_set(state: State<RelayCardState>, relay: u8, on: bool) -> Result<(), String> {
    if !(1..=8).contains(&relay) {
        return Err(format!("Relay number out of range: {relay}"));
    }
    use std::io::Write;
    let cmd = if on { RELAY_ON_BASE + relay } else { RELAY_OFF_BASE + relay };
    let mut guard = state.conn.lock().unwrap();
    let conn = guard.as_mut().ok_or("Not connected to the relay card.")?;
    conn.write_all(&[cmd])
        .and_then(|_| conn.flush())
        .map_err(|e| format!("Relay command failed: {e}"))
}

#[tauri::command]
fn relay_disconnect(app: AppHandle, state: State<RelayCardState>) {
    relay_stop_current(&state);
    let _ = app.emit("relay://state", RelayCardStatus {
        connected: false,
        relays: 0,
        inputs: 0,
        model: None,
        error: None,
    });
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .manage(ScaleState::default())
        .manage(RelayCardState::default())
        .invoke_handler(tauri::generate_handler![
            list_com_ports,
            probe_com_ports,
            list_printers,
            print_raw,
            print_image,
            weighscale_connect,
            weighscale_disconnect,
            probe_relay_ports,
            relay_connect,
            relay_set,
            relay_disconnect
        ])
        .plugin(tauri_plugin_http::init())
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(tauri_plugin_process::init())
        // os plugin: lets the web UI read the real machine hostname so the API can
        // resolve which dbo.Device row (and screen mode) this touch screen is.
        .plugin(tauri_plugin_os::init())
        // Production runs borderless (no title bar, see tauri.conf.json) and maximized
        // to fill the screen, always-on-top. In DEV only, make it a normal window — title
        // bar, draggable, resizable, centered, not maximized — for easier debugging.
        .setup(|_app| {
            use tauri::Manager;
            if let Some(win) = _app.get_webview_window("main") {
                #[cfg(not(debug_assertions))]
                {
                    // Kiosk: cover the ENTIRE monitor with borderless fullscreen. `maximize()`
                    // is a no-op on a non-resizable borderless window, and sizing to the monitor
                    // can leave a sliver of desktop (DPI rounding / position offset). Fullscreen
                    // covers everything (incl. the taskbar) with no gaps.
                    let _ = win.set_fullscreen(true);
                }
                #[cfg(debug_assertions)]
                {
                    let _ = win.set_fullscreen(false);
                    let _ = win.set_decorations(true);
                    let _ = win.set_resizable(true);
                    let _ = win.set_always_on_top(false);
                    let _ = win.unmaximize();
                    let _ = win.center();
                }
            }
            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
