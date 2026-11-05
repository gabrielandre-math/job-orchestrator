import { Injectable, signal } from '@angular/core';

/** Tema escolhido na barra. Sem escolha, segue o sistema. Guardado só neste navegador. */
@Injectable({ providedIn: 'root' })
export class Theme {
  readonly mode = signal<'dark' | 'light'>(this.initial());

  toggle() {
    const next = this.mode() === 'dark' ? 'light' : 'dark';
    this.mode.set(next);
    document.documentElement.dataset['theme'] = next;
    try { localStorage.setItem('jo.theme', next); } catch { /* armazenamento indisponível */ }
  }

  private initial(): 'dark' | 'light' {
    let saved: string | null = null;
    try { saved = localStorage.getItem('jo.theme'); } catch { /* armazenamento indisponível */ }
    const mode = saved === 'light' || saved === 'dark' ? saved : matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
    document.documentElement.dataset['theme'] = mode;
    return mode;
  }
}
