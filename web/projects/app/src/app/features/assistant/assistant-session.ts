import { Injectable, signal } from '@angular/core';
import { AskResponse, AssistantAction, send } from '../../core/api';
import { command } from '../../core/command';

export interface Bubble { id: string; role: 'user' | 'assistant' | 'route' | 'error'; text: string; html: string; actions: AssistantAction[]; model: string | null; provider: string | null; rung: string | null; escalatedFrom: string | null; seconds: number | null; compare: boolean }

/**
 * The open conversation outlives the page: leave while a model writes and the question, the working line and the answer are there when
 * you come back. The server keeps every conversation; this only remembers which one is open and what is on screen.
 */
@Injectable({ providedIn: 'root' })
export class AssistantSession {
  readonly current = signal<string | null>(null);
  readonly bubbles = signal<Bubble[]>([]);
  readonly asking = command((body: object) => send<AskResponse>('POST', '/assistant/ask', body));
}
