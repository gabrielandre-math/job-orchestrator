import { ChangeDetectionStrategy, Component, computed, effect, input, signal } from '@angular/core';
import { send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';

interface InterviewRound {
  id: string;
  stage: string;
  createdAt: string;
  feedback: string | null;
  pack: {
    resumeVersionId: string | null;
    questions: { question: string; focus: string; evidence: { id: string; statement: string }[] }[];
    askEmployer: string[];
  };
}
interface PracticeFeedback { strength: string; improve: string }

@Component({
  selector: 'app-interview-panel',
  templateUrl: './interview-panel.html',
  styleUrl: './interview-panel.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InterviewPanel {
  readonly applicationId = input.required<string>();
  protected readonly stages = [
    { key: 'screen', label: 'Triagem' }, { key: 'technical', label: 'Técnica' },
    { key: 'case', label: 'Estudo de caso' }, { key: 'final', label: 'Final' },
  ];
  protected readonly stage = signal('screen');
  protected readonly previousFeedback = signal('');
  protected readonly rounds = query<InterviewRound[]>(() => this.path());
  protected readonly selected = signal<InterviewRound | null>(null);
  protected readonly round = computed(() => this.selected() ?? this.rounds.value()?.[0] ?? null);
  protected readonly questionIndex = signal(0);
  protected readonly answer = signal('');
  protected readonly result = signal<PracticeFeedback | null>(null);
  protected readonly note = signal<string | null>(null);
  protected readonly preparing = command(() => send<InterviewRound>('POST', this.path(), {
    stage: this.stage(), previousFeedback: this.previousFeedback(),
  }));
  protected readonly saving = command((roundId: string) =>
    send('PUT', `${this.path()}/${roundId}/feedback`, { feedback: this.note() }), 'Feedback salvo para a próxima etapa.');
  protected readonly practicing = command((roundId: string) =>
    send<PracticeFeedback>('POST', `${this.path()}/${roundId}/practice`, {
      questionIndex: this.questionIndex(), answer: this.answer(),
    }));
  constructor() {
    effect(() => {
      this.applicationId();
      this.selected.set(null);
      this.note.set(null);
      this.previousFeedback.set('');
      this.chooseQuestion(0);
    });
  }

  private path() { return `/applications/${this.applicationId()}/interviews`; }

  protected stageLabel(stage: string) { return this.stages.find((x) => x.key === stage)?.label ?? stage; }
  protected date(iso: string) { return new Date(iso).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }); }

  protected chooseRound(round: InterviewRound) {
    this.selected.set(round);
    this.note.set(null);
    this.chooseQuestion(0);
  }

  protected chooseQuestion(index: number) {
    this.questionIndex.set(index);
    this.answer.set('');
    this.result.set(null);
  }

  protected async prepare() {
    const id = this.applicationId();
    const round = await this.preparing.run(undefined);
    if (!round || this.applicationId() !== id) return;
    this.previousFeedback.set('');
    this.chooseRound(round);
    await this.rounds.reload();
  }

  protected async save(round: InterviewRound) {
    const value = this.note();
    if (value === null || (await this.saving.run(round.id)) === null) return;
    if (this.round()?.id === round.id && this.note() === value) {
      this.selected.set({ ...round, feedback: value.trim() || null });
      this.note.set(null);
    }
    await this.rounds.reload();
  }

  protected async practice(round: InterviewRound) {
    const index = this.questionIndex();
    const result = await this.practicing.run(round.id);
    if (result && this.round()?.id === round.id && this.questionIndex() === index) this.result.set(result);
  }
}
