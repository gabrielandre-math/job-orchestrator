import { ChangeDetectionStrategy, Component, input } from '@angular/core';

let next = 0;

/**
 * A marca: a rota. Um caminho único que desce, faz a curva e sobe de novo, como cada vaga passa pelas etapas
 * até a candidatura. Forma 57 do shapes.gallery (Monika Michalczyk), usada como veio, na cor do texto ao redor.
 * animated: a rota se desenha ao aparecer. active: uma luz percorre a rota enquanto algum agente trabalha.
 * Com movimento reduzido no sistema, a marca fica parada.
 */
@Component({
  selector: 'jo-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<svg viewBox="0 0 256 256" [attr.width]="size()" [attr.height]="size()" aria-hidden="true" focusable="false">
    @if (animated()) {
      <!-- A forma é a linha central com traço de 48: desenhar o traço é desenhar a marca. -->
      <path class="draw" [attr.d]="route" pathLength="100" fill="none" stroke="currentColor" stroke-width="48" />
    } @else {
      <path [attr.d]="shape" fill="currentColor" />
    }
    @if (active()) {
      <defs><clipPath [attr.id]="clip"><path [attr.d]="shape" /></clipPath></defs>
      <g [attr.clip-path]="'url(#' + clip + ')'">
        <path class="glow" [attr.d]="route" pathLength="100" fill="none" stroke-width="64" />
        <path class="spark" [attr.d]="route" pathLength="100" fill="none" stroke-width="48" />
      </g>
    }
  </svg>`,
  styles: `
    :host { display: inline-flex; flex: none; }
    svg { display: block; overflow: visible; }
    .draw { stroke-dasharray: 100; stroke-dashoffset: 100; animation: draw 1.15s cubic-bezier(0.65, 0, 0.35, 1) 0.1s forwards; }
    .glow, .spark { stroke-dasharray: 16 84; stroke-linecap: round; animation: run 2.2s linear infinite; }
    .glow { stroke: oklch(100% 0 0 / 0.18); filter: blur(6px); }
    .spark { stroke: oklch(100% 0 0 / 0.5); stroke-dasharray: 9 91; animation-duration: 2.2s; }
    @keyframes draw { to { stroke-dashoffset: 0; } }
    @keyframes run { from { stroke-dashoffset: 0; } to { stroke-dashoffset: -100; } }
    @media (prefers-reduced-motion: reduce) {
      .draw { animation: none; stroke-dashoffset: 0; }
      .glow, .spark { animation: none; opacity: 0; }
    }
  `,
})
export class Mark {
  readonly size = input(24);
  readonly animated = input(false);
  readonly active = input(false);
  protected readonly shape = 'M 180 0 C 221.974 0 256 34.026 256 76 L 256 256 L 208 256 L 208 76 C 208 60.536 195.464 48 180 48 C 164.536 48 152 60.536 152 76 L 152 180 C 152 221.974 117.974 256 76 256 C 34.026 256 0 221.974 0 180 L 0 0 L 48 0 L 48 180 C 48 195.464 60.536 208 76 208 C 91.464 208 104 195.464 104 180 L 104 76 C 104 34.026 138.026 0 180 0 Z';
  /** Linha central da forma (traço de 48): desce, curva por baixo, sobe, curva por cima e desce. */
  protected readonly route = 'M 24 0 L 24 180 A 52 52 0 0 0 128 180 L 128 76 A 52 52 0 0 1 232 76 L 232 256';
  private readonly id = ++next;
  protected readonly clip = `jo-mark-clip-${this.id}`;
}
