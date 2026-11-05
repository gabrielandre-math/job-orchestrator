import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { Count } from './count';
import { Iso } from './iso';

export interface HeroStat { label: string; value: number; tone?: 'ok' | 'warn' | 'danger' | 'accent'; note?: string }

/**
 * Cabeçalho com identidade: título, frase, a cena isométrica do módulo (de duas a três estações que contam o que a tela faz)
 * e os números principais. As ações entram por projeção.
 */
@Component({
  selector: 'jo-page-hero',
  imports: [Count, Iso],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="text">
      <h1>{{ title() }}</h1>
      <p class="lead">{{ lead() }}</p>
      <div class="actions"><ng-content /></div>
    </div>
    <div class="scene" aria-hidden="true">
      @for (s of scene(); track s; let i = $index) { <jo-iso [name]="s" [size]="i === 1 ? 92 : 70" [class]="'s' + i" /> }
    </div>
    @if (stats().length > 0) {
      <dl class="stats">
        @for (s of stats(); track s.label) {
          <div [class]="'stat ' + (s.tone ?? '')"><dt>{{ s.label }}</dt><dd class="num"><jo-count [value]="s.value" /></dd>@if (s.note) { <span class="note">{{ s.note }}</span> }</div>
        }
      </dl>
    }`,
  styles: `
    :host { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 8px 24px; align-items: center; padding: 22px 24px 18px; border-radius: var(--radius); border: 1px solid var(--line);
      background: radial-gradient(110% 160% at 100% 0%, color-mix(in oklch, var(--accent) 8%, transparent), transparent 55%), var(--surface); }
    .text { display: grid; gap: 6px; min-width: 0; }
    h1 { font-size: 22px; font-weight: 650; letter-spacing: -0.015em; margin: 0; }
    .lead { margin: 0; color: var(--text-2); line-height: 1.5; max-width: 70ch; }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 6px; }
    .actions:empty { display: none; }
    .scene { position: relative; display: flex; align-items: flex-end; gap: 0; padding-right: 4px; }
    .scene .s0, .scene .s2 { margin-bottom: 4px; }
    .scene .s1 { margin: 0 -10px; z-index: 1; filter: drop-shadow(0 10px 16px color-mix(in oklch, var(--accent) 22%, transparent)); }
    .stats { grid-column: 1 / -1; display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 10px; margin: 10px 0 0; padding-top: 14px; border-top: 1px solid var(--line); }
    .stat { display: grid; gap: 2px; }
    dt { font-size: 12px; color: var(--text-3); }
    dd { margin: 0; font-size: 22px; font-weight: 600; line-height: 1.1; }
    .stat.ok dd { color: var(--ok); } .stat.warn dd { color: var(--warn); } .stat.danger dd { color: var(--danger); } .stat.accent dd { color: var(--accent); }
    .note { font-size: 11.5px; color: var(--text-3); }
    @media (max-width: 760px) {
      :host { grid-template-columns: 1fr; padding: 16px; }
      .scene { order: -1; justify-content: flex-start; }
      .scene jo-iso { transform: scale(0.8); transform-origin: bottom left; }
    }
  `,
})
export class PageHero {
  readonly title = input.required<string>();
  readonly lead = input('');
  readonly scene = input<string[]>([]);
  readonly stats = input<HeroStat[]>([]);
}
