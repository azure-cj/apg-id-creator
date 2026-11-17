//! What the window can do that a browser tab cannot, offered to the page
//! as commands (`window.__TAURI__.core.invoke`). The page asks; nothing
//! here runs unless it does. In a browser none of this exists, and the
//! page carries on without it (see `src/desktop.ts`).
//!
//! - **Updates:** ask GitHub whether a newer release is out, and install it.
//! - **Pen tablets:** list the pens Windows knows about, so the signature
//!   step can say whether one is plugged in before anyone touches it.
//! - **The cursor:** hold it inside the signature pad while somebody signs,
//!   and always let it go again.

use serde::Serialize;
use tauri::ipc::Channel;
use tauri::{AppHandle, WebviewWindow};
use tauri_plugin_updater::{Updater, UpdaterExt};

/* ---- updates ------------------------------------------------------- */

/// The updater, pointed at GitHub unless `APG_UPDATE_URL` says otherwise.
/// The override exists to test an install against a local `latest.json`
/// without publishing anything; nobody sets it on the issuing laptop.
fn updater(app: &AppHandle) -> Result<Updater, String> {
    let mut builder = app.updater_builder();
    if let Ok(url) = std::env::var("APG_UPDATE_URL") {
        let url = url.parse().map_err(|e| format!("APG_UPDATE_URL: {e}"))?;
        builder = builder.endpoints(vec![url]).map_err(|e| e.to_string())?;
    }
    builder.build().map_err(|e| e.to_string())
}

#[derive(Serialize)]
pub struct UpdateInfo {
    version: String,
    current: String,
    notes: Option<String>,
}

/// Is there a newer release? `None` means this is the newest.
#[tauri::command]
pub async fn check_update(app: AppHandle) -> Result<Option<UpdateInfo>, String> {
    let update = updater(&app)?.check().await.map_err(|e| e.to_string())?;
    Ok(update.map(|u| UpdateInfo {
        version: u.version.clone(),
        current: u.current_version.clone(),
        notes: u.body.clone(),
    }))
}

/// Download the newer release, check its signature, and run its
/// installer. On Windows the installer closes this app and opens the new
/// one, so on success this never returns. `progress` gets the percentage.
#[tauri::command]
pub async fn install_update(app: AppHandle, progress: Channel<u32>) -> Result<(), String> {
    let Some(update) = updater(&app)?.check().await.map_err(|e| e.to_string())? else {
        return Err("There is no newer version to install.".into());
    };
    let mut done: u64 = 0;
    update
        .download_and_install(
            |chunk, total| {
                done += chunk as u64;
                if let Some(total) = total.filter(|t| *t > 0) {
                    let _ = progress.send(((done * 100) / total).min(100) as u32);
                }
            },
            || {
                let _ = progress.send(100);
            },
        )
        .await
        .map_err(|e| e.to_string())?;
    // macOS and Linux reach here; Windows has already handed over
    app.restart();
}

/* ---- pen tablets --------------------------------------------------- */

#[derive(Serialize)]
pub struct Pen {
    name: String,
    built_in: bool,
}

/// The pens Windows has registered: drawing tablets, pen displays and
/// the pens of 2-in-1 laptops. Touch screens and touchpads are left out.
///
/// A tablet whose driver is set to act as a mouse (Windows Ink off) is
/// not a pen to Windows and will not be listed; the page also notices a
/// pen the moment one touches it, which covers most of those.
#[tauri::command]
pub fn pen_tablets() -> Vec<Pen> {
    #[cfg(windows)]
    {
        use windows::Win32::UI::Controls::{
            POINTER_DEVICE_INFO, POINTER_DEVICE_TYPE_EXTERNAL_PEN, POINTER_DEVICE_TYPE_INTEGRATED_PEN,
        };
        use windows::Win32::UI::Input::Pointer::GetPointerDevices;

        let mut count = 0u32;
        if unsafe { GetPointerDevices(&mut count, None) }.is_err() || count == 0 {
            return Vec::new();
        }
        let mut devices = vec![POINTER_DEVICE_INFO::default(); count as usize];
        if unsafe { GetPointerDevices(&mut count, Some(devices.as_mut_ptr())) }.is_err() {
            return Vec::new();
        }
        devices.truncate(count as usize);
        devices
            .iter()
            .filter(|d| {
                d.pointerDeviceType == POINTER_DEVICE_TYPE_EXTERNAL_PEN
                    || d.pointerDeviceType == POINTER_DEVICE_TYPE_INTEGRATED_PEN
            })
            .map(|d| {
                let end = d.productString.iter().position(|&c| c == 0).unwrap_or(d.productString.len());
                Pen {
                    name: String::from_utf16_lossy(&d.productString[..end]).trim().to_string(),
                    built_in: d.pointerDeviceType == POINTER_DEVICE_TYPE_INTEGRATED_PEN,
                }
            })
            .collect()
    }
    #[cfg(not(windows))]
    {
        Vec::new()
    }
}

/* ---- holding the cursor in the signature pad ----------------------- */

#[derive(serde::Deserialize)]
pub struct CssRect {
    x: f64,
    y: f64,
    w: f64,
    h: f64,
}

/// Hold the mouse cursor inside `rect` (CSS pixels, from the page's
/// `getBoundingClientRect`), or let it go when `rect` is null.
///
/// The cursor is shared by every program on the computer, so this is
/// released when signing ends, when the window loses focus, and when it
/// closes (see `release_cursor` in main.rs). Ctrl+Alt+Del also frees it.
#[tauri::command]
pub fn clip_cursor(window: WebviewWindow, rect: Option<CssRect>) -> Result<(), String> {
    let Some(r) = rect else {
        release_cursor();
        return Ok(());
    };
    #[cfg(windows)]
    {
        use windows::Win32::Foundation::RECT;
        use windows::Win32::UI::WindowsAndMessaging::ClipCursor;

        let origin = window.inner_position().map_err(|e| e.to_string())?;
        let scale = window.scale_factor().map_err(|e| e.to_string())?;
        // one pixel in from each edge, so the cursor stays over the pad
        let rect = RECT {
            left: origin.x + (r.x * scale).ceil() as i32 + 1,
            top: origin.y + (r.y * scale).ceil() as i32 + 1,
            right: origin.x + ((r.x + r.w) * scale).floor() as i32 - 1,
            bottom: origin.y + ((r.y + r.h) * scale).floor() as i32 - 1,
        };
        if rect.right <= rect.left || rect.bottom <= rect.top {
            return Err("The signature pad is not on screen.".into());
        }
        unsafe { ClipCursor(Some(&rect)) }.map_err(|e| e.to_string())?;
    }
    #[cfg(not(windows))]
    let _ = (window, r);
    Ok(())
}

/// Let the cursor go anywhere again. Safe to call when it is not held.
pub fn release_cursor() {
    #[cfg(windows)]
    unsafe {
        let _ = windows::Win32::UI::WindowsAndMessaging::ClipCursor(None);
    }
}
