import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Iso } from './iso';

/** Uma estação na rota: em que fração do caminho fica, o ícone e o nome. */
export interface RouteStop { at: number; icon: string; label: string; active?: boolean }

const SEGMENTS = [
  { kind: 'line', from: [24, 0], to: [24, 180] },
  { kind: 'arc', center: [76, 180], r: 52, from: Math.PI, to: 0, dir: -1 },
  { kind: 'line', from: [128, 180], to: [128, 76] },
  { kind: 'arc', center: [180, 76], r: 52, from: Math.PI, to: 2 * Math.PI, dir: 1 },
  { kind: 'line', from: [232, 76], to: [232, 256] },
] as const;
const LENGTHS = [180, Math.PI * 52, 104, Math.PI * 52, 180];
const TOTAL = LENGTHS.reduce((a, b) => a + b, 0);

/** Ponto da linha central da marca numa fração do comprimento (0 no começo, 1 no fim). */
function pointAt(f: number): [number, number] {
  let d = Math.min(Math.max(f, 0), 1) * TOTAL;
  for (let i = 0; i < SEGMENTS.length; i++) {
    const s = SEGMENTS[i];
    if (d > LENGTHS[i] && i < SEGMENTS.length - 1) { d -= LENGTHS[i]; continue; }
    const t = d / LENGTHS[i];
    if (s.kind === 'line') return [s.from[0] + (s.to[0] - s.from[0]) * t, s.from[1] + (s.to[1] - s.from[1]) * t];
    // Arco por baixo (primeiro) e por cima (segundo).
    const a = s.dir === -1 ? Math.PI - t * Math.PI : Math.PI + t * Math.PI;
    return [s.center[0] + Math.cos(a) * s.r, s.center[1] + Math.sin(a) * s.r];
  }
  return [232, 256];
}

let next = 0;

/**
 * A marca como cenário: a rota grande, em tom baixo, com as estações dos agentes ao longo dela e uma luz que percorre
 * o caminho quando há trabalho. É a ideia do produto desenhada: cada vaga passa pelas etapas, sem atalho.
 * A luz não usa filtro (blur e drop-shadow refazem a pintura a cada quadro e travam a 144 Hz): o rastro são dois traços e a
 * cabeça é um brilho em gradiente que anda pelo caminho; os dois pelo relógio do próprio SVG, no mesmo compasso.
 */
@Component({
  selector: 'jo-route-hero',
  imports: [Iso],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg viewBox="-30 -30 316 316" aria-hidden="true">
      <defs>
        <radialGradient [attr.id]="glow">
          <stop offset="0" style="stop-color: var(--accent); stop-opacity: 0.95" />
          <stop offset="0.35" style="stop-color: var(--accent); stop-opacity: 0.42" />
          <stop offset="1" style="stop-color: var(--accent); stop-opacity: 0" />
        </radialGradient>
      </defs>
      <path class="bed" [attr.d]="route" />
      <path class="lane" [attr.d]="route" pathLength="100" />
      @if (busy()) {
        <g class="light">
          <path class="trail wide" [attr.d]="route" pathLength="100" stroke-dasharray="9 91" stroke-dashoffset="9">
            <animate attributeName="stroke-dashoffset" values="9;-91" dur="2.6s" repeatCount="indefinite" />
          </path>
          <path class="trail" [attr.d]="route" pathLength="100" stroke-dasharray="7 93" stroke-dashoffset="7">
            <animate attributeName="stroke-dashoffset" values="7;-93" dur="2.6s" repeatCount="indefinite" />
          </path>
          <g>
            <circle r="15" [attr.fill]="'url(#' + glow + ')'" />
            <circle r="3.4" class="core" />
            <animateMotion dur="2.6s" repeatCount="indefinite" calcMode="linear" [attr.path]="route" />
          </g>
        </g>
      }
    </svg>
    @for (s of placed(); track s.label) {
      <span class="stop" [class.on]="s.active" [style.left.%]="s.x" [style.top.%]="s.y">
        <jo-iso [name]="s.icon" [size]="52" />
        <span class="name">{{ s.label }}</span>
      </span>
    }`,
  styles: `
    :host { position: relative; display: block; aspect-ratio: 1; }
    svg { position: absolute; inset: 0; width: 100%; height: 100%; overflow: visible; }
    .bed { fill: none; stroke: color-mix(in oklch, var(--accent) 12%, var(--surface-2)); stroke-width: 48; }
    .lane { fill: none; stroke: color-mix(in oklch, var(--accent) 55%, transparent); stroke-width: 2; stroke-dasharray: 1.2 2.2; animation: flow 6s linear infinite; }
    .trail { fill: none; stroke: var(--accent); stroke-width: 5; stroke-linecap: round; }
    .trail.wide { stroke-width: 14; opacity: 0.22; }
    .core { fill: color-mix(in oklch, var(--accent) 35%, white); }
    .stop { position: absolute; transform: translate(-50%, -62%); display: grid; justify-items: center; gap: 2px; transition: transform 240ms ease; }
    .stop.on { transform: translate(-50%, -70%); }
    .stop.on jo-iso { animation: bob 1.4s ease-in-out infinite; will-change: transform; filter: drop-shadow(0 6px 10px color-mix(in oklch, var(--accent) 45%, transparent)); }
    .name { padding: 1px 6px; border-radius: 3px; background: var(--label-bg); color: var(--text-2); font: 500 10.5px var(--font-mono); white-space: nowrap; }
    .stop.on .name { color: var(--accent); }
    @keyframes flow { to { stroke-dashoffset: -100; } }
    @keyframes bob { 50% { transform: translate3d(0, -4px, 0); } }
    @media (prefers-reduced-motion: reduce) { .lane, .stop.on jo-iso { animation: none; } .light { display: none; } }
  `,
})
export class RouteHero {
  readonly stops = input<RouteStop[]>([]);
  readonly busy = input(false);
  protected readonly glow = `jo-route-glow-${++next}`;
  protected readonly route = 'M 24 0 L 24 180 A 52 52 0 0 0 128 180 L 128 76 A 52 52 0 0 1 232 76 L 232 256';
  protected readonly placed = computed(() => this.stops().map((s) => {
    const [x, y] = pointAt(s.at);
    // viewBox de -30 a 286: converte para porcentagem do quadro.
    return { ...s, x: ((x + 30) / 316) * 100, y: ((y + 30) / 316) * 100 };
  }));
}
