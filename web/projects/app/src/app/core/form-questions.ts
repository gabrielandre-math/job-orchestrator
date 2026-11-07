import { Injectable, computed, signal } from '@angular/core';
import { query } from './query';

export interface QuestionItem { key: string; label: string; kind: string; options: string[]; applications: string[]; }
/** One line of the questions window: the same question from several forms, under a short title, with one answer for all of them. */
export interface QuestionGroup {
  key: string;
  kind: 'consent' | 'date' | 'yesno' | 'choice' | 'checkboxes' | 'number' | 'text' | 'textarea' | 'arrived';
  title: string;
  detail: string;
  options: string[];
  companies: string[];
  applications: string[];
  items: QuestionItem[];
  draft: string | null;
  note: string | null;
}

/** Only questions and counts live in memory here. Answers are saved by the local API. */
@Injectable({ providedIn: 'root' })
export class FormQuestions {
  readonly data = query<QuestionGroup[]>(() => '/applications/questions', 8000);
  readonly open = signal(false);
  /** The reminder in the corner, hidden for this session after you close it. */
  readonly nudge = signal(true);
  readonly count = computed(() => this.data.value()?.length ?? 0);
  readonly waiting = computed(() => new Set((this.data.value() ?? []).flatMap((g) => g.applications)).size);
}
