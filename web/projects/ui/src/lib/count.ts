import { ChangeDetectionStrategy, Component, effect, input, signal } from '@angular/core';

/** Número que conta até o valor quando ele muda (700 ms, saída suave). Com movimento reduzido no sistema, vai direto. */
@Component({
  selector: 'jo-count',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `{{ shown().toLocaleString('pt-BR') }}`,
  styles: `:host { font-variant-numeric: tabular-nums; }`,
})
export class Count {
  readonly value = input(0);
  protected readonly shown = signal(0);
  private frame = 0;

  constructor() {
    effect(() => {
      const to = this.value();
      const from = this.shown();
      cancelAnimationFrame(this.frame);
      if (typeof matchMedia !== 'undefined' && matchMedia('(prefers-reduced-motion: reduce)').matches) { this.shown.set(to); return; }
      const start = performance.now();
      const step = () => {
        const p = Math.min(1, (performance.now() - start) / 700);
        this.shown.set(Math.round(from + (to - from) * (1 - Math.pow(1 - p, 3))));
        if (p < 1) this.frame = requestAnimationFrame(step);
      };
      this.frame = requestAnimationFrame(step);
    });
  }
}
