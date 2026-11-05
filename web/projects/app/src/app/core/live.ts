import { Injectable, signal } from '@angular/core';

export interface AgentMessage {
  id: string; at: string; agent: string; kind: 'ask' | 'working' | 'answer' | 'note' | 'fail'; providerId: string | null; model: string | null;
  effort: string | null; title: string; body: string | null; seconds: number; ok: boolean; to: string | null; data?: string | null;
}
export interface LiveMessage { threadId: string; kind: string; subject: string; message: AgentMessage; }
export interface AgentThread { id: string; kind: string; subject: string; startedAt: string; lastAt: string; messages: number; agents: string[]; providers: string[]; failed: boolean; }

/**
 * Conversas dos agentes ao vivo: uma conexão SSE com /api/agents/stream para o app inteiro. O navegador reconecta sozinho.
 * busy acende enquanto algum modelo trabalha e apaga seis segundos depois da última mensagem.
 */
@Injectable({ providedIn: 'root' })
export class Live {
  readonly feed = signal<LiveMessage[]>([]);
  readonly busy = signal(false);
  readonly connected = signal(false);
  private timer?: ReturnType<typeof setTimeout>;

  constructor() {
    if (typeof EventSource === 'undefined') return;
    const es = new EventSource('/api/agents/stream');
    es.addEventListener('open', () => this.connected.set(true));
    es.addEventListener('error', () => this.connected.set(false));
    es.addEventListener('message', (e) => this.push(JSON.parse((e as MessageEvent<string>).data) as LiveMessage));
  }

  private push(m: LiveMessage) {
    this.feed.update((f) => [...f.slice(-299), m]);
    this.busy.set(true);
    clearTimeout(this.timer);
    this.timer = setTimeout(() => this.busy.set(false), m.message.kind === 'working' ? 10 * 60_000 : 6000);
  }
}

/** Ícone isométrico de cada agente e logo de cada provedor. */
export const AGENT_ICON: Record<string, string> = {
  Leitor: 'reader', Extrator: 'extractor', Conferente: 'checker', Seletor: 'selector', Redator: 'writer', Revisor: 'reviewer',
  Renderizador: 'renderer', 'Inspetor ATS': 'ats', 'Guardião': 'guardian', Roteador: 'router', 'Você': 'you', Supervisor: 'router',
  Batedor: 'radar', Normalizador: 'reader', Juiz: 'chart', Analista: 'queue', Semeador: 'selector', Robots: 'guardian', Coletor: 'radar', Assistente: 'router', Garimpeiro: 'radar', Curador: 'checker', Navegador: 'reader',
};
export const PROVIDER_LOGO: Record<string, { file: string; mono: boolean }> = {
  'claude-pro': { file: 'claude', mono: false }, 'claude-max': { file: 'claude', mono: false }, codex: { file: 'openai', mono: true }, ollama: { file: 'ollama', mono: true },
};
