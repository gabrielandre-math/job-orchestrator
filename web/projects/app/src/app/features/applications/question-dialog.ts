import { ChangeDetectionStrategy, Component, ElementRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { Iso } from '@jo/ui/iso';
import { Icon } from '@jo/ui/icon';
import { FormQuestions, QuestionGroup } from '../../core/form-questions';
import { send } from '../../core/api';
import { command } from '../../core/command';

/**
 * Suas respostas: what the AI could not answer from your profile, one line per meaning. Declarations from several forms are one
 * consent, the same question in two languages is one line, and a draft the AI wrote comes ready to edit. It never opens by itself
 * over the page: the reminder in the corner, the side menu or an application opens it.
 */
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
  protected readonly groups = signal<QuestionGroup[]>([]);
  protected readonly values = signal<Record<string, string>>({});
  protected readonly shown = signal<Record<string, boolean>>({});
  protected readonly today = new Date().toLocaleDateString('en-CA');
  protected readonly answered = computed(() => this.groups().filter((g) => !!this.values()[g.key]?.trim()).length);
  protected readonly affected = computed(() => new Set(this.groups().flatMap((g) => g.applications)).size);
  protected readonly saving = command((answers: { question: string; answer: string; key: string }[]) =>
    send<{ resumed: number; handedOff: number }>('POST', '/applications/questions/answers', { answers }),
    (r) => `Respostas salvas. ${r.resumed} candidatura(s) voltaram ao preparo${r.handedOff ? `, ${r.handedOff} ficaram com você` : ''}.`);

  constructor() {
    effect(() => {
      const box = this.dialog()?.nativeElement;
      if (this.enabled() && this.hub.open() && box && !box.open) {
        const list = (this.hub.data.value() ?? []).slice(0, 30);
        this.groups.set(list);
        // A draft the AI wrote comes ready to edit.
        this.values.set(Object.fromEntries(list.filter((g) => g.draft).map((g) => [g.key, g.draft!])));
        if (list.length) box.showModal(); else this.hub.open.set(false);
      } else if ((!this.enabled() || !this.hub.open()) && box?.open) box.close();
    });
  }

  protected set(key: string, value: string) { this.values.update((v) => ({ ...v, [key]: value })); }
  protected picked(key: string, value: string) { return this.values()[key] === value; }
  protected ticked(key: string, option: string) { return (this.values()[key] ?? '').split('\n').includes(option); }
  protected tick(key: string, option: string) {
    const all = (this.values()[key] ?? '').split('\n').filter(Boolean);
    this.set(key, (all.includes(option) ? all.filter((v) => v !== option) : [...all, option]).join('\n'));
  }
  protected toggle(key: string) { this.shown.update((s) => ({ ...s, [key]: !s[key] })); }
  protected later() {
    if (this.saving.running()) return;
    this.hub.open.set(false);
  }
  protected async save() {
    const answers = this.groups().filter((g) => !!this.values()[g.key]?.trim()).map((g) => ({ question: g.title, answer: this.values()[g.key].trim(), key: g.key }));
    if (!answers.length) return;
    if ((await this.saving.run(answers)) === null) return;
    this.hub.open.set(false);
    this.values.set({});
    await this.hub.data.reload();
  }
}
