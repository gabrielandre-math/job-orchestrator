import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Score } from '@jo/ui/score';
import { ApplicationRow, MailAccountView, MailOverview, Overview, Profile, TaskRow, send } from '../../core/api';
import { BrowserCopilot, CopilotQueueItem } from '../../core/browser-copilot';
import { command } from '../../core/command';
import { ago, clock, plural } from '../../core/format';
import { FormQuestions } from '../../core/form-questions';
import { Live } from '../../core/live';
import { query } from '../../core/query';
import { SearchRun } from '../../core/search';
import { WelcomeHub } from '../../core/welcome-hub';

/** One step of the path: what it is, how many wait, and the single action that moves it. */
interface Step {
  key: 'setup' | 'copilot' | 'questions' | 'site' | 'inbox' | 'mail' | 'other';
  label: string; hint: string; doneHint: string; n: number;
  iso: string; title: string; text: string; action: string; short: string;
  alt?: { label: string; link: string };
}

/**
 * Início: one next step with one button, the path in the order that unlocks the most (configure, install the copilot, answer the
 * questions, send what is ready, connect the inbox), the results that count, and what the app is doing now. Nothing else.
 */
@Component({
  selector: 'app-overview',
  imports: [RouterLink, Company, Icon, Iso, Score],
  templateUrl: './overview.page.html',
  styleUrl: './overview.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OverviewPage {
  protected readonly live = inject(Live);
  protected readonly search = inject(SearchRun);
  protected readonly hub = inject(WelcomeHub);
  protected readonly questions = inject(FormQuestions);
  protected readonly copilot = inject(BrowserCopilot);
  private readonly router = inject(Router);
  /** The old "Configurar tudo" link (?configurar=1) still opens the guided setup. */
  readonly configurar = input<string | undefined>();
  private readonly openSetup = effect(() => {
    if (!this.configurar() || !this.hub.state()) return;
    untracked(() => {
      this.hub.open.set(true);
      void this.router.navigate([], { queryParams: { configurar: null }, queryParamsHandling: 'merge', replaceUrl: true });
    });
  });

  protected readonly data = query<Overview>(() => '/overview', 4000);
  private readonly tasks = query<TaskRow[]>(() => '/tasks', 6000);
  private readonly mail = query<MailOverview>(() => '/mail/overview', 20000);
  private readonly accounts = query<MailAccountView[]>(() => '/mail/accounts', 30000);
  private readonly profile = query<Profile>(() => '/profile', 0);
  private readonly apps = query<ApplicationRow[]>(() => '/applications', 15000);
  private readonly queue = query<CopilotQueueItem[]>(() => '/copilot/queue', 6000);
  protected readonly ago = ago;
  protected readonly clock = clock;

  /** Jobs skipped in this visit: they stay ready, the card shows the next one. */
  private readonly skipped = signal<ReadonlySet<string>>(new Set());
  /** The job opened from the card: the card then waits for the send instead of opening the same one again. */
  protected readonly opened = signal<string | null>(null);
  protected readonly ready = computed(() => (this.queue.value() ?? []).filter((q) => !this.skipped().has(q.id)));
  protected readonly current = computed(() => {
    const list = this.ready();
    return list.find((q) => q.id === this.opened()) ?? list[0] ?? null;
  });
  protected readonly declaring = command((id: string) => send('POST', `/applications/${id}/declare`, {}), 'Registrada como enviada por você.');

  private readonly inbox = computed(() => {
    const mine = (this.profile.value()?.identity?.email ?? '').trim().toLowerCase();
    const read = (this.accounts.value() ?? []).map((a) => a.address.toLowerCase());
    if (!this.accounts.value() || !this.profile.value()) return null;
    if (read.length === 0) return { mine, read: null as string | null };
    return mine && !read.includes(mine) ? { mine, read: read[0] } : null;
  });

  protected readonly steps = computed<Step[]>(() => {
    const o = this.hub.state(), d = this.data.value(), tasks = (this.tasks.value() ?? []).filter((t) => !t.resolvedAt);
    const missing = !o || o.done ? [] : [!o.aiReady && 'a IA', o.facts === 0 && 'o currículo', (o.keywords === 0 || o.sources === 0) && 'o que buscar'].filter(Boolean) as string[];
    const site = this.queue.value()?.length ?? 0;
    const copilotOn = this.copilot.here() || !!this.copilot.status.value()?.connected;
    const asked = this.questions.count(), waiting = this.questions.waiting();
    const inbox = this.inbox();
    const isSite = (t: TaskRow) => t.kind === 'Review' && t.title.startsWith('Candidatar-se pelo site');
    const other = tasks.filter((t) => !isSite(t) && t.kind !== 'Question').length;
    const steps: Step[] = [
      { key: 'setup', label: 'Configurar a busca', hint: `Falta ${missing.join(', ')}`, doneHint: 'IA, currículo e o que buscar prontos', n: missing.length,
        iso: 'identity', title: 'Termine a configuração', text: `Falta ${missing.join(', ')}. O assistente guia cada passo e monta a busca a partir do seu currículo.`,
        action: 'Continuar a configuração', short: 'Continuar' },
      this.copilot.outdated()
        ? { key: 'copilot', label: 'Recarregar o copiloto', hint: `Versão ${this.copilot.status.value()?.latest} disponível`, doneHint: 'Copiloto atualizado', n: 1,
            iso: 'selector', title: 'Recarregue o copiloto no Chrome', text: 'A versão nova pede à IA as respostas dos campos que ficarem vazios. No Chrome, abra chrome://extensions e clique na seta de recarregar do Job Orchestrator: leva cinco segundos.',
            action: 'Ver como recarregar', short: 'Recarregar' }
        : { key: 'copilot', label: 'Instalar o copiloto do navegador', hint: 'Abre cada vaga já preenchida, com o currículo anexado', doneHint: 'Copiloto conectado', n: site > 0 && !copilotOn ? 1 : 0,
            iso: 'selector', title: 'Instale o copiloto do navegador', text: 'Leva dois minutos. Com ele, cada vaga abre no seu navegador já preenchida e com o currículo anexado: você confere e clica em enviar, e a candidatura conta como enviada.',
            action: 'Ver como instalar', short: 'Instalar' },
      { key: 'questions', label: 'Responder perguntas dos formulários', hint: asked ? `${plural(waiting, 'candidatura espera', 'candidaturas esperam')} por elas` : '', doneHint: 'Nenhuma pergunta sem resposta', n: asked,
        iso: 'form', title: asked === 1 ? 'Responda 1 pergunta' : `Responda ${asked} perguntas`, text: 'Os formulários perguntaram o que o seu perfil não responde. Cada resposta vale para todas as vagas que perguntam o mesmo.',
        action: 'Responder agora', short: 'Responder' },
      { key: 'site', label: 'Enviar as candidaturas prontas', hint: 'A IA preparou; você confere e clica em enviar no site', doneHint: 'Nenhuma esperando envio', n: site,
        iso: 'send', title: 'Envie a próxima vaga', text: 'Ela abre no seu navegador já preenchida, com o currículo da vaga anexado. Confira, clique em enviar no site e a confirmação conta aqui sozinha. O que o copiloto não preencher está no kit de respostas.',
        action: 'Abrir preenchida', short: 'Enviar' },
      { key: 'inbox', label: 'Conectar o e-mail das candidaturas', hint: 'A IA lê as respostas das empresas e move as suas candidaturas', doneHint: 'A IA lê a caixa que vai nos formulários', n: inbox ? 1 : 0,
        iso: 'inbox', title: inbox?.read ? `Conecte ${inbox.mine}` : 'Conecte o seu e-mail',
        text: inbox?.read ? `Os formulários usam ${inbox.mine}, mas o app lê ${inbox.read}. As confirmações e as respostas das empresas chegam em ${inbox.mine}: conectado, a IA lê e organiza tudo sozinha.`
          : 'Com o e-mail conectado, a IA lê as confirmações, convites de entrevista e recusas e move cada candidatura sozinha.',
        action: inbox?.read ? `Conectar ${inbox?.mine}` : 'Conectar o e-mail', short: 'Conectar',
        alt: inbox?.read ? { label: `Usar ${inbox.read} nos formulários`, link: '/perfil' } : undefined },
      { key: 'mail', label: 'Aprovar e-mails para recrutadores', hint: 'Vagas que pedem o currículo por e-mail', doneHint: 'Nenhum e-mail esperando', n: this.mail.value()?.awaiting ?? 0,
        iso: 'writer', title: 'Revise os e-mails escritos pela IA', text: 'Algumas vagas pedem o currículo por e-mail. A IA escreveu cada um a partir do seu currículo: revise e aprove.',
        action: 'Revisar os e-mails', short: 'Revisar' },
      { key: 'other', label: 'Resolver outras pendências', hint: 'Login ou verificação que só você faz', doneHint: 'Nada mais esperando você', n: other,
        iso: 'guardian', title: other === 1 ? '1 candidatura precisa de você' : `${other} candidaturas precisam de você`, text: 'O site pediu login ou uma verificação que só você faz. Cada uma está no quadro de Candidaturas, com o próximo passo.',
        action: 'Ver no quadro', short: 'Ver' },
    ];
    // Steps that never apply here (no e-mail flow, nothing else waiting) stay out of the list instead of showing as done forever.
    return steps.filter((s) => s.n > 0 || ['setup', 'questions', 'site', 'inbox'].includes(s.key) || (s.key === 'copilot' && (site > 0 || copilotOn)));
  });
  /** The first step with something waiting: the one big card. */
  protected readonly next = computed(() => this.steps().find((s) => s.n > 0) ?? null);
  protected readonly doneCount = computed(() => this.steps().filter((s) => s.n === 0).length);

  protected readonly greeting = computed(() => {
    const h = new Date().getHours();
    const name = (this.profile.value()?.identity?.fullName ?? '').split(' ')[0];
    return `${h < 12 ? 'Bom dia' : h < 18 ? 'Boa tarde' : 'Boa noite'}${name ? `, ${name}` : ''}.`;
  });
  protected readonly lead = computed(() => {
    const d = this.data.value(), site = this.queue.value()?.length ?? 0;
    if (!d) return '';
    const sent = d.confirmedToday === 0 ? 'Nenhuma candidatura enviada hoje' : plural(d.confirmedToday, 'candidatura enviada hoje', 'candidaturas enviadas hoje');
    return site > 0 ? `${sent}. ${plural(site, 'pronta', 'prontas')} para você enviar.` : `${sent}.`;
  });

  /** What the app is doing now, in one line. */
  protected readonly now = computed(() => {
    const s = this.search.state(), d = this.data.value();
    const parts: string[] = [];
    if (s?.running) parts.push(`Buscando vagas: ${s.sources.filter((x) => x.state === 'done' || x.state === 'failed').length} de ${s.sources.length} fontes lidas`);
    else if (this.search.lastFull()?.lastAt) parts.push(`Última busca ${ago(this.search.lastFull()!.lastAt)}`);
    if (d && d.applications.inProgress > 0) parts.push(`${plural(d.applications.inProgress, 'candidatura', 'candidaturas')} em preparo`);
    return parts.join(' · ') || 'Parado. A próxima busca começa sozinha.';
  });
  protected readonly busy = computed(() => this.live.busy() || !!this.search.state()?.running);

  /** The numbers that answer "is this working": sent today, sent in total, answers from companies, interviews. */
  protected readonly results = computed(() => {
    const d = this.data.value(), apps = this.apps.value() ?? [];
    const answered = apps.filter((a) => a.outcome).length, interviews = apps.filter((a) => a.outcome === 'Entrevista').length;
    return [
      { value: d?.confirmedToday ?? 0, label: 'enviadas hoje', link: '/candidaturas' },
      { value: d?.applications.confirmed ?? 0, label: 'enviadas no total', link: '/candidaturas' },
      { value: answered, label: answered === 1 ? 'empresa respondeu' : 'empresas responderam', link: '/candidaturas' },
      { value: interviews, label: interviews === 1 ? 'entrevista' : 'entrevistas', link: '/candidaturas' },
    ];
  });
  protected readonly activity = computed(() => (this.data.value()?.activity ?? []).slice(0, 6));

  protected go(s: Step) {
    switch (s.key) {
      case 'setup': this.hub.open.set(true); break;
      case 'copilot': void this.router.navigate(['/configuracoes'], { fragment: 'copiloto' }); break;
      case 'questions': this.questions.open.set(true); break;
      case 'site': this.openCurrent(); break;
      case 'inbox': void this.router.navigate(['/email']); break;
      case 'mail': void this.router.navigate(['/email']); break;
      case 'other': void this.router.navigate(['/candidaturas']); break;
    }
  }

  protected openCurrent() {
    const job = this.current();
    if (!job) return;
    this.opened.set(job.id);
    this.copilot.open({ id: job.id, applyUrl: job.applyUrl });
  }
  /** Next job: the one opened stays ready (it leaves the list when the send is confirmed). */
  protected skip() {
    const job = this.current();
    if (!job) return;
    this.skipped.update((s) => new Set([...s, job.id]));
    this.opened.set(null);
  }
  protected async declare() {
    const job = this.current();
    if (!job || (await this.declaring.run(job.id)) === null) return;
    this.opened.set(null);
    await Promise.all([this.queue.reload(), this.data.reload()]);
  }
}
