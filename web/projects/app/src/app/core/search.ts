import { Injectable, computed, effect, inject, signal, untracked } from '@angular/core';
import { Campaign, get, send } from './api';
import { AgentMessage, AgentThread, Live } from './live';
import { SourceInfo, describeSource } from './sources';
import { Toasts } from './toast';

export interface SourceState extends SourceInfo { state: 'waiting' | 'working' | 'done' | 'failed'; listed: number; kept: number; seconds: number | null; error: string | null }
export interface TopJob { company: string; title: string; score: number; domain: string | null; id: string }
export interface SearchState {
  threadId: string; startedAt: string; lastAt: string; running: boolean; stage: 'start' | 'source' | 'dedupe' | 'score' | 'done';
  sources: SourceState[]; listed: number; kept: number; fresh: number | null; again: number | null; duplicates: number | null; ignored: number;
  scored: number | null; above: number | null; shortlisted: number | null; cut: number; bands: number[] | null; seconds: number | null; top: TopJob[]; messages: AgentMessage[];
}

/**
 * A busca da campanha ao vivo para o app inteiro: junta o último fio "search" gravado com as mensagens que chegam pelo SSE
 * e monta o estado de cada fonte e de cada etapa a partir dos números (Data) que a rodada narra.
 */
@Injectable({ providedIn: 'root' })
export class SearchRun {
  private readonly live = inject(Live);
  private readonly toasts = inject(Toasts);
  private readonly history = signal<{ threadId: string; messages: AgentMessage[] } | null>(null);
  /** Pedido feito e a primeira mensagem ainda não chegou. */
  readonly starting = signal(false);
  private told = new Set<string>();

  constructor() {
    void this.load();
    // Aviso quando uma busca termina enquanto o app está aberto.
    effect(() => {
      const last = this.live.feed().at(-1);
      if (!last || last.kind !== 'search') return;
      untracked(() => {
        this.starting.set(false);
        const d = parse(last.message.data);
        if (d?.['stage'] === 'done' && !this.told.has(last.threadId)) {
          this.told.add(last.threadId);
          const n = Number(d['shortlisted'] ?? 0);
          this.toasts.ok(n > 0 ? `Busca concluída: ${n} ${n === 1 ? 'vaga nova' : 'vagas novas'} na fila.` : 'Busca concluída. Nenhuma vaga nova acima do corte desta vez.');
        }
      });
    });
  }

  async load() {
    try {
      const threads = await get<AgentThread[]>('/agents/threads?limit=40');
      const last = threads.find((t) => t.kind === 'search');
      this.history.set(last ? { threadId: last.id, messages: await get<AgentMessage[]>(`/agents/threads/${last.id}`) } : null);
    } catch { /* sem API: a tela mostra o estado vazio */ }
  }

  /** Pede uma rodada agora para a primeira campanha. */
  async start() {
    this.starting.set(true);
    try {
      const campaigns = await get<Campaign[]>('/campaigns');
      if (!campaigns[0]) { this.starting.set(false); this.toasts.bad('Crie a campanha antes de buscar.'); return; }
      await send('POST', `/campaigns/${campaigns[0].id}/runs`);
      setTimeout(() => this.starting.set(false), 30_000);
    } catch { this.starting.set(false); this.toasts.bad('A API não respondeu. Confira se o serviço está no ar.'); }
  }

  readonly state = computed<SearchState | null>(() => {
    const feed = this.live.feed().filter((l) => l.kind === 'search');
    const h = this.history();
    const threadId = feed.at(-1)?.threadId ?? h?.threadId;
    if (!threadId) return null;
    const base = h?.threadId === threadId ? h.messages : [];
    const seen = new Set(base.map((m) => m.id));
    const extra = feed.filter((l) => l.threadId === threadId && !seen.has(l.message.id)).map((l) => l.message);
    return fold(threadId, [...base, ...extra].sort((a, b) => a.at.localeCompare(b.at)));
  });

  /** De 0 a 1: fontes pesam 80% e as três etapas finais o resto. */
  readonly progress = computed(() => {
    const s = this.state();
    if (!s) return 0;
    if (!s.running) return 1;
    const done = s.sources.filter((x) => x.state === 'done' || x.state === 'failed').length;
    const base = s.sources.length ? (done / s.sources.length) * 0.8 : 0;
    return Math.min(0.98, base + (s.fresh !== null ? 0.07 : 0) + (s.scored !== null ? 0.07 : 0));
  });
}

function parse(data: string | null | undefined): Record<string, unknown> | null {
  if (!data) return null;
  try { return JSON.parse(data) as Record<string, unknown>; } catch { return null; }
}

function fold(threadId: string, messages: AgentMessage[]): SearchState {
  const s: SearchState = {
    threadId, startedAt: messages[0]?.at ?? '', lastAt: messages.at(-1)?.at ?? '', running: true, stage: 'start', sources: [], listed: 0, kept: 0,
    fresh: null, again: null, duplicates: null, ignored: 0, scored: null, above: null, shortlisted: null, cut: 0, bands: null, seconds: null, top: [], messages,
  };
  const bySource = new Map<string, SourceState>();
  const ensure = (source: string) => {
    let x = bySource.get(source);
    if (!x) { x = { ...describeSource(source), state: 'waiting', listed: 0, kept: 0, seconds: null, error: null }; bySource.set(source, x); s.sources.push(x); }
    return x;
  };
  for (const m of messages) {
    const d = parse(m.data);
    if (!d) continue;
    const num = (k: string) => (typeof d[k] === 'number' ? (d[k] as number) : 0);
    switch (d['stage']) {
      case 'start':
        s.cut = num('cut');
        for (const src of (d['sources'] as string[] | undefined) ?? []) ensure(src);
        break;
      case 'source': {
        s.stage = 'source';
        const x = ensure(String(d['source']));
        x.state = (d['state'] as SourceState['state']) ?? 'working';
        x.listed = num('listed'); x.kept = num('kept');
        x.seconds = typeof d['seconds'] === 'number' ? (d['seconds'] as number) : null;
        x.error = (d['error'] as string | undefined) ?? null;
        break;
      }
      case 'dedupe': s.stage = 'dedupe'; s.fresh = num('fresh'); s.again = num('again'); s.duplicates = num('duplicates'); s.ignored = num('ignored'); break;
      case 'score': s.stage = 'score'; s.scored = num('scored'); s.above = num('above'); s.shortlisted = num('shortlisted'); s.bands = (d['bands'] as number[] | undefined) ?? null; break;
      case 'done':
        s.stage = 'done'; s.running = false; s.seconds = num('seconds'); s.shortlisted = num('shortlisted');
        s.top = (d['top'] as TopJob[] | undefined) ?? [];
        break;
    }
  }
  s.listed = s.sources.reduce((n, x) => n + x.listed, 0);
  s.kept = s.sources.reduce((n, x) => n + x.kept, 0);
  // Uma rodada sem notícia há dez minutos não está mais rodando (o worker caiu ou foi reiniciado).
  if (s.running && s.lastAt && Date.now() - new Date(s.lastAt).getTime() > 10 * 60_000) s.running = false;
  return s;
}
