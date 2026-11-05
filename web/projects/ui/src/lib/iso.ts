import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { isoArt } from './iso-art';

/** Ícone isométrico de estação (ver iso-art.ts). Cores pelos tokens --iso-*, que mudam com o tema. */
@Component({
  selector: 'jo-iso',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<svg [attr.viewBox]="art().viewBox" [attr.width]="size()" [attr.height]="size() * 62 / 68" aria-hidden="true" focusable="false">
    @for (f of art().faces; track $index) { <path [attr.d]="f.d" [attr.class]="f.cls" [attr.stroke-width]="f.w ?? null" /> }
  </svg>`,
  styles: `
    :host { display: inline-flex; flex: none; }
    svg { display: block; overflow: visible; }
    .iso-f { stroke: var(--iso-edge); stroke-width: 0.5; stroke-linejoin: round; }
    .iso-line { fill: none; stroke-linecap: round; stroke-linejoin: round; }
    .iso-t.iso-base { fill: var(--iso-base-t); } .iso-l.iso-base { fill: var(--iso-base-l); } .iso-r.iso-base { fill: var(--iso-base-r); }
    .iso-t.iso-paper { fill: var(--iso-paper-t); } .iso-l.iso-paper { fill: var(--iso-paper-l); } .iso-r.iso-paper { fill: var(--iso-paper-r); }
    .iso-t.iso-dark { fill: var(--iso-dark-t); } .iso-l.iso-dark { fill: var(--iso-dark-l); } .iso-r.iso-dark { fill: var(--iso-dark-r); }
    .iso-t.iso-accent { fill: var(--iso-accent-t); } .iso-l.iso-accent { fill: var(--iso-accent-l); } .iso-r.iso-accent { fill: var(--iso-accent-r); }
    .iso-line.iso-ink { stroke: var(--iso-ink); } .iso-line.iso-soft { stroke: var(--iso-soft); } .iso-line.iso-accent { stroke: var(--iso-accent-line); }
  `,
})
export class Iso {
  readonly name = input.required<string>();
  readonly size = input(64);
  protected readonly art = computed(() => isoArt(this.name()));
}
