import { Injectable, signal } from '@angular/core';
import { query } from './query';

export interface FormQuestion { key: string; label: string; kind: string; options: string[]; applications: string[]; }
/** Only questions and counts live in memory here. Answers are saved by the local API. */
@Injectable({ providedIn: 'root' })
export class FormQuestions {
  readonly data = query<FormQuestion[]>(() => '/applications/questions', 8000);
  readonly open = signal(false);
  readonly deferred = new Set<string>();
}
