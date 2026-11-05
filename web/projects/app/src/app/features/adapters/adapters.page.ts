import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { Badge } from '@jo/ui/badge';
import { Iso } from '@jo/ui/iso';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { Adapter } from '../../core/api';
import { ago } from '../../core/format';
import { query } from '../../core/query';
import { SOURCE_TABS, Tabs } from '../../shared/tabs';

const LOGO: Record<string, { file: string; mono: boolean }> = {
  greenhouse: { file: 'greenhouse', mono: false }, github: { file: 'github', mono: true }, hackernews: { file: 'hackernews', mono: false },
};

/** Adapters: cada conector com o que faz, onde atua e o placar real que alimenta o roteador. */
@Component({
  selector: 'app-adapters',
  imports: [Badge, Iso, PageHero, Tabs],
  template: `
    <section class="page">
      <jo-tabs [items]="tabs" label="Fontes" />
      <jo-page-hero title="Conectores" lead="Os conectores de fontes e de formulários. O roteador escolhe por capacidade testada, domínio, risco e placar; circuito aberto tira o adapter da rota por 10 minutos."
        [scene]="['router', 'radar', 'form']" [stats]="stats()" />
      @if (list.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
      <div class="cards">
        @for (a of list.value() ?? []; track a.id) {
          <article class="panel card" [class.open]="a.circuitOpen">
            <header>
              @if (logo(a); as l) {
                <span class="mark">@if (l.mono) { <span class="logo mono" [style.--logo]="'url(/logos/' + l.file + '.svg)'"></span> } @else { <img class="logo" [src]="'/logos/' + l.file + '.svg'" alt="" /> }</span>
              } @else { <jo-iso [name]="a.kind.includes('Formul') ? 'form' : a.id.startsWith('web') ? 'radar' : 'router'" [size]="44" /> }
              <div class="title"><h2>{{ a.name }}</h2><p class="mono faint">{{ a.id }} · {{ a.version }}</p></div>
              @if (a.circuitOpen) { <jo-badge label="Circuito aberto" tone="danger" /> } @else { <jo-badge label="Na rota" tone="ok" /> }
            </header>
            <p class="kind faint">{{ a.kind }}</p>
            <div class="caps">@for (c of a.capabilities; track c) { <span class="tag">{{ c }}</span> }</div>
            @if (a.domains.length > 0) { <p class="domains mono faint">{{ a.domains.join(', ') }}</p> }
            <div class="score">
              <span class="track"><span class="fill" [style.width.%]="a.attempts > 0 ? (a.confirmed / a.attempts) * 100 : 0"></span></span>
              <span class="num small">{{ a.confirmed }} de {{ a.attempts }} confirmados · {{ a.failures }} falhas</span>
            </div>
            @if (a.circuitOpen) { <p class="warn small">Volta para a rota {{ ago(a.circuitOpenUntil) }}.</p> }
          </article>
        }
      </div>
    </section>`,
  styles: `
    .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(320px, 100%), 1fr)); gap: 12px; }
    .card { display: grid; gap: 10px; padding: 14px 16px; transition: border-color 160ms ease, transform 140ms ease; }
    .card:hover { border-color: color-mix(in oklch, var(--accent) 35%, var(--line-strong)); transform: translateY(-1px); }
    .card.open { border-color: color-mix(in oklch, var(--danger) 45%, var(--line)); }
    header { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 12px; align-items: center; }
    .mark { display: grid; place-items: center; width: 44px; height: 44px; }
    .logo { width: 28px; height: 28px; display: block; }
    .logo.mono { background: var(--text); mask: var(--logo) center / contain no-repeat; -webkit-mask: var(--logo) center / contain no-repeat; }
    h2 { font-size: 14px; }
    .title p { margin: 2px 0 0; font-size: 11.5px; }
    .kind { margin: 0; font-size: 12px; }
    .caps { display: flex; flex-wrap: wrap; gap: 4px; }
    .tag { padding: 1px 7px; border-radius: 3px; background: var(--surface-3); color: var(--text-2); font-size: 11.5px; }
    .domains { margin: 0; font-size: 11.5px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .score { display: grid; gap: 4px; }
    .track { height: 6px; border-radius: 3px; background: var(--surface-3); overflow: hidden; }
    .fill { display: block; height: 100%; background: var(--ok); transition: width 600ms ease; }
    .small { font-size: 12px; }
    .warn { color: var(--warn); margin: 0; }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdaptersPage {
  protected readonly list = query<Adapter[]>(() => '/adapters', 6000);
  protected readonly ago = ago;
  protected readonly tabs = SOURCE_TABS;
  protected readonly stats = computed<HeroStat[]>(() => {
    const all = this.list.value() ?? [];
    return [
      { label: 'Conectores', value: all.length },
      { label: 'Fontes de vagas', value: all.filter((a) => a.capabilities.some((c) => c.includes('Discover'))).length },
      { label: 'Formulários', value: all.filter((a) => a.capabilities.some((c) => c.includes('Prepare') || c.includes('Submit'))).length },
      { label: 'Circuitos abertos', value: all.filter((a) => a.circuitOpen).length, tone: all.some((a) => a.circuitOpen) ? 'danger' : undefined },
    ];
  });
  protected logo(a: Adapter) {
    const key = a.id.split('-')[0].replace('hn', 'hackernews');
    return LOGO[key] ?? null;
  }
}
