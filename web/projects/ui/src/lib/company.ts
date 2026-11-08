import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { BRANDS } from './brands';

/**
 * Logo da empresa: a marca oficial quando o Simple Icons tem, senão o ícone do site (a API busca e guarda), senão um monograma
 * sóbrio com as iniciais. O tom do monograma vem do nome, então a mesma empresa tem sempre a mesma cor.
 */
@Component({
  selector: 'jo-company',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `@if (brand(); as b) { <img [src]="'/logos/brands/' + b.file + '.svg'" [class.dark]="b.dark" alt="" class="brand" /> }
    @else if (host() && !failed()) { <img [src]="'/api/companies/' + host() + '/logo'" alt="" class="site" (error)="failed.set(true)" /> }
    @else { <span class="mono" [style.--h]="hue()">{{ initials() }}</span> }`,
  styles: `
    :host { display: grid; place-items: center; flex: none; border-radius: 6px; background: var(--surface-2); border: 1px solid var(--line); overflow: hidden; }
    img { display: block; }
    .brand { width: 62%; height: 62%; }
    .site { width: 70%; height: 70%; border-radius: 3px; object-fit: contain; }
    :host-context(:root:not([data-theme="light"])) img.dark { filter: invert(1) hue-rotate(180deg); }
    .mono { width: 100%; height: 100%; display: grid; place-items: center; font: 650 calc(var(--s) * 0.36) var(--font-ui); letter-spacing: -0.02em;
      color: oklch(85% 0.06 var(--h)); background: oklch(30% 0.04 var(--h)); }
    :host-context([data-theme="light"]) .mono { color: oklch(40% 0.08 var(--h)); background: oklch(94% 0.03 var(--h)); }
  `,
  host: { '[style.width.px]': 'size()', '[style.height.px]': 'size()', '[style.--s]': 'size() + "px"', '[attr.aria-label]': 'name()', role: 'img' },
})
export class Company {
  readonly name = input.required<string>();
  readonly domain = input<string | null>(null);
  readonly size = input(40);
  protected readonly failed = signal(false);
  /** A career page key ("vetta.com.br/en/careers") or a full address still names one site: the logo is its host's. */
  protected readonly host = computed(() => this.domain()?.replace(/^https?:\/\//i, '').split(/[/?#]/)[0].toLowerCase() || null);

  private readonly clean = computed(() => this.name().replace(/\s+-\s+linkedin$/i, '').replace(/\(.*?\)/g, '').replace(/\b(s\.?a\.?|ltda|inc\.?|llc|ltd)\b/gi, '').trim());
  protected readonly brand = computed(() => {
    const words = this.clean().split(/\s+/);
    return BRANDS[fold(this.clean())] ?? BRANDS[fold(words[0] ?? '')] ?? BRANDS[fold(words.slice(0, 2).join(''))] ?? null;
  });
  protected readonly initials = computed(() => {
    const w = this.clean().split(/\s+/).filter((x) => /[A-Za-zÀ-ÿ0-9]/.test(x));
    return ((w[0]?.[0] ?? '?') + (w.length > 1 ? (w[1][0] ?? '') : (w[0]?.[1] ?? ''))).toUpperCase();
  });
  protected readonly hue = computed(() => [...this.clean()].reduce((h, c) => (h * 31 + c.charCodeAt(0)) % 360, 7));
}

function fold(s: string) { return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9]/g, ''); }
