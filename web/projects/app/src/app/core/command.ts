import { Signal, signal } from '@angular/core';
import { isProblem, Problem } from './api';

/** Comando (CQRS) no cliente: uma ação que muda algo no servidor, com estado de execução e de erro em signals. */
export interface Command<A, T> {
  readonly running: Signal<boolean>;
  readonly problem: Signal<Problem | null>;
  run(argument: A): Promise<T | null>;
  clear(): void;
}

export function command<A, T>(call: (argument: A) => Promise<T>): Command<A, T> {
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
        return await call(argument);
      } catch (e) {
        problem.set(isProblem(e) ? e : { status: 0, code: 'offline', detail: 'A API não respondeu. Confira se o serviço está no ar.' });
        return null;
      } finally {
        running.set(false);
      }
    },
    clear: () => problem.set(null),
  };
}
