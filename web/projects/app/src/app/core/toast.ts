import { Injectable, signal } from '@angular/core';

export interface Toast { id: number; tone: 'ok' | 'bad' | 'info'; text: string }

/** Avisos curtos no canto: confirmam o que acabou de acontecer e somem sozinhos em 4 s. */
@Injectable({ providedIn: 'root' })
export class Toasts {
  readonly list = signal<Toast[]>([]);
  private next = 0;

  ok(text: string) { this.push('ok', text); }
  bad(text: string) { this.push('bad', text); }
  info(text: string) { this.push('info', text); }
  dismiss(id: number) { this.list.update((l) => l.filter((t) => t.id !== id)); }

  private push(tone: Toast['tone'], text: string) {
    const id = ++this.next;
    this.list.update((l) => [...l.slice(-3), { id, tone, text }]);
    setTimeout(() => this.dismiss(id), 4000);
  }
}
