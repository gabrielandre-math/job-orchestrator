import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Selo de estado: ponto colorido e rótulo. A cor vem do tom; o texto carrega o significado. */
@Component({
  selector: 'jo-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="dot"></span><span>{{ label() }}</span>`,
  styles: `
    :host { display: inline-flex; align-items: center; gap: 6px; height: 22px; padding: 0 8px; border-radius: 4px; font: 500 11.5px var(--font-ui); white-space: nowrap; background: var(--surface-3); color: var(--text-2); }
    .dot { width: 6px; height: 6px; border-radius: 50%; background: currentColor; }
    :host(.ok) { background: var(--ok-weak); color: var(--ok); }
    :host(.warn) { background: var(--warn-weak); color: var(--warn); }
    :host(.danger) { background: var(--danger-weak); color: var(--danger); }
    :host(.info) { background: var(--info-weak); color: var(--info); }
    :host(.accent) { background: var(--accent-weak); color: var(--accent); }
  `,
  host: { '[class]': 'tone()' },
})
export class Badge {
  readonly label = input.required<string>();
  readonly tone = input<string>('neutral');
}
