import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { ExploreStatus, get, send } from '../../core/api';
import { command } from '../../core/command';
import { AGENT_ICON, AgentMessage, AgentThread, Live } from '../../core/live';
import { query } from '../../core/query';
import { describeSource } from '../../core/sources';

const KINDS: Record<string, string> = { board: 'quadros e APIs', github: 'repositórios', gupy: 'Gupy', web: 'sites' };
const SPEAKERS = new Set(['Garimpeiro', 'Juiz', 'Coletor', 'Curador', 'Navegador', 'Semeador', 'Robots', 'Extrator', 'Conferente']);

/**
 * O Explorador na tela: se está ligado, o que está fazendo agora (as falas do ciclo ao vivo), a fila de pistas e as fontes que ele
 * testou, com o motivo de cada uma ter entrado ou ficado de fora. Compacto na Busca ao vivo, completo em Fontes.
 */
@Component({
  selector: 'jo-explorer',
  imports: [Company, Icon, Iso],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel ex" [class.compact]="compact()" [class.busy]="busy()">
      <header>
        <span class="title">
          <span class="mark"><jo-iso name="radar" [size]="30" /></span>
          <span class="what"><h2>Explorador</h2><span class="faint small">{{ line() }}</span></span>
        </span>
        <button type="button" class="btn" (click)="run()" [disabled]="busy() || !s()?.enabled">
          @if (busy()) { <span class="spin" aria-hidden="true"></span>Procurando } @else { <jo-icon name="magnifying-glass" [size]="14" />Procurar agora }
        </button>
      </header>
      @if (!compact()) {
        <p class="muted lead">Vai atrás de fonte nova sozinho, sem você passar link. Lê os links das vagas que chegam, busca na Gupy e nas APIs públicas pelas suas palavras, acha repositórios de vaga ativos no GitHub, percorre listas de empresas e manda o coletor aos sites delas. Entra só a fonte com vaga recente da sua área, confirmada pelo modelo local, e as vagas dela chegam na mesma hora.</p>
      }
      <div class="nums">
        <span><strong class="num">{{ s()?.frontier ?? 0 }}</strong> pistas na fila</span>
        <span><strong class="num">{{ s()?.explored ?? 0 }}</strong> fontes testadas</span>
        <span><strong class="num ok">{{ s()?.fit ?? 0 }}</strong> com vaga da sua área</span>
        @if (!compact()) {
          <span class="queue">
            @for (q of queue(); track q.kind) { <span class="chip">{{ q.count }} {{ q.label }}</span> }
          </span>
        }
      </div>
      @if (feed().length) {
        <ol class="feed" aria-live="polite">
          @for (m of feed(); track m.id) {
            <li [class.fail]="m.title.includes('não respondeu')" [class.out]="!m.ok && !m.title.includes('não respondeu')"><jo-iso [name]="icon(m.agent)" [size]="18" /><strong>{{ m.agent }}</strong><span>{{ m.title }}</span></li>
          }
        </ol>
      }
      @if (!compact() && recent().length) {
        <div class="found-head">
          <h3>Fontes testadas</h3>
          <span class="seg" role="group" aria-label="Filtrar">
            <button type="button" [attr.aria-pressed]="!onlyFit()" (click)="onlyFit.set(false)">Todas</button>
            <button type="button" [attr.aria-pressed]="onlyFit()" (click)="onlyFit.set(true)">Só as que entraram</button>
          </span>
        </div>
        <ul class="found">
          @for (v of recent(); track v.source) {
            <li [class.fit]="v.fit">
              <jo-company [name]="v.info.company ?? v.info.platformName" [domain]="v.info.companyDomain ?? v.info.platformDomain" [size]="26" />
              <span class="who"><strong>{{ v.info.label }}</strong><span class="faint small">{{ v.origin }}{{ v.total ? ' · ' + v.relevant + ' de ' + v.total + ' da sua área' : '' }}</span></span>
              <span class="why small muted">{{ v.reason }}</span>
              <span class="tag" [class.ok]="v.fit">{{ v.fit ? 'Entrou' : 'Ficou de fora' }}</span>
            </li>
          }
        </ul>
      }
    </section>
  `,
  styles: `
    :host { display: block; }
    .ex > header { gap: 14px; }
    .title { display: flex; align-items: center; gap: 12px; min-width: 0; }
    .mark { display: grid; place-items: center; width: 38px; height: 38px; }
    .busy .mark jo-iso { animation: sweep 2.4s ease-in-out infinite; }
    .what { display: grid; min-width: 0; }
    .small { font-size: 12px; }
    .lead { margin: 0; padding: 12px 16px 0; font-size: 12.5px; line-height: 1.55; max-width: 92ch; }
    .nums { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 22px; padding: 12px 16px; font-size: 12.5px; color: var(--text-2); }
    .nums strong { font-size: 16px; color: var(--text); margin-right: 4px; }
    .nums strong.ok { color: var(--ok); }
    .queue { display: flex; flex-wrap: wrap; gap: 6px; margin-left: auto; }
    .chip { padding: 2px 8px; border-radius: 999px; background: var(--surface-2); border: 1px solid var(--line); font-size: 11.5px; color: var(--text-2); }
    .feed { list-style: none; margin: 0; padding: 4px 16px 12px; display: grid; gap: 6px; }
    .feed li { display: grid; grid-template-columns: auto auto minmax(0, 1fr); gap: 8px; align-items: center; font-size: 12.5px; animation: line-in 0.35s cubic-bezier(0.2, 0.8, 0.2, 1) both; }
    .feed li span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--text-2); }
    .feed li.fail span { color: var(--warn); }
    .feed li.out span { color: var(--text-3); }
    .found-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 10px 16px; border-top: 1px solid var(--line); }
    .found-head h3 { margin: 0; font-size: 12.5px; }
    .seg { display: inline-flex; border: 1px solid var(--line-strong); border-radius: var(--radius-sm); overflow: hidden; }
    .seg button { border: 0; background: transparent; color: var(--text-2); font: 500 12px var(--font-ui); padding: 5px 10px; cursor: pointer; }
    .seg button[aria-pressed="true"] { background: var(--surface-3); color: var(--text); }
    .found { list-style: none; margin: 0; padding: 0 0 6px; display: grid; max-height: 420px; overflow: auto; }
    .found li { display: grid; grid-template-columns: auto minmax(0, 220px) minmax(0, 1fr) auto; gap: 12px; align-items: center; padding: 8px 16px; transition: background-color 180ms ease; }
    .found li:hover { background: var(--surface-2); }
    .who { display: grid; min-width: 0; }
    .who strong, .who span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .why { overflow: hidden; text-overflow: ellipsis; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; }
    .tag { font-size: 11.5px; padding: 2px 8px; border-radius: 999px; background: var(--surface-2); color: var(--text-3); white-space: nowrap; }
    .tag.ok { background: var(--ok-weak); color: var(--ok); }
    .spin { width: 12px; height: 12px; border-radius: 50%; border: 2px solid var(--line-strong); border-top-color: var(--accent); animation: turn 0.8s linear infinite; }
    .compact .nums { padding-top: 10px; padding-bottom: 6px; }
    @keyframes turn { to { transform: rotate(360deg); } }
    @keyframes sweep { 50% { transform: translateY(-2px) rotate(-4deg); } }
    @keyframes line-in { from { opacity: 0; transform: translateY(3px); } }
    @media (max-width: 760px) { .found li { grid-template-columns: auto minmax(0, 1fr) auto; } .why { display: none; } .queue { margin-left: 0; } }
    @media (prefers-reduced-motion: reduce) { .busy .mark jo-iso, .feed li { animation: none; } }
  `,
})
export class Explorer {
  readonly compact = input(false);
  private readonly live = inject(Live);
  protected readonly status = query<ExploreStatus>(() => '/explore/status', 15000);
  protected readonly s = computed(() => this.status.value() ?? null);
  protected readonly onlyFit = signal(false);
  private readonly seed = signal<AgentMessage[]>([]);
  protected readonly cycle = command(() => send<unknown>('POST', '/explore/run'));
  protected readonly busy = computed(() => !!this.s()?.running || this.cycle.running() || this.livelyAt() > Date.now() - 20_000);

  /** Última fala do Explorador no SSE: enquanto chegam falas, o ciclo está rodando mesmo que o status ainda não tenha atualizado. */
  private readonly livelyAt = computed(() => {
    const last = this.live.feed().filter((l) => this.mine(l.kind, l.subject)).at(-1)?.message;
    return !last || (last.data ?? '').includes('"stage":"done"') ? 0 : Date.parse(last.at);
  });

  protected readonly feed = computed(() => {
    const fromLive = this.live.feed().filter((l) => this.mine(l.kind, l.subject)).map((l) => l.message);
    const all = [...this.seed(), ...fromLive].filter((m) => (m.kind === 'note' || m.kind === 'fail') && SPEAKERS.has(m.agent));
    const unique = [...new Map(all.map((m) => [m.id, m])).values()];
    return unique.filter((m) => Date.parse(m.at) > Date.now() - 30 * 60_000).slice(this.compact() ? -4 : -7);
  });

  protected readonly queue = computed(() => Object.entries(this.s()?.queue ?? {}).map(([kind, count]) => ({ kind, count, label: KINDS[kind] ?? kind })).sort((a, b) => b.count - a.count));

  protected readonly recent = computed(() => (this.s()?.recent ?? [])
    .filter((v) => !this.onlyFit() || v.fit)
    .map((v) => ({ ...v, info: describeSource(v.source) })));

  protected readonly line = computed(() => {
    const s = this.s();
    if (!s) return 'carregando…';
    if (!s.enabled) return 'Desligado. Ligue em Configurações › Busca contínua.';
    if (this.busy()) return 'Procurando fontes agora';
    const next = s.next ? new Date(s.next) : null;
    const when = next && next.getTime() > Date.now() ? `próximo ciclo às ${next.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}` : 'próximo ciclo em instantes';
    return `Ligado, a cada ${s.intervalMinutes} min. ${when[0].toUpperCase()}${when.slice(1)}.`;
  });

  constructor() { void this.loadSeed(); }

  protected icon(agent: string) { return AGENT_ICON[agent] ?? 'radar'; }

  protected async run() {
    const r = this.cycle.run(undefined);
    setTimeout(() => void this.status.reload(), 1500);
    await r;
    await this.status.reload();
  }

  private mine(kind: string, subject: string) { return kind === 'explore' || (kind === 'crawl' && subject.startsWith('Explorador')); }

  /** Quem abre a tela no meio de um ciclo já vê as últimas falas dele, sem esperar a próxima. */
  private async loadSeed() {
    try {
      const threads = await get<AgentThread[]>('/agents/threads?limit=30');
      const t = threads.find((x) => this.mine(x.kind, x.subject));
      if (!t || Date.parse(t.lastAt) < Date.now() - 30 * 60_000) return;
      this.seed.set(await get<AgentMessage[]>(`/agents/threads/${t.id}`));
    } catch { /* sem histórico, fica só o ao vivo */ }
  }
}
