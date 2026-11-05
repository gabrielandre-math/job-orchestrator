import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export interface ChartRow { label: string; value: number; logo?: string | null; mono?: boolean; tone?: 'accent' | 'ok' | 'warn' | 'muted'; hint?: string }

/** Barras horizontais: rótulo (com logo, se houver), trilho com a barra crescendo e o valor. A maior barra ocupa o trilho todo. */
@Component({
  selector: 'jo-hbars',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @for (r of rows(); track r.label; let i = $index) {
      <div class="row" [attr.title]="r.hint ?? (r.label + ': ' + r.value)">
        <span class="k">
          @if (r.logo) {
            @if (r.mono) { <span class="logo mono" [style.--logo]="'url(' + r.logo + ')'"></span> } @else { <img class="logo" [src]="r.logo" alt="" /> }
          }
          <span class="text">{{ r.label }}</span>
        </span>
        <span class="track"><span class="fill" [class]="'fill ' + (r.tone ?? '')" [style.width.%]="r.pct" [style.animation-delay.ms]="i * 50"></span></span>
        <span class="v num">{{ r.value }}</span>
      </div>
    }`,
  styles: `
    :host { display: grid; gap: 9px; }
    .row { display: grid; grid-template-columns: minmax(0, 120px) minmax(0, 1fr) 40px; gap: 10px; align-items: center; }
    .k { display: flex; align-items: center; gap: 7px; min-width: 0; font-size: 12.5px; color: var(--text-2); }
    .text { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .logo { width: 15px; height: 15px; flex: none; }
    .logo.mono { background: var(--text-2); mask: var(--logo) center / contain no-repeat; -webkit-mask: var(--logo) center / contain no-repeat; }
    .track { height: 8px; border-radius: 2px; background: var(--surface-3); overflow: hidden; }
    .fill { display: block; height: 100%; border-radius: 2px; background: color-mix(in oklch, var(--text-2) 55%, transparent); transform-origin: left; animation: grow 0.8s cubic-bezier(0.2, 0.8, 0.2, 1) both; }
    .fill.accent { background: var(--accent); }
    .fill.ok { background: var(--ok); }
    .fill.warn { background: var(--warn); }
    .fill.muted { background: var(--text-3); opacity: 0.6; }
    .v { text-align: right; font: 600 12.5px var(--font-mono); color: var(--text); }
    @keyframes grow { from { transform: scaleX(0); } }
    @media (prefers-reduced-motion: reduce) { .fill { animation: none; } }
  `,
})
export class HBars {
  readonly data = input.required<ChartRow[]>();
  protected readonly rows = computed(() => {
    const max = Math.max(1, ...this.data().map((d) => d.value));
    return this.data().map((d) => ({ ...d, pct: Math.max(d.value > 0 ? 2 : 0, (d.value / max) * 100) }));
  });
}

/** Colunas (histograma): barras finas, linha de corte opcional (índice da coluna onde o corte começa) e rótulos embaixo. */
@Component({
  selector: 'jo-columns',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="plot" [style.--n]="data().length">
      @if (cutAt() !== null) { <span class="cut" [style.left.%]="(cutAt()! / data().length) * 100"><span class="cut-label">{{ cutLabel() }}</span></span> }
      @for (c of cols(); track c.label; let i = $index) {
        <div class="col" [attr.title]="c.hint ?? (c.label + ': ' + c.value)">
          <span class="v num">{{ c.value || '' }}</span>
          <span class="bar" [class]="'bar ' + (c.tone ?? '')" [style.height.%]="c.pct" [style.animation-delay.ms]="i * 35"></span>
        </div>
      }
    </div>
    <div class="labels" [style.--n]="data().length">@for (c of data(); track c.label) { <span>{{ c.label }}</span> }</div>`,
  styles: `
    :host { display: grid; gap: 6px; }
    .plot { position: relative; display: grid; grid-template-columns: repeat(var(--n), minmax(0, 1fr)); gap: 6px; align-items: end; height: var(--plot-h, 130px);
      background: repeating-linear-gradient(to top, transparent 0, transparent calc(25% - 1px), var(--line) calc(25% - 1px), var(--line) 25%); }
    .col { position: relative; display: grid; grid-template-rows: auto minmax(0, 1fr); justify-items: center; height: 100%; align-items: end; }
    .v { font: 600 11px var(--font-mono); color: var(--text-2); align-self: end; margin-bottom: 3px; }
    .bar { width: min(100%, 22px); border-radius: 2px 2px 0 0; background: color-mix(in oklch, var(--text-2) 45%, transparent); transform-origin: bottom; animation: rise 0.8s cubic-bezier(0.2, 0.8, 0.2, 1) both; align-self: end; min-height: 0; }
    .bar.accent { background: var(--accent); }
    .bar.ok { background: var(--ok); }
    .labels { display: grid; grid-template-columns: repeat(var(--n), minmax(0, 1fr)); gap: 6px; font: 500 10.5px var(--font-mono); color: var(--text-3); text-align: center; }
    .labels span { overflow: hidden; text-overflow: clip; white-space: nowrap; }
    .cut { position: absolute; top: 0; bottom: 0; border-left: 1px dashed color-mix(in oklch, var(--accent) 70%, transparent); z-index: 1; pointer-events: none; }
    .cut-label { position: absolute; top: -2px; left: 4px; font: 500 10.5px var(--font-ui); color: var(--accent); white-space: nowrap; }
    @keyframes rise { from { transform: scaleY(0); } }
    @media (prefers-reduced-motion: reduce) { .bar { animation: none; } }
  `,
})
export class Columns {
  readonly data = input.required<ChartRow[]>();
  readonly cutAt = input<number | null>(null);
  readonly cutLabel = input('');
  protected readonly cols = computed(() => {
    const max = Math.max(1, ...this.data().map((d) => d.value));
    return this.data().map((d) => ({ ...d, pct: d.value > 0 ? Math.max(3, (d.value / max) * 82) : 0 }));
  });
}

/** Série no tempo: linha suave, área em degradê, pontos e o último ponto em destaque. A linha se desenha ao aparecer. */
@Component({
  selector: 'jo-area',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="0 0 300 110" preserveAspectRatio="none" aria-hidden="true">
      <defs>
        <linearGradient [attr.id]="id" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0" stop-color="var(--accent)" stop-opacity="0.32" />
          <stop offset="1" stop-color="var(--accent)" stop-opacity="0" />
        </linearGradient>
      </defs>
      @for (y of [27, 54, 81]; track y) { <line x1="0" x2="300" [attr.y1]="y" [attr.y2]="y" class="grid" /> }
      <path [attr.d]="shape().area" [attr.fill]="'url(#' + id + ')'" class="area" />
      <path [attr.d]="shape().line" class="line" pathLength="100" />
    </svg>
    <div class="dots">
      @for (p of shape().points; track $index; let last = $last) {
        <span class="dot" [class.last]="last" [style.left.%]="p.x / 3" [style.top.%]="(p.y / 110) * 100" [attr.title]="p.hint"></span>
      }
    </div>
    <div class="axis"><span>{{ data()[0]?.label }}</span><span>{{ data().at(-1)?.label }}</span></div>`,
  styles: `
    :host { position: relative; display: grid; gap: 6px; }
    svg { width: 100%; height: var(--plot-h, 130px); display: block; overflow: visible; }
    .grid { stroke: var(--line); stroke-width: 1; vector-effect: non-scaling-stroke; }
    .line { fill: none; stroke: var(--accent); stroke-width: 2; vector-effect: non-scaling-stroke; stroke-linejoin: round; stroke-linecap: round;
      stroke-dasharray: 100; animation: draw 1.1s cubic-bezier(0.65, 0, 0.35, 1) both; }
    .area { animation: fadein 1.1s ease-out both; }
    .dots { position: absolute; inset: 0 0 18px 0; pointer-events: none; }
    .dot { position: absolute; width: 5px; height: 5px; margin: -2.5px 0 0 -2.5px; border-radius: 50%; background: var(--surface); border: 1.5px solid var(--accent); pointer-events: auto; }
    .dot.last { width: 9px; height: 9px; margin: -4.5px 0 0 -4.5px; background: var(--accent); box-shadow: 0 0 0 4px var(--accent-weak); }
    .axis { display: flex; justify-content: space-between; font: 500 10.5px var(--font-mono); color: var(--text-3); }
    @keyframes draw { from { stroke-dashoffset: 100; } to { stroke-dashoffset: 0; } }
    @keyframes fadein { from { opacity: 0; } }
    @media (prefers-reduced-motion: reduce) { .line, .area { animation: none; } }
  `,
})
export class Area {
  private static next = 0;
  readonly data = input.required<ChartRow[]>();
  protected readonly id = `jo-area-${++Area.next}`;

  protected readonly shape = computed(() => {
    const d = this.data();
    const n = Math.max(1, d.length - 1);
    const max = Math.max(1, ...d.map((x) => x.value));
    const pts = d.map((x, i) => ({ x: (i / n) * 300, y: 100 - (x.value / max) * 88, hint: x.hint ?? `${x.label}: ${x.value}` }));
    if (pts.length === 0) return { line: '', area: '', points: [] };
    // Catmull-Rom para Bézier: curva suave que passa por todos os pontos.
    let line = `M ${pts[0].x} ${pts[0].y}`;
    for (let i = 0; i < pts.length - 1; i++) {
      const p0 = pts[i - 1] ?? pts[i], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2] ?? p2;
      const c1 = { x: p1.x + (p2.x - p0.x) / 6, y: Math.min(100, p1.y + (p2.y - p0.y) / 6) };
      const c2 = { x: p2.x - (p3.x - p1.x) / 6, y: Math.min(100, p2.y - (p3.y - p1.y) / 6) };
      line += ` C ${c1.x} ${c1.y}, ${c2.x} ${c2.y}, ${p2.x} ${p2.y}`;
    }
    return { line, area: `${line} L 300 108 L 0 108 Z`, points: pts };
  });
}
