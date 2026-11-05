import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TECH } from './brands';

/** Tecnologia com o ícone oficial (Simple Icons) ou monograma. have: você tem no perfil (true), não tem (false) ou sem perfil (null). */
@Component({
  selector: 'jo-tech',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (icon(); as i) { <img [src]="'/logos/tech/' + i.file + '.svg'" [class.dark]="i.dark" alt="" [width]="size()" [height]="size()" /> }
    @else { <span class="mono-ic" [style.width.px]="size()" [style.height.px]="size()">{{ initials() }}</span> }
    @if (label()) { <span class="name">{{ name() }}</span> }`,
  styles: `
    :host { display: inline-flex; align-items: center; gap: 6px; padding: 3px 8px 3px 5px; border-radius: 4px; background: var(--surface-2); border: 1px solid var(--line); font: 500 12px var(--font-ui); color: var(--text); white-space: nowrap; }
    :host(.have) { border-color: color-mix(in oklch, var(--ok) 45%, var(--line)); }
    :host(.missing) { opacity: 0.6; border-style: dashed; }
    :host(.bare) { padding: 0; background: none; border: 0; }
    img { display: block; flex: none; }
    :host-context(:root:not([data-theme="light"])) img.dark { filter: invert(1) hue-rotate(180deg); }
    .mono-ic { display: grid; place-items: center; flex: none; border-radius: 3px; background: var(--surface-3); color: var(--text-2); font: 700 9px var(--font-mono); letter-spacing: -0.02em; }
  `,
  host: { '[class.have]': 'have() === true', '[class.missing]': 'have() === false', '[class.bare]': '!label()', '[attr.title]': 'name() + (have() === true ? ", você tem" : have() === false ? ", não está no seu perfil" : "")' },
})
export class Tech {
  readonly name = input.required<string>();
  readonly size = input(16);
  readonly label = input(true);
  readonly have = input<boolean | null>(null);
  protected readonly icon = computed(() => TECH[this.name()] ?? null);
  protected readonly initials = computed(() => this.name().replace(/[^A-Za-z0-9#+]/g, '').slice(0, 3));
}
