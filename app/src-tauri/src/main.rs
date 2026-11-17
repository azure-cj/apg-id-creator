// No console window behind the app in a release build.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

//! A window around the ID Maker.
//!
//! It draws nothing itself. The card, the photo pipeline and the PDF all
//! come from the one HTML file, exactly as they do in a browser, so the
//! golden-render test still governs what is printed.
//!
//! This replaces a WinForms + WebView2 shell that did the same job in
//! C#. On Windows, Tauri renders in WebView2 too, so the engine under
//! the card has not changed — only the window around it.

mod desktop;

use tauri::webview::{DownloadEvent, WebviewWindowBuilder};
use tauri::{RunEvent, Url, WebviewUrl, WindowEvent};
use tauri_plugin_opener::OpenerExt;

/// Is this somewhere the app window is allowed to go?
///
/// Everything the app itself serves, plus the blob: and data: URLs its
/// own downloads and previews use. Anything else is the web, and the web
/// belongs in a real browser: an application window that can be
/// navigated away from is no longer an application window.
fn is_ours(url: &Url) -> bool {
    match url.scheme() {
        // what Tauri serves the app from, and what the app makes itself
        "tauri" | "blob" | "data" | "about" => true,
        // on Windows the app is served from http://tauri.localhost
        "http" | "https" => url
            .host_str()
            .is_some_and(|h| h == "tauri.localhost" || h.ends_with(".localhost")),
        _ => false,
    }
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_opener::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .invoke_handler(tauri::generate_handler![
            desktop::check_update,
            desktop::install_update,
            desktop::pen_tablets,
            desktop::clip_cursor,
        ])
        // The cursor is everybody's: never keep it held once this window
        // is not the one being used.
        .on_window_event(|_window, event| {
            if matches!(event, WindowEvent::Focused(false) | WindowEvent::CloseRequested { .. } | WindowEvent::Destroyed) {
                desktop::release_cursor();
            }
        })
        .setup(|app| {
            let for_links = app.handle().clone();
            let for_saves = app.handle().clone();

            WebviewWindowBuilder::new(app, "main", WebviewUrl::App("index.html".into()))
                .title("Alpha Premier ID Maker")
                .inner_size(1280.0, 900.0)
                .min_inner_size(960.0, 700.0)
                .center()
                .on_navigation(move |url| {
                    if is_ours(url) {
                        return true;
                    }
                    if url.scheme() == "https" {
                        let _ = for_links.opener().open_url(url.as_str(), None::<&str>);
                    }
                    false
                })
                .on_download(move |_webview, event| {
                    // The PDFs, the batch ZIP and the register CSV all
                    // arrive this way. Saving them silently would leave
                    // the operator with no idea where the print went, so
                    // show the finished file in Explorer and let them
                    // decide what to do with it.
                    if let DownloadEvent::Finished {
                        path: Some(path),
                        success: true,
                        ..
                    } = event
                    {
                        let _ = for_saves.opener().reveal_item_in_dir(path);
                    }
                    true
                })
                .build()?;

            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("the ID Maker window could not be created")
        .run(|_app, event| {
            if matches!(event, RunEvent::Exit | RunEvent::ExitRequested { .. }) {
                desktop::release_cursor();
            }
        });
}
