import { Injectable, signal } from '@angular/core';
import { send } from './api';
import { query } from './query';

export interface CopilotStatus { connected: boolean; seenAt: string | null; version: string | null; extensionId: string }
export interface CopilotQueueItem { id: string; company: string; title: string; applyUrl: string; score: number; hasResume: boolean; updatedAt: string }
export interface CopilotExtension { path: string; exists: boolean; id: string }

/** The extension's id, fixed by the key in its manifest (the same in the app window and in your Chrome). */
export const COPILOT_ID = 'ghclhgdehejmhbcfejcgglhlkjbibmni';

type Runtime = { sendMessage: (id: string, message: unknown, reply: (r: unknown) => void) => void; lastError?: unknown };
const runtime = (): Runtime | null => (globalThis as { chrome?: { runtime?: Runtime } }).chrome?.runtime ?? null;

/**
 * The browser copilot: whether the extension answers the app, and opening a job that goes by the site already filled. In a browser
 * that has the extension (the app window has it; your Chrome, once installed) the extension opens the tabs, as many as asked; anywhere
 * else this page opens the job and the extension, if that browser has it, fills it.
 */
@Injectable({ providedIn: 'root' })
export class BrowserCopilot {
  readonly status = query<CopilotStatus>(() => '/copilot/status', 30000);
  readonly queue = query<CopilotQueueItem[]>(() => '/copilot/queue', 30000);
  /** The extension lives in this very browser. */
  readonly here = signal(false);

  constructor() {
    const rt = runtime();
    if (!rt?.sendMessage) return;
    try { rt.sendMessage(COPILOT_ID, { type: 'ping' }, (r) => { void rt.lastError; this.here.set(!!r); }); } catch { /* no extension in this browser */ }
  }

  /** One job, already filled. Without the extension here, the click itself opens the tab (a browser lets a page open one per click). */
  open(item: { id: string; applyUrl?: string | null }) {
    const url = item.applyUrl ?? this.queue.value()?.find((q) => q.id === item.id)?.applyUrl;
    if (this.here() || !url) { void this.openMany([item.id]); return; }
    void send('POST', `/copilot/open/${item.id}`).catch(() => undefined);
    window.open(url, '_blank', 'noopener');
  }

  /** Several jobs at once, each in its own tab, through the extension. Resolves with how many opened. */
  openMany(ids: string[]): Promise<number> {
    const rt = runtime();
    if (!rt?.sendMessage) return Promise.resolve(0);
    return new Promise((resolve) => {
      try { rt.sendMessage(COPILOT_ID, { type: 'open', items: ids.slice(0, 10).map((id) => ({ id })) }, (r) => { void rt.lastError; resolve((r as { opened?: number } | null)?.opened ?? 0); }); }
      catch { resolve(0); }
    });
  }
}
