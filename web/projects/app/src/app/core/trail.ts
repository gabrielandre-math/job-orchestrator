import { Injectable, inject } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';

export interface TrailEvent { name: string; level?: 'info' | 'warn' | 'error'; detail?: string; ms?: number; code?: string; page?: string; session?: string; }

const session = Math.random().toString(36).slice(2, 10);
const queue: TrailEvent[] = [];
let timer: ReturnType<typeof setTimeout> | null = null;

/** Sends the queued screen events to the local trail: in batches, and once more when the window closes. Never to the internet. */
function flush() {
  timer = null;
  if (!queue.length) return;
  const events = queue.splice(0, 100);
  try { void fetch('/api/diagnostics/client', { method: 'POST', keepalive: true, headers: { 'content-type': 'application/json' }, body: JSON.stringify({ events }) }).catch(() => {}); } catch { /* the trail never breaks the screen */ }
}

const recent = new Map<string, number>();

/** One screen event for the trail (a click, a page, an error, an API call that failed), with the page it happened on. */
export function trail(e: TrailEvent) {
  // The same failure every few seconds (a poll while the API is down) is one event per 15 s, not a flood.
  if (e.level && e.level !== 'info') {
    const key = `${e.name}|${e.code}|${e.detail}`;
    const now = Date.now();
    if (now - (recent.get(key) ?? 0) < 15000) return;
    recent.set(key, now);
  }
  queue.push({ ...e, page: e.page ?? location.pathname, session });
  if (queue.length >= 25) flush();
  else if (!timer) timer = setTimeout(flush, 3000);
}

/** What a clicked control says: its accessible name or its text. Inside [data-private], only that it was clicked. */
function label(el: Element): string {
  if (el.closest('[data-private]')) return '(privado)';
  const name = el.getAttribute('aria-label') ?? el.getAttribute('title') ?? el.textContent ?? '';
  return name.replace(/\s+/g, ' ').trim().slice(0, 80) || el.tagName.toLowerCase();
}

/**
 * Rastro da tela: clicks on buttons, links and options, the pages you open, dialogs opening and closing, JavaScript errors and API
 * calls that failed. What you type never goes in.
 */
@Injectable({ providedIn: 'root' })
export class TrailClient {
  private readonly router = inject(Router);
  private started = false;

  start() {
    if (this.started) return;
    this.started = true;
    window.addEventListener('error', (e) => trail({ name: 'erro de JavaScript', level: 'error', detail: `${e.message} @ ${e.filename?.split('/').pop()}:${e.lineno}` }));
    window.addEventListener('unhandledrejection', (e) => trail({ name: 'promessa rejeitada', level: 'error', detail: String((e.reason as Error)?.message ?? e.reason).slice(0, 300) }));
    document.addEventListener('click', (e) => {
      const el = (e.target as Element | null)?.closest('button, a, [role="radio"], [role="checkbox"], [role="tab"], [role="switch"], summary');
      if (!el) return;
      const disabled = el.hasAttribute('disabled') || el.getAttribute('aria-disabled') === 'true';
      trail({ name: disabled ? 'clique em controle desligado' : 'clique', level: disabled ? 'warn' : 'info', detail: label(el) });
    }, { capture: true });
    // A dialog opened over the page is where a click can get lost: its opening and closing go to the trail.
    new MutationObserver((changes) => {
      for (const c of changes) {
        const d = c.target as HTMLDialogElement;
        if (c.attributeName === 'open' && d.tagName === 'DIALOG') trail({ name: d.open ? 'diálogo aberto' : 'diálogo fechado', detail: d.getAttribute('aria-labelledby') ? document.getElementById(d.getAttribute('aria-labelledby')!)?.textContent?.trim().slice(0, 80) : d.className });
      }
    }).observe(document.body, { attributes: true, attributeFilter: ['open'], subtree: true });
    this.router.events.subscribe((e) => { if (e instanceof NavigationEnd) trail({ name: 'página', detail: e.urlAfterRedirects }); });
    window.addEventListener('pagehide', flush);
    trail({ name: 'painel aberto', detail: `${navigator.userAgent.slice(0, 120)} ${innerWidth}x${innerHeight}` });
  }
}
