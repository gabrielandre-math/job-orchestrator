import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { Iso } from '@jo/ui/iso';
import { Icon } from '@jo/ui/icon';
import { FormQuestion, FormQuestions } from '../../core/form-questions';
import { send } from '../../core/api';
import { command } from '../../core/command';

@Component({
  selector: 'app-question-dialog',
  imports: [Iso, Icon],
  templateUrl: './question-dialog.html',
  styleUrl: './question-dialog.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QuestionDialog {
  readonly enabled = input(true);
  protected readonly hub = inject(FormQuestions);
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('box');
  protected readonly questions = signal<FormQuestion[]>([]);
  protected readonly values = signal<Record<string, string>>({});
  protected readonly today = new Date().toLocaleDateString('en-CA');
  protected readonly affected = computed(() => new Set(this.questions().flatMap(q => q.applications)).size);
  protected readonly ready = computed(() => this.questions().length > 0 && this.questions().every(q => !!this.values()[q.key]?.trim()));
  protected readonly saving = command((answers: { question: string; answer: string }[]) => send<{ resumed: number }>('POST', '/applications/questions/answers', { answers }),
    r => `Respostas salvas. ${r.resumed} candidatura(s) voltaram ao preparo.`);

  constructor() {
    effect(() => {
      const list = this.hub.data.value() ?? [];
      if (this.enabled() && list.some(q => !this.hub.deferred.has(q.key))) this.hub.open.set(true);
    });
    effect(() => {
      const box = this.dialog()?.nativeElement;
      if (this.enabled() && this.hub.open() && box && !box.open) {
        this.questions.set((this.hub.data.value() ?? []).slice(0, 30));
        if (this.questions().length) box.showModal();
      } else if ((!this.enabled() || !this.hub.open()) && box?.open) box.close();
    });
  }
  protected set(key: string, value: string) { this.values.update(v => ({ ...v, [key]: value })); }
  protected tick(q: FormQuestion, option: string, checked: boolean) {
    const all = (this.values()[q.key] ?? '').split('\n').filter(Boolean).filter(v => v !== option);
    this.set(q.key, [...all, ...(checked ? [option] : [])].join('\n'));
  }
  protected later() {
    if (this.saving.running()) return;
    this.questions().forEach(q => this.hub.deferred.add(q.key));
    this.hub.open.set(false);
  }
  protected async save() {
    if (!this.ready()) return;
    const r = await this.saving.run(this.questions().map(q => ({ question: q.label, answer: this.values()[q.key].trim() })));
    if (r === null) return;
    this.questions().forEach(q => this.hub.deferred.add(q.key));
    this.hub.open.set(false);
    this.values.set({});
    await this.hub.data.reload();
  }
}
