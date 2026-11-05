import { DestroyRef, Signal, effect, inject, signal, untracked } from '@angular/core';
import { get, isProblem, Problem } from './api';

/** Consulta (CQRS) no cliente: lê, guarda o último valor e relê no intervalo enquanto a aba está visível. */
export interface Query<T> {
  readonly value: Signal<T | undefined>;
  readonly problem: Signal<Problem | null>;
  readonly loading: Signal<boolean>;
  reload(): Promise<void>;
}

export function query<T>(path: () => string | null, every = 0): Query<T> {
  const destroy = inject(DestroyRef);
  const value = signal<T | undefined>(undefined);
  const problem = signal<Problem | null>(null);
  const loading = signal(true);
  let controller: AbortController | null = null;
  let current: string | null = null;

  const load = async () => {
    const p = current;
    if (!p) return;
    controller?.abort();
    controller = new AbortController();
    try {
      const data = await get<T>(p, controller.signal);
      if (p === current) { value.set(data); problem.set(null); }
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') return;
      problem.set(isProblem(e) ? e : { status: 0, code: 'offline', detail: 'A API não respondeu. Confira se o serviço está no ar.' });
    } finally {
      loading.set(false);
    }
  };

  effect(() => {
    const p = path();
    untracked(() => {
      if (p !== current) { current = p; loading.set(true); value.set(undefined); }
      void load();
    });
  });
  if (every > 0) {
    const timer = setInterval(() => { if (document.visibilityState === 'visible') void load(); }, every);
    destroy.onDestroy(() => clearInterval(timer));
  }
  destroy.onDestroy(() => controller?.abort());
  return { value: value.asReadonly(), problem: problem.asReadonly(), loading: loading.asReadonly(), reload: load };
}
