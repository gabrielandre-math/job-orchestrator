import { ChangeDetectionStrategy, Component, computed, effect, inject, signal, untracked } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { send } from '../../core/api';
import { command } from '../../core/command';
import { ago } from '../../core/format';
import { AGENT_ICON, Live } from '../../core/live';
import { query } from '../../core/query';

interface HuntChannel { name: string; queries: number; found: number; leads: number; note: string | null }
interface HuntPlan { roles: string[]; slugs: string[]; countries: string[]; industries: string[]; companies: string[]; queries: string[] }
interface HuntSummary { at: string; depth: string; plan: HuntPlan | null; plannedBy: string; channels: HuntChannel[]; leads: number; sample: string[]; seconds: number; skipped: string | null }
interface HuntStatus { running: boolean; enabled: boolean; intervalHours: number; depth: string; last: HuntSummary | null; queued: number }
type Depth = 'rapido' | 'profundo';

/**
 * O caçador com o Llama na tela: ligar para rodar sozinho, a profundidade, caçar agora, as falas ao vivo e o que cada canal achou na
 * última caçada (Common Crawl, Wikidata, busca na web), com o plano que o modelo local montou.
 */
@Component({
  selector: 'jo-hunter',
  imports: [Icon, Iso],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel hunt" [class.busy]="running()">
      <header>
        <span class="mark"><jo-iso name="radar" [size]="44" /></span>
        <div class="t">
          <h2>Caçador com o Llama</h2>
          <p class="muted small">O modelo local monta o plano (cargos em três línguas, palavras do endereço de páginas de vaga, países e setores) e o caçador
            procura no mundo todo: no índice público do Common Crawl, no Wikidata e, com a chave do Brave, na web. O que ele achar vai para a fila do
            Explorador, que testa cada fonte antes de pôr na campanha. Nada da campanha sai da máquina.</p>
        </div>
        <label class="auto"><input type="checkbox" [checked]="s()?.enabled ?? false" (change)="toggle($any($event.target).checked)" />Caçar sozinho a cada {{ s()?.intervalHours ?? 12 }} h</label>
      </header>
      <div class="controls">
        <div class="seg" role="radiogroup" aria-label="Profundidade da caçada">
          <button type="button" role="radio" [attr.aria-checked]="depth() === 'rapido'" [class.on]="depth() === 'rapido'" (click)="setDepth('rapido')">
            <strong>Rápida</strong><span>4 ATS e um lote de países</span></button>
          <button type="button" role="radio" [attr.aria-checked]="depth() === 'profundo'" [class.on]="depth() === 'profundo'" (click)="setDepth('profundo')">
            <strong>Profunda</strong><span>12 ATS, três lotes de países e as empresas do plano conferidas</span></button>
        </div>
        <button type="button" class="btn primary" (click)="go()" [disabled]="running()">
          @if (running()) { <span class="spin" aria-hidden="true"></span>Caçando… } @else { <jo-icon name="magnifying-glass" [size]="14" />Caçar agora }
        </button>
      </div>
      @if (feed().length > 0) {
        <ol class="feed" aria-live="polite">
          @for (l of feed(); track l.message.id) {
            <li><jo-iso [name]="icons[l.message.agent] ?? 'radar'" [size]="26" /><span><strong>{{ l.message.agent }}</strong> {{ l.message.title }}</span><span class="faint small">{{ ago(l.message.at) }}</span></li>
          }
        </ol>
      }
      @if (s()?.last; as l) {
        @if (l.skipped) {
          <p class="notice">{{ l.skipped }}</p>
        } @else {
          <div class="channels">
            @for (c of l.channels; track c.name) {
              <div class="ch" [class.none]="c.leads === 0">
                <span class="n num">{{ c.leads }}</span>
                <span class="what"><strong>{{ c.name }}</strong><span class="faint small">pistas de {{ c.found }} achados em {{ c.queries }} consultas</span>
                  @if (c.note) { <span class="faint small">{{ c.note }}</span> }</span>
              </div>
            }
          </div>
          @if (l.plan; as p) {
            <div class="plan">
              <p><span class="k">Cargos</span>@for (x of p.roles; track x) { <span class="chip">{{ x }}</span> }</p>
              <p><span class="k">No endereço</span>@for (x of p.slugs; track x) { <span class="chip mono">{{ x }}</span> }</p>
              <p><span class="k">Países</span>@for (x of p.countries; track x) { <span class="chip mono">{{ x }}</span> }</p>
            </div>
          }
          <p class="faint small foot">Última caçada {{ ago(l.at) }} ({{ l.depth === 'profundo' ? 'profunda' : 'rápida' }}): {{ l.leads }} pistas em {{ l.seconds }} s, plano por {{ l.plannedBy }}.
            @if (s()?.queued) { {{ s()!.queued }} esperam o próximo ciclo do Explorador. }</p>
        }
      }
    </section>`,
  styles: `
    :host { display: block; }
    .hunt header { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 14px; align-items: start; padding: 14px 16px; }
    .hunt h2 { font-size: 15px; margin: 0 0 4px; }
    .hunt .t p { margin: 0; line-height: 1.55; max-width: 92ch; }
    .busy .mark jo-iso { animation: sweep 1.6s ease-in-out infinite; }
    .auto { display: inline-flex; gap: 8px; align-items: center; font-size: 12.5px; color: var(--text-2); white-space: nowrap; cursor: pointer; }
    .controls { display: flex; flex-wrap: wrap; gap: 10px; align-items: stretch; padding: 0 16px 14px; }
    .seg { display: flex; gap: 6px; flex: 1; min-width: min(420px, 100%); }
    .seg button { flex: 1; display: grid; gap: 2px; align-content: center; height: auto; min-height: 54px; line-height: 1.35; text-align: left; padding: 8px 12px; border-radius: 6px; border: 1px solid var(--line); background: var(--surface-2); color: var(--text-2);
      font: inherit; cursor: pointer; transition: border-color 160ms ease, background-color 160ms ease, transform 120ms ease; }
    .seg button:hover { border-color: var(--line-strong); color: var(--text); }
    .seg button:active { transform: scale(0.99); }
    .seg button.on { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-weak); color: var(--text); }
    .seg span { font-size: 11.5px; color: var(--text-3); }
    .feed { list-style: none; margin: 0; padding: 0 16px 12px; display: grid; gap: 4px; }
    .feed li { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 8px; align-items: center; font-size: 12.5px; animation: line-in 0.3s cubic-bezier(0.2, 0.8, 0.2, 1) both; }
    .channels { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(220px, 100%), 1fr)); gap: 8px; padding: 0 16px 12px; }
    .ch { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 10px; align-items: center; padding: 10px 12px; border: 1px solid var(--line); border-radius: 6px; background: var(--surface-2); }
    .ch.none { opacity: 0.75; }
    .ch .n { font-size: 20px; font-weight: 600; min-width: 2ch; text-align: right; }
    .what { display: grid; gap: 2px; min-width: 0; }
    .plan { display: grid; gap: 6px; padding: 0 16px 10px; }
    .plan p { margin: 0; display: flex; flex-wrap: wrap; gap: 5px; align-items: center; }
    .k { font-size: 11.5px; color: var(--text-3); min-width: 9ch; }
    .chip { padding: 2px 7px; border-radius: 3px; background: var(--surface-3); font-size: 11.5px; }
    .foot { margin: 0; padding: 0 16px 14px; }
    .notice { margin: 0 16px 14px; }
    .spin { width: 12px; height: 12px; border-radius: 50%; border: 2px solid var(--accent-weak); border-top-color: var(--accent-text); animation: turn 0.8s linear infinite; }
    @keyframes turn { to { transform: rotate(360deg); } }
    @keyframes sweep { 50% { transform: translateY(-2px) rotate(-4deg); } }
    @keyframes line-in { from { opacity: 0; transform: translateY(3px); } }
    @media (max-width: 760px) { .hunt header { grid-template-columns: auto minmax(0, 1fr); } .auto { grid-column: 1 / -1; } }
    @media (prefers-reduced-motion: reduce) { .busy .mark jo-iso, .feed li { animation: none; } }
  `,
})
export class Hunter {
  private readonly live = inject(Live);
  protected readonly ago = ago;
  protected readonly icons = AGENT_ICON;
  protected readonly status = query<HuntStatus>(() => '/explore/hunt', 4000);
  protected readonly s = computed(() => this.status.value() ?? null);
  protected readonly depth = signal<Depth>('rapido');
  private readonly starting = signal(false);
  protected readonly running = computed(() => !!this.s()?.running || this.starting());
  protected readonly feed = computed(() => this.live.feed().filter((l) => l.kind === 'hunt').slice(-6).reverse());
  private readonly saving = command((body: { enabled: boolean; intervalHours: number; depth: Depth }) => send<void>('PUT', '/explore/hunt/settings', body));
  private readonly starter = command((depth: Depth) => send<void>('POST', '/explore/hunt', { depth }), 'A caçada começou: as falas aparecem aqui e as pistas entram na fila do Explorador.');

  constructor() {
    // A profundidade salva vem do servidor uma vez; depois vale a escolha na tela.
    let loaded = false;
    effect(() => {
      const d = this.s()?.depth;
      if (!loaded && d) { loaded = true; untracked(() => this.depth.set(d === 'profundo' ? 'profundo' : 'rapido')); }
    });
  }

  protected async go() {
    this.starting.set(true);
    await this.starter.run(this.depth());
    await this.status.reload();
    this.starting.set(false);
  }

  protected async setDepth(d: Depth) {
    this.depth.set(d);
    const s = this.s();
    if (s) await this.saving.run({ enabled: s.enabled, intervalHours: s.intervalHours, depth: d });
  }

  protected async toggle(on: boolean) {
    await this.saving.run({ enabled: on, intervalHours: this.s()?.intervalHours ?? 12, depth: this.depth() });
    await this.status.reload();
  }
}
