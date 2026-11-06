import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Company } from '@jo/ui/company';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { Empty } from '@jo/ui/empty';
import { Icon } from '@jo/ui/icon';
import { Score } from '@jo/ui/score';
import { ApplicationRow, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';
import { ago, evidenceLabel, stateLabel, stateTone } from '../../core/format';

/** Candidaturas: cada vaga que entrou na fila, do currículo à confirmação. */
@Component({
  selector: 'app-applications',
  imports: [RouterLink, Badge, Empty, Icon, Score, Company, PageHero],
  templateUrl: './applications.page.html',
  styleUrl: './applications.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApplicationsPage {
  private readonly router = inject(Router);
  readonly state = input<string | undefined>();
  protected readonly all = query<ApplicationRow[]>(() => '/applications', 4000);
  protected readonly label = stateLabel;
  protected readonly tone = stateTone;
  protected readonly ago = ago;
  protected readonly evidence = evidenceLabel;
  protected readonly filters = [
    { key: undefined, label: 'Todas' }, { key: 'AwaitingApproval', label: 'Esperando você' }, { key: 'NeedsHuman', label: 'Precisa de você' },
    { key: 'FormValidated', label: 'Preparadas' }, { key: 'SubmittedConfirmed', label: 'Confirmadas' }, { key: 'SubmissionUnknown', label: 'Incertas' },
  ];
  protected readonly heroStats = computed<HeroStat[]>(() => {
    const all = this.all.value() ?? [];
    const by = (states: string[]) => all.filter((a) => states.includes(a.state)).length;
    return [
      { label: 'No total', value: all.length },
      { label: 'Em andamento', value: by(['Shortlisted', 'MaterialsReady', 'FormPreparing', 'FormValidated', 'ReadyToSubmit', 'Submitting', 'RetryScheduled']) },
      { label: 'Esperando você', value: by(['AwaitingApproval', 'NeedsHuman', 'ManualHandoff']), tone: by(['AwaitingApproval', 'NeedsHuman', 'ManualHandoff']) > 0 ? 'warn' : undefined },
      { label: 'Confirmadas', value: by(['SubmittedConfirmed', 'SubmittedDeclared']), tone: 'ok' },
      { label: 'Incertas', value: by(['SubmissionUnknown']), tone: by(['SubmissionUnknown']) > 0 ? 'warn' : undefined },
    ];
  });
  protected readonly rows = computed(() => (this.all.value() ?? []).filter((a) => !this.state() || a.state === this.state()));

  /** Quadro ou tabela (lembrado neste navegador). */
  protected readonly view = signal<'board' | 'table'>(read() === 'table' ? 'table' : 'board');
  protected setView(v: 'board' | 'table') { this.view.set(v); try { localStorage.setItem('jo.applications.view', v); } catch { /* sem armazenamento */ } }

  /**
   * O quadro: antes do envio quem move é a máquina (Em preparo, Com você); depois do envio, você arrasta (Enviada, Entrevista, Oferta,
   * Aceita, Recusada). Candidatura que parou antes de sair (falha, retirada, política) fica no fim, só para consulta.
   */
  protected readonly steps = OUTCOMES;
  protected readonly columns = computed(() => {
    const all = this.all.value() ?? [];
    const sent = (a: ApplicationRow) => SENT.includes(a.state);
    return [
      { key: 'prep', label: 'Em preparo', hint: 'currículo, formulário e conferência', drop: null as string | null, items: all.filter((a) => PREP.includes(a.state)) },
      { key: 'you', label: 'Com você', hint: 'aprovação, login, pergunta nova ou envio incerto', drop: null, items: all.filter((a) => YOU.includes(a.state)) },
      { key: 'sent', label: 'Enviada', hint: 'arraste quando a empresa responder', drop: '', items: all.filter((a) => sent(a) && !a.outcome) },
      ...OUTCOMES.map((o) => ({ key: o, label: o, hint: o === 'Entrevista' ? 'marcada ou feita' : o === 'Oferta' ? 'proposta na mesa' : o === 'Aceita' ? 'você aceitou' : 'a empresa disse não', drop: o, items: all.filter((a) => sent(a) && a.outcome === o) })),
      { key: 'stopped', label: 'Parou antes do envio', hint: 'falha, retirada ou política', drop: null, items: all.filter((a) => STOPPED.includes(a.state)) },
    ].filter((c) => c.key !== 'stopped' || c.items.length > 0);
  });
  protected readonly dragging = signal<string | null>(null);
  protected readonly over = signal<string | null>(null);
  private readonly moving = command((m: { id: string; outcome: string }) => send<void>('PUT', `/applications/${m.id}/outcome`, { outcome: m.outcome || null }));
  protected async move(id: string, outcome: string) {
    this.dragging.set(null);
    this.over.set(null);
    if ((await this.moving.run({ id, outcome })) !== null) await this.all.reload();
  }
  protected drop(e: DragEvent, outcome: string | null) {
    e.preventDefault();
    const id = this.dragging() ?? e.dataTransfer?.getData('text/plain');
    if (id && outcome !== null) void this.move(id, outcome);
  }
  protected isSent(a: ApplicationRow) { return SENT.includes(a.state); }
  protected count(key: string | undefined) { return (this.all.value() ?? []).filter((a) => !key || a.state === key).length; }
  protected filter(key: string | undefined) { void this.router.navigate([], { queryParams: { state: key ?? null } }); }
}

const PREP = ['Shortlisted', 'MaterialsReady', 'FormPreparing', 'FormValidated', 'ReadyToSubmit', 'Submitting', 'RetryScheduled', 'PausedProvider'];
const YOU = ['AwaitingApproval', 'NeedsHuman', 'ManualHandoff', 'SubmissionUnknown'];
const SENT = ['SubmittedConfirmed', 'SubmittedDeclared'];
const STOPPED = ['Failed', 'DeadLetter', 'Withdrawn', 'BlockedByPolicy'];
const OUTCOMES = ['Entrevista', 'Oferta', 'Aceita', 'Recusada'];

function read(): string | null { try { return localStorage.getItem('jo.applications.view'); } catch { return null; } }
