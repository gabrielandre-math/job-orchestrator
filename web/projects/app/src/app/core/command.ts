import { Signal, inject, signal } from '@angular/core';
import { Toasts } from './toast';
import { isProblem, Problem } from './api';

/** Comando (CQRS) no cliente: uma ação que muda algo no servidor, com estado de execução e de erro em signals. */
export interface Command<A, T> {
  readonly running: Signal<boolean>;
  readonly problem: Signal<Problem | null>;
  run(argument: A): Promise<T | null>;
  clear(): void;
}

/** success: texto do aviso quando der certo. Erro sempre vira aviso também, além de ficar em problem. */
export function command<A, T>(call: (argument: A) => Promise<T>, success?: string | ((result: T) => string)): Command<A, T> {
  const toasts = inject(Toasts);
  const running = signal(false);
  const problem = signal<Problem | null>(null);
  return {
    running: running.asReadonly(),
    problem: problem.asReadonly(),
    async run(argument: A) {
      if (running()) return null;
      running.set(true);
      problem.set(null);
      try {
        const result = await call(argument);
        if (success) toasts.ok(typeof success === 'function' ? success(result) : success);
        return result;
      } catch (e) {
        const p = isProblem(e) ? e : { status: 0, code: 'offline', detail: 'A API não respondeu. Confira se o serviço está no ar.' };
        problem.set(p);
        toasts.bad(p.detail);
        return null;
      } finally {
        running.set(false);
      }
    },
    clear: () => problem.set(null),
  };
}
