/* ------------------------------------------------------------------
   The desktop window, seen from the page
   ------------------------------------------------------------------
   In the Tauri window (app/) the page can ask for things a browser tab
   cannot have: updates from GitHub, the list of pen tablets plugged in,
   filling the screen while signing, and holding the cursor on the
   signing board. The Rust side is
   app/src-tauri/src/desktop.rs.

   In a browser, or the development page, Desktop.available is false and
   every call quietly does nothing, so the app works the same without it.
   ------------------------------------------------------------------ */

interface DesktopUpdate {
  version: string;
  current: string;
  notes: string | null;
}

interface DesktopPen {
  name: string;
  built_in: boolean;
}

interface TauriGlobal {
  core: {
    invoke<T>(cmd: string, args?: Record<string, unknown>): Promise<T>;
    Channel: new <T>() => { onmessage: (message: T) => void };
  };
}

const Desktop = {
  get tauri(): TauriGlobal | null {
    return (window as unknown as { __TAURI__?: TauriGlobal }).__TAURI__ ?? null;
  },

  /** True in the Tauri window, false in a browser. */
  get available(): boolean {
    return !!this.tauri;
  },

  /** A newer release, or null when this is the newest. Throws when it cannot tell. */
  async checkUpdate(): Promise<DesktopUpdate | null> {
    if (!this.tauri) return null;
    return this.tauri.core.invoke<DesktopUpdate | null>('check_update');
  },

  /**
   * Download and install the newer release. On Windows the installer
   * closes this window and opens the new version, so a resolved promise
   * is not expected; a rejected one carries the reason.
   */
  async installUpdate(onProgress: (percent: number) => void): Promise<void> {
    if (!this.tauri) throw new Error('Updates come with the desktop app.');
    const progress = new this.tauri.core.Channel<number>();
    progress.onmessage = onProgress;
    await this.tauri.core.invoke('install_update', { progress });
  },

  /** The pen tablets Windows has registered. Empty in a browser. */
  async penTablets(): Promise<DesktopPen[]> {
    if (!this.tauri) return [];
    try { return await this.tauri.core.invoke<DesktopPen[]>('pen_tablets'); } catch { return []; }
  },

  /** Fill the screen (signing mode), or go back to the window as it was. */
  async fullScreen(on: boolean): Promise<void> {
    if (!this.tauri) return;
    try { await this.tauri.core.invoke('full_screen', { on }); } catch { /* signing still works in the window */ }
  },

  /** Hold the cursor inside an element's box, or let it go with null. */
  async holdCursor(el: Element | null): Promise<void> {
    if (!this.tauri) return;
    const r = el?.getBoundingClientRect();
    const rect = r ? { x: r.left, y: r.top, w: r.width, h: r.height } : null;
    try { await this.tauri.core.invoke('clip_cursor', { rect }); } catch { /* the pad is off screen: leave the cursor free */ }
  },
};
