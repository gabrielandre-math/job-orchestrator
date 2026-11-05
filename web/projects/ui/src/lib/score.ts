import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Nota de 0 a 100 com barra curta. Sem nota, mostra traço. */
@Component({
  selector: 'jo-score',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (value() !== null) {<span class="num">{{ value() }}</span><span class="bar"><span [style.width.%]="value()" [class]="level()"></span></span>} @else {<span class="num faint">–</span>}`,
  styles: `
    :host { display: inline-flex; align-items: center; gap: 8px; }
    .num { font: 500 12.5px var(--font-mono); min-width: 22px; text-align: right; }
    .bar { width: 44px; height: 4px; border-radius: 2px; background: var(--surface-3); overflow: hidden; }
    .bar > span { display: block; height: 100%; background: var(--text-3); }
    .bar > .high { background: var(--ok); }
    .bar > .mid { background: var(--warn); }
  `,
})
export class Score {
  readonly value = input<number | null>(null);
  protected readonly level = computed(() => ((this.value() ?? 0) >= 80 ? 'high' : (this.value() ?? 0) >= 60 ? 'mid' : 'low'));
}
