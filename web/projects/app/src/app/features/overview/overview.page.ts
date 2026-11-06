import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Automate } from './automate';
import { Router, RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { ChartRow, HBars } from '@jo/ui/charts';
import { Count } from '@jo/ui/count';
import { Empty } from '@jo/ui/empty';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { RouteHero, RouteStop } from '@jo/ui/route-hero';
import { ApplicationRow, ExploreStatus, JobRow, MailOverview, OutgoingMail, Overview, Profile, send, TaskRow } from '../../core/api';
import { command } from '../../core/command';
import { ago, clock, modeLabel, plural, stateLabel, stateTone } from '../../core/format';
import { AGENT_ICON, Live, PROVIDER_LOGO } from '../../core/live';
import { query } from '../../core/query';
import { SearchRun } from '../../core/search';
import { WelcomeHub } from '../../core/welcome-hub';

/**
 * Início: a marca como cenário (a rota com as estações dos agentes), a frase do dia, os números contando, o funil, os agentes
 * ao vivo e o que precisa de você. É a primeira tela, então diz em segundos se está tudo andando.
 */
@Component({
  selector: 'app-overview',
  imports: [RouterLink, Badge, Count, Empty, HBars, Icon, Iso, RouteHero, Automate],
  templateUrl: './overview.page.html',
  styleUrl: './overview.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OverviewPage {
  protected readonly live = inject(Live);
  protected readonly search = inject(SearchRun);
  protected readonly hub = inject(WelcomeHub);
  protected readonly automating = signal(false);
  /** "Configurar tudo" do menu chega aqui como ?configurar=1 e abre o passo a passo guiado. */
  readonly configurar = input<string | undefined>();
  private readonly openSetup = effect(() => {
    if (!this.configurar() || !this.hub.state()) return;
    untracked(() => {
      if (this.setup() && !this.allDone()) this.hub.open.set(true); else this.automating.set(true);
      // Abriu: tira o ?configurar=1, para recarregar a página não abrir de novo.
      void this.router.navigate([], { queryParams: { configurar: null }, queryParamsHandling: 'merge', replaceUrl: true });
    });
  });
  private readonly router = inject(Router);
  /** Fechou o passo a passo: tira o ?configurar=1, para recarregar a página não abrir de novo. */
  protected closeSetup() { this.automating.set(false); }
  /** Configuração pendente: o que falta para a busca andar sozinha. */
  protected readonly setup = computed(() => {
    const o = this.hub.state();
    if (!o || o.done) return null;
    return [
      { label: 'IA conectada', done: o.aiReady, hint: o.aiReady ? o.accounts.slice(0, 3).join(', ') : 'Claude, ChatGPT, chave de API ou modelo local' },
      { label: 'Currículo lido', done: o.facts > 0, hint: o.facts > 0 ? `${o.facts} fatos verificados` : 'PDF, DOCX ou o export do LinkedIn' },
      { label: 'Busca montada', done: o.keywords > 0 && o.sources > 0, hint: o.keywords > 0 ? `${o.keywords} palavras, ${o.sources} fontes` : 'cargos, palavras, locais e fontes' },
    ];
  });
  protected readonly allDone = computed(() => (this.setup() ?? []).every((s) => s.done));
  protected readonly stages = computed(() => {
    const s = this.search.state();
    const n = s?.sources.length ?? 0;
    const done = s?.sources.filter((x) => x.state === 'done' || x.state === 'failed').length ?? 0;
    const at = !s ? -1 : !s.running ? 5 : s.scored !== null ? 4 : s.fresh !== null ? 3 : done === n && n > 0 ? 2 : 0;
    return [
      { iso: 'radar', label: 'Fontes', value: s ? `${done}/${n}` : '—' }, { iso: 'reader', label: 'Da sua área', value: s ? String(s.kept) : '—' },
      { iso: 'checker', label: 'Novas', value: s?.fresh ?? '—' }, { iso: 'chart', label: 'Nota', value: s?.above ?? '—' }, { iso: 'queue', label: 'Na fila', value: s && !s.running ? (s.shortlisted ?? 0) : '—' },
    ].map((x, i) => ({ ...x, state: at < 0 ? 'idle' : i < at ? 'done' : i === at && s?.running ? 'working' : at === 5 ? 'done' : 'idle' }));
  });
  protected readonly data = query<Overview>(() => '/overview', 4000);
  private readonly mail = query<MailOverview>(() => '/mail/overview', 15000);
  private readonly outgoing = query<OutgoingMail[]>(() => '/mail/outgoing', 30000);
  private readonly apps = query<ApplicationRow[]>(() => '/applications', 15000);
  private readonly rehearsal = query<{ running: boolean; last: { items: { kind: string; reason: string | null; detail: string | null }[] } | null }>(() => '/applications/rehearsal', 30000);
  private readonly queue = query<JobRow[]>(() => '/jobs?status=Shortlisted', 60000);

  /** O Início responde três perguntas: o que saiu, o que voltou e o que depende de você, cada linha com o caminho para resolver. */
  protected readonly today = computed(() => {
    const d = this.data.value(), m = this.mail.value(), apps = this.apps.value() ?? [], mails = this.outgoing.value() ?? [];
    const lost = mails.filter((x) => x.status === 'Bounced' || x.status === 'Failed').length;
    const outcome = (o: string) => apps.filter((a) => a.outcome === o).length;
    const questions = new Set((this.rehearsal.value()?.last?.items ?? []).filter((i) => i.reason === 'unsupported-field').flatMap((i) => [...(i.detail ?? '').matchAll(/"([^"]+)"/g)].map((x) => x[1]))).size;
    const bySite = (this.queue.value() ?? []).filter((j) => j.applyChannel === 'manual').length;
    type Line = { n: number; label: string; link: string; query?: Record<string, string>; tone?: string };
    const sent: Line[] = [
      { n: d?.confirmedToday ?? 0, label: word(d?.confirmedToday ?? 0, 'candidatura enviada hoje', 'candidaturas enviadas hoje'), link: '/candidaturas' },
      { n: m?.sentToday ?? 0, label: word(m?.sentToday ?? 0, 'e-mail enviado hoje', 'e-mails enviados hoje'), link: '/email' },
      { n: lost, label: word(lost, 'e-mail não chegou', 'e-mails não chegaram'), link: '/email', tone: 'bad' },
      { n: d?.applications.confirmed ?? 0, label: 'enviadas no total', link: '/candidaturas' },
    ];
    const back: Line[] = [
      { n: m?.replied ?? 0, label: word(m?.replied ?? 0, 'resposta por e-mail', 'respostas por e-mail'), link: '/email' },
      { n: outcome('Entrevista') + (m?.interviews ?? 0), label: word(outcome('Entrevista') + (m?.interviews ?? 0), 'entrevista', 'entrevistas'), link: '/candidaturas', tone: 'ok' },
      { n: outcome('Oferta'), label: word(outcome('Oferta'), 'oferta', 'ofertas'), link: '/candidaturas', tone: 'ok' },
      { n: outcome('Recusada'), label: word(outcome('Recusada'), 'recusa', 'recusas'), link: '/candidaturas' },
    ];
    const you: Line[] = [
      { n: d?.applications.awaitingApproval ?? 0, label: word(d?.applications.awaitingApproval ?? 0, 'formulário para aprovar', 'formulários para aprovar'), link: '/revisao', tone: 'warn' },
      { n: d?.pendingTasks ?? 0, label: word(d?.pendingTasks ?? 0, 'pendência para resolver', 'pendências para resolver'), link: '/atencao', tone: 'warn' },
      { n: m?.awaiting ?? 0, label: word(m?.awaiting ?? 0, 'e-mail para aprovar', 'e-mails para aprovar'), link: '/email', tone: 'warn' },
      { n: questions, label: word(questions, 'pergunta de formulário sem resposta', 'perguntas de formulário sem resposta'), link: '/candidaturas', tone: 'warn' },
      { n: bySite, label: word(bySite, 'vaga para você enviar pelo site', 'vagas para você enviar pelo site'), link: '/vagas', tone: 'warn' },
    ];
    return { sent, back, you, sentN: (d?.confirmedToday ?? 0) + (m?.sentToday ?? 0), backN: back.slice(0, 3).reduce((n, l) => n + l.n, 0), youN: you.reduce((n, l) => n + l.n, 0) };
  });
  protected readonly tasks = query<TaskRow[]>(() => '/tasks', 4000);
  protected readonly profile = query<Profile>(() => '/profile', 0);
  protected readonly linkedinOn = computed(() => (this.profile.value()?.documents ?? []).some((d) => d.kind === 'linkedin' && d.status === 'Ready'));
  protected readonly explore = query<ExploreStatus>(() => '/explore/status', 15000);
  /** Relógio de 30 s para os "em 12 min" do hero andarem sozinhos. */
  private readonly now = signal(Date.now());
  private readonly tick = setInterval(() => this.now.set(Date.now()), 30_000);
  private readonly untick = inject(DestroyRef).onDestroy(() => clearInterval(this.tick));
  protected readonly run = command((id: string) => send('POST', `/campaigns/${id}/runs`), 'Rodada pedida. As fontes começam a ser lidas.');
  protected readonly label = stateLabel;
  protected readonly tone = stateTone;
  protected readonly ago = ago;
  protected readonly clock = clock;
  protected readonly icons = AGENT_ICON;
  protected readonly logos = PROVIDER_LOGO;
  protected readonly mode = computed(() => modeLabel(this.data.value()?.campaign?.mode ?? ''));

  protected readonly greeting = computed(() => {
    const h = new Date().getHours();
    const part = h < 12 ? 'Bom dia' : h < 18 ? 'Boa tarde' : 'Boa noite';
    const name = (this.profile.value()?.identity.fullName ?? '').split(' ')[0];
    return name ? `${part}, ${name}.` : `${part}.`;
  });

  protected readonly waiting = computed(() => { const a = this.data.value()?.applications; return a ? a.awaitingApproval + a.needsHuman : 0; });
  protected readonly state = computed(() => this.live.busy() ? 'Agentes trabalhando agora' : this.waiting() > 0 ? plural(this.waiting(), 'coisa espera por você', 'coisas esperam por você') : 'Tudo em ordem');

  /** A frase do dia: o que andou e o que espera por você, sem enfeite. */
  protected readonly summary = computed(() => {
    const d = this.data.value();
    if (!d) return '';
    const waiting = d.applications.awaitingApproval + d.applications.needsHuman;
    const queue = plural(d.jobs.shortlisted, 'vaga na fila', 'vagas na fila');
    const prepared = plural(d.applications.prepared, 'candidatura preparada', 'candidaturas preparadas');
    // A mesma conta do bloco "O que depende de você": formulários, pendências, e-mails, perguntas e envios pelo site.
    const all = Math.max(waiting, this.today().youN);
    const you = all === 0 ? 'nada esperando por você' : plural(all, 'coisa esperando por você', 'coisas esperando por você');
    return `${queue}, ${prepared} e ${you}.`;
  });

  /**
   * O pulso do hero: a próxima busca, o que o Explorador fez no último ciclo e a IA ligada. Três colunas com rótulo, valor e
   * detalhe, para o hero dizer o que vem a seguir e não só o que já aconteceu.
   */
  protected readonly pulse = computed(() => {
    const now = this.now();
    const e = this.explore.value(), s = this.search.state(), full = this.search.lastFull(), o = this.hub.state();
    const every = e?.searchIntervalMinutes ?? 60;
    const minutes = (iso: string | null | undefined) => iso ? Math.round((new Date(iso).getTime() - now) / 60_000) : null;
    const due = full?.lastAt ? minutes(new Date(new Date(full.lastAt).getTime() + every * 60_000).toISOString()) : null;
    const search = s?.running
      ? { value: 'Agora', note: `${s.sources.filter((x) => x.state === 'done' || x.state === 'failed').length} de ${s.sources.length} fontes lidas` }
      : { value: due === null ? `A cada ${every} min` : due <= 1 ? 'Em instantes' : `Em ${due} min`, note: full?.lastAt ? `última ${ago(full.lastAt)}, ${plural(full.kept, 'vaga da sua área', 'vagas da sua área')}` : 'ainda sem rodada completa' };
    const last = e?.last;
    const next = minutes(e?.next);
    const explorer = !e ? { value: '—', note: 'carregando' }
      : e.running ? { value: 'Testando fontes', note: `${e.frontier.toLocaleString('pt-BR')} candidatas na fila` }
      : !e.enabled ? { value: 'Desligado', note: 'ligue em Fontes' }
      : { value: last ? plural(last.added.length, 'fonte nova', 'fontes novas') : 'Sem ciclo ainda', note: last ? `${last.tested} testadas ${ago(last.at)}${next !== null && next > 0 ? `, próximo em ${next} min` : ''}` : `${e.frontier.toLocaleString('pt-BR')} candidatas na fila` };
    const ai = !o ? { value: '—', note: 'carregando' }
      : o.aiReady ? { value: plural(o.accounts.length, 'conta ligada', 'contas ligadas'), note: o.accounts.slice(0, 3).join(', ') }
      : { value: 'Sem IA', note: 'conecte em Provedores' };
    return [
      { label: 'Próxima busca', ...search, link: '/busca' },
      { label: 'Explorador', ...explorer, link: '/fontes' },
      { label: 'IA', ...ai, link: '/provedores' },
    ];
  });

  /** Estações da rota: acende a que trabalhou por último (ao vivo). */
  protected readonly stops = computed<RouteStop[]>(() => {
    const last = this.live.feed().at(-1)?.message.agent ?? '';
    const on = (names: string[]) => this.live.busy() && names.includes(last);
    return [
      { at: 0.06, icon: 'radar', label: 'Batedor', active: on(['Batedor']) },
      { at: 0.3, icon: 'chart', label: 'Analista', active: on(['Analista']) },
      { at: 0.49, icon: 'writer', label: 'Redator', active: on(['Seletor', 'Redator', 'Conferente', 'Revisor', 'Renderizador', 'Inspetor ATS']) },
      { at: 0.72, icon: 'guardian', label: 'Guardião', active: on(['Guardião']) },
      { at: 0.94, icon: 'send', label: 'Envio', active: on(['Envio', 'Verificador']) },
    ];
  });


  protected readonly funnel = computed<ChartRow[]>(() => {
    const d = this.data.value();
    if (!d) return [];
    const a = d.applications;
    return [
      { label: 'Encontradas', value: d.jobs.total, tone: 'muted' },
      { label: 'Na fila', value: d.jobs.shortlisted },
      { label: 'Preparadas', value: a.prepared },
      { label: 'Esperando você', value: a.awaitingApproval + a.needsHuman, tone: 'warn' },
      { label: 'Confirmadas', value: a.confirmed, tone: 'ok' },
    ];
  });

  /** As últimas falas dos agentes, ao vivo. */
  protected readonly voices = computed(() => this.live.feed().slice(-5).reverse());

  protected async runNow(id: string) {
    await this.run.run(id);
    await this.data.reload();
  }
}

/** Só a palavra no singular ou no plural: o número vai à parte, em destaque. */
function word(n: number, one: string, many: string) { return n === 1 ? one : many; }
