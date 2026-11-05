import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { Iso } from '@jo/ui/iso';
import { Icon } from '@jo/ui/icon';
import { get } from '../../core/api';
import { AGENT_ICON, AgentMessage, AgentThread, Live, PROVIDER_LOGO } from '../../core/live';

/**
 * A conversa dos agentes, como numa ferramenta de orquestração: à esquerda os trabalhos recentes, à direita a troca
 * mensagem a mensagem, ao vivo. Pedido mostra o prompt (já sem dados pessoais); resposta mostra o JSON; nota mostra o que
 * cada agente sem IA conferiu e para quem passou.
 */
@Component({
  selector: 'app-conversation',
  imports: [Iso, Icon],
  templateUrl: './conversation.html',
  styleUrl: './conversation.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'close.emit()' },
})
export class Conversation {
  private readonly live = inject(Live);
  private readonly destroy = inject(DestroyRef);
  private readonly scroller = viewChild<ElementRef<HTMLDivElement>>('scroller');

  readonly thread = input<string | null>(null);
  readonly focus = input<string | null>(null);
  readonly close = output<void>();

  protected readonly threads = signal<AgentThread[]>([]);
  protected readonly selected = signal<string | null>(null);
  protected readonly loaded = signal<AgentMessage[]>([]);
  protected readonly open = signal<Set<string>>(new Set());
  protected readonly now = signal(Date.now());
  protected readonly icons = AGENT_ICON;
  protected readonly logos = PROVIDER_LOGO;

  /** Mensagens do fio: as carregadas mais as que chegaram ao vivo depois. */
  protected readonly messages = computed(() => {
    const id = this.selected();
    const seen = new Set(this.loaded().map((m) => m.id));
    const extra = this.live.feed().filter((l) => l.threadId === id && !seen.has(l.message.id)).map((l) => l.message);
    return [...this.loaded(), ...extra];
  });

  /** Quem ainda está trabalhando: "working" sem resposta do mesmo provedor depois. */
  protected readonly working = computed(() => {
    const list = this.messages();
    return list.filter((m, i) => m.kind === 'working' && !list.slice(i + 1).some((n) => (n.kind === 'answer' || n.kind === 'fail') && n.providerId === m.providerId));
  });

  protected readonly current = computed(() => this.threads().find((t) => t.id === this.selected()) ?? null);

  constructor() {
    const tick = setInterval(() => this.now.set(Date.now()), 1000);
    this.destroy.onDestroy(() => clearInterval(tick));
    void this.loadThreads();
    effect(() => {
      // Fio novo chegando ao vivo entra no topo da lista; se nada estiver aberto, abre sozinho.
      const last = this.live.feed().at(-1);
      if (!last) return;
      const known = this.threads().some((t) => t.id === last.threadId);
      if (!known) void this.loadThreads(this.selected() ?? last.threadId);
      else this.threads.update((ts) => ts.map((t) => (t.id === last.threadId ? { ...t, lastAt: last.message.at, messages: t.messages + 1 } : t)));
      queueMicrotask(() => this.follow());
    });
  }

  private async loadThreads(prefer?: string) {
    const list = await get<AgentThread[]>('/agents/threads?limit=40');
    this.threads.set(list);
    const want = prefer ?? this.thread() ?? list[0]?.id ?? null;
    if (want && want !== this.selected()) await this.pick(want);
  }

  protected async pick(id: string) {
    this.selected.set(id);
    this.loaded.set(await get<AgentMessage[]>(`/agents/threads/${id}`));
    queueMicrotask(() => this.follow(true));
  }

  /** Rola para o fim quando chega mensagem, se você já estava no fim. */
  private follow(force = false) {
    const el = this.scroller()?.nativeElement;
    if (!el) return;
    if (force || el.scrollHeight - el.scrollTop - el.clientHeight < 160) el.scrollTo({ top: el.scrollHeight, behavior: force ? 'auto' : 'smooth' });
  }

  protected toggle(id: string) {
    this.open.update((s) => { const n = new Set(s); if (n.has(id)) n.delete(id); else n.add(id); return n; });
  }

  protected isWorking(m: AgentMessage) { return this.working().includes(m); }
  protected speaker(m: AgentMessage) { return m.kind === 'answer' || m.kind === 'working' || (m.kind === 'fail' && m.providerId) ? 'provider' : 'agent'; }
  protected live30(t: AgentThread) { return this.now() - new Date(t.lastAt).getTime() < 30_000; }
  protected time(iso: string) { return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' }); }
  protected day(iso: string) { return new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }); }
  protected elapsed(iso: string) { return Math.max(0, Math.round((this.now() - new Date(iso).getTime()) / 1000)); }
  protected kindLabel(k: string) { return ({ profile: 'Leitura do currículo', resume: 'Currículo da vaga' } as Record<string, string>)[k] ?? k; }

  /** Corpo legível: JSON formatado; texto como veio. */
  protected pretty(body: string | null) {
    if (!body) return '';
    const t = body.trim();
    if (t.startsWith('{') || t.startsWith('[')) { try { return JSON.stringify(JSON.parse(t), null, 2); } catch { return body; } }
    return body;
  }
  protected long(body: string | null) { return !!body && (body.length > 420 || body.split('\n').length > 8); }
}
