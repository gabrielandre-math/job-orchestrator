import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Company } from '@jo/ui/company';
import { Empty } from '@jo/ui/empty';
import { Icon } from '@jo/ui/icon';
import { Score } from '@jo/ui/score';
import { ApplicationRow, send } from '../../core/api';
import { BrowserCopilot } from '../../core/browser-copilot';
import { command } from '../../core/command';
import { FormQuestions } from '../../core/form-questions';
import { query } from '../../core/query';
import { ago } from '../../core/format';

/** The stages of an application, in the order a person lives them. */
type Stage = 'prep' | 'send' | 'sent' | 'Entrevista' | 'Oferta' | 'closed';
const STAGES: { key: Stage; label: string; hint: string; drop: string | null }[] = [
  { key: 'prep', label: 'Preparando', hint: 'a IA monta currículo e respostas', drop: null },
  { key: 'send', label: 'Para enviar', hint: 'abra preenchida e envie', drop: null },
  { key: 'sent', label: 'Enviada', hint: 'esperando a empresa; a IA lê o seu e-mail', drop: '' },
  { key: 'Entrevista', label: 'Entrevista', hint: 'convite, teste ou conversa', drop: 'Entrevista' },
  { key: 'Oferta', label: 'Oferta', hint: 'proposta na mesa ou aceita', drop: 'Oferta' },
  { key: 'closed', label: 'Encerrada', hint: 'recusa, vaga fechada ou descartada', drop: 'Recusada' },
];
const PREP = ['Shortlisted', 'MaterialsReady', 'FormPreparing', 'FormValidated', 'ReadyToSubmit', 'Submitting', 'RetryScheduled', 'PausedProvider'];
const SENT = ['SubmittedConfirmed', 'SubmittedDeclared'];
const STOPPED = ['Failed', 'DeadLetter', 'Withdrawn'];

function stageOf(a: ApplicationRow): Stage {
  if (SENT.includes(a.state)) return a.outcome === 'Entrevista' ? 'Entrevista' : a.outcome === 'Oferta' || a.outcome === 'Aceita' ? 'Oferta' : a.outcome === 'Recusada' ? 'closed' : 'sent';
  if (STOPPED.includes(a.state)) return 'closed';
  return PREP.includes(a.state) ? 'prep' : 'send';
}

/**
 * Candidaturas: every application on its stage, as a board you can drag (after the send, the stages are yours to move, and the AI
 * moves them too when the company answers by e-mail) or as a list you can search and sort.
 */
@Component({
  selector: 'app-applications',
  imports: [RouterLink, Company, Empty, Icon, Score],
  templateUrl: './applications.page.html',
  styleUrl: './applications.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApplicationsPage {
  private readonly router = inject(Router);
  protected readonly copilot = inject(BrowserCopilot);
  protected readonly questions = inject(FormQuestions);
  /** ?etapa= keeps the list's filter in the address. */
  readonly etapa = input<string | undefined>();
  protected readonly all = query<ApplicationRow[]>(() => '/applications', 5000);
  protected readonly ago = ago;
  protected readonly stages = STAGES;
  protected readonly view = signal<'board' | 'list'>(read() === 'list' ? 'list' : 'board');
  protected readonly text = signal('');
  protected readonly sort = signal<'recent' | 'score'>('recent');

  protected readonly found = computed(() => {
    const q = fold(this.text().trim());
    return (this.all.value() ?? []).filter((a) => !q || fold(`${a.company} ${a.title}`).includes(q));
  });
  protected readonly columns = computed(() => STAGES.map((s) => ({ ...s, items: this.found().filter((a) => stageOf(a) === s.key).sort(order(this.sort())) })));
  protected readonly rows = computed(() => this.found().filter((a) => !this.etapa() || stageOf(a) === this.etapa()).sort(order(this.sort())));
  protected count(stage: Stage | undefined) { return this.found().filter((a) => !stage || stageOf(a) === stage).length; }
  protected stage(a: ApplicationRow) { return STAGES.find((s) => s.key === stageOf(a))!; }
  protected stageKey(a: ApplicationRow) { return stageOf(a); }
  protected isSent(a: ApplicationRow) { return SENT.includes(a.state); }
  /** One short line for the list: the outcome for a sent one, what is missing for one that waits, the last step otherwise. */
  protected line(a: ApplicationRow) {
    if (this.isSent(a)) return a.outcome ? `${a.outcome} ${ago(a.outcomeAt)}` : a.state === 'SubmittedDeclared' ? 'Enviada por você' : 'Enviada, confirmada pelo site';
    if (a.state === 'NeedsHuman') return a.lastReason ?? 'Falta uma resposta sua';
    if (a.state === 'ManualHandoff') return 'Pronta: abra preenchida e envie';
    return a.lastReason ?? '';
  }

  protected setView(v: 'board' | 'list') { this.view.set(v); try { localStorage.setItem('jo.applications.view', v); } catch { /* sem armazenamento */ } }
  protected filter(stage: Stage | undefined) { void this.router.navigate([], { queryParams: { etapa: stage ?? null } }); }

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
}

function order(by: 'recent' | 'score') {
  return by === 'score' ? (a: ApplicationRow, b: ApplicationRow) => b.score - a.score : (a: ApplicationRow, b: ApplicationRow) => (b.outcomeAt ?? b.updatedAt).localeCompare(a.outcomeAt ?? a.updatedAt);
}
function fold(s: string) { return s.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, ''); }
function read(): string | null { try { return localStorage.getItem('jo.applications.view'); } catch { return null; } }
