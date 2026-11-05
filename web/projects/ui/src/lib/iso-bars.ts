import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';

export interface IsoBar { label: string; value: number; tone?: 'accent' | 'base' | 'ok' | 'warn'; logo?: string | null; mono?: boolean; hint?: string }

const C = Math.cos(Math.PI / 6);
const P = (x: number, y: number, z: number) => [(x - y) * C, (x + y) * 0.5 - z] as const;

/**
 * Gráfico de colunas isométricas: cada coluna é um bloco com topo, esquerda e direita, na mesma luz dos ícones.
 * As colunas crescem ao aparecer (uma depois da outra) e quando os números mudam. Rótulos e valores em HTML, para ler bem.
 */
@Component({
  selector: 'jo-iso-bars',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="cols" [style.--n]="data().length">
      @for (b of bars(); track b.label; let i = $index) {
        <div class="col" [attr.title]="b.hint ?? (b.label + ': ' + b.value)">
          <span class="v num">{{ b.value }}</span>
          <svg [attr.viewBox]="'-14 ' + (-max() - 2) + ' 28 ' + (max() + 18)" preserveAspectRatio="xMidYMax meet" aria-hidden="true">
            <path [attr.d]="b.left" [attr.class]="'f l ' + (b.tone ?? 'base')" />
            <path [attr.d]="b.right" [attr.class]="'f r ' + (b.tone ?? 'base')" />
            <path [attr.d]="b.top" [attr.class]="'f t ' + (b.tone ?? 'base')" />
          </svg>
          @if (b.logo) {
            @if (b.mono) { <span class="logo mono" [style.--logo]="'url(' + b.logo + ')'"></span> } @else { <img class="logo" [src]="b.logo" alt="" /> }
          }
          <span class="k">{{ b.label }}</span>
        </div>
      }
    </div>`,
  styles: `
    :host { display: block; }
    .cols { display: grid; grid-template-columns: repeat(var(--n), minmax(0, 1fr)); gap: 4px; align-items: end; height: 100%; }
    .col { display: grid; justify-items: center; align-content: end; gap: 3px; min-width: 0; }
    svg { width: 100%; max-width: 56px; height: var(--bar-h, 120px); overflow: visible; }
    .v { font: 600 11.5px var(--font-mono); color: var(--text); }
    .k { font: 500 11px var(--font-ui); color: var(--text-3); text-align: center; max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .logo { width: 14px; height: 14px; }
    .logo.mono { background: var(--text-2); mask: var(--logo) center / contain no-repeat; -webkit-mask: var(--logo) center / contain no-repeat; }
    .f { stroke: var(--iso-edge); stroke-width: 0.4; stroke-linejoin: round; }
    .t.base { fill: var(--iso-paper-t); } .l.base { fill: var(--iso-paper-l); } .r.base { fill: var(--iso-paper-r); }
    .t.accent { fill: var(--iso-accent-t); } .l.accent { fill: var(--iso-accent-l); } .r.accent { fill: var(--iso-accent-r); }
    .t.ok { fill: oklch(80% 0.13 155); } .l.ok { fill: oklch(66% 0.13 155); } .r.ok { fill: oklch(56% 0.12 155); }
    .t.warn { fill: oklch(84% 0.12 80); } .l.warn { fill: oklch(72% 0.13 75); } .r.warn { fill: oklch(62% 0.12 70); }
  `,
})
export class IsoBars {
  private readonly destroy = inject(DestroyRef);
  readonly data = input.required<IsoBar[]>();
  /** Altura da coluna mais alta, em unidades do desenho. */
  readonly max = input(60);
  private readonly progress = signal<number[]>([]);
  private frame = 0;
  private key = '';

  protected readonly bars = computed(() => {
    const data = this.data();
    const top = Math.max(1, ...data.map((d) => d.value));
    const p = this.progress();
    return data.map((d, i) => {
      const h = Math.max(1.5, (d.value / top) * this.max() * (p[i] ?? 0));
      const w = 10;
      const shape = (pts: (readonly [number, number])[]) => 'M ' + pts.map(([x, y]) => `${x.toFixed(2)} ${y.toFixed(2)}`).join(' L ') + ' Z';
      // Bloco centrado: a frente de baixo fica em (0, 10).
      const o = (x: number, y: number, z: number) => { const [sx, sy] = P(x - w / 2, y - w / 2, z); return [sx, sy + 5] as const; };
      return {
        ...d,
        left: shape([o(0, w, 0), o(w, w, 0), o(w, w, h), o(0, w, h)]),
        right: shape([o(w, 0, 0), o(w, w, 0), o(w, w, h), o(w, 0, h)]),
        top: shape([o(0, 0, h), o(w, 0, h), o(w, w, h), o(0, w, h)]),
      };
    });
  });

  constructor() {
    this.destroy.onDestroy(() => cancelAnimationFrame(this.frame));
    effect(() => {
      const data = this.data();
      const n = data.length;
      // A lista atualiza sozinha a cada poucos segundos: só anima de novo quando os números mudam.
      const key = data.map((d) => `${d.label}:${d.value}`).join('|');
      if (key === this.key) return;
      this.key = key;
      cancelAnimationFrame(this.frame);
      if (typeof matchMedia !== 'undefined' && matchMedia('(prefers-reduced-motion: reduce)').matches) { this.progress.set(Array(n).fill(1)); return; }
      const start = performance.now();
      const step = () => {
        const t = performance.now() - start;
        const p = Array.from({ length: n }, (_, i) => { const x = Math.min(1, Math.max(0, (t - i * 45) / 650)); return 1 - Math.pow(1 - x, 3); });
        this.progress.set(p);
        if (p.some((x) => x < 1)) this.frame = requestAnimationFrame(step);
      };
      this.frame = requestAnimationFrame(step);
    });
  }
}
