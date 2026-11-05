import { ChangeDetectionStrategy, Component, ElementRef, computed, inject, signal, viewChild } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Router } from '@angular/router';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Mark } from '@jo/ui/mark';
import { AskResponse, AssistantAction, Campaign, ChatTurn, ConversationView, Profile, get, send, upload } from '../../core/api';
import { command } from '../../core/command';
import { ago } from '../../core/format';
import { Live } from '../../core/live';
import { markdown } from '../../core/markdown';
import { query } from '../../core/query';
import { describeSource } from '../../core/sources';

interface Bubble { id: string; role: 'user' | 'assistant' | 'route' | 'error'; text: string; html: string; actions: AssistantAction[]; model: string | null; provider: string | null; rung: string | null; escalatedFrom: string | null; seconds: number | null; compare: boolean }
/** Linha da conversa: uma fala sozinha ou as respostas da mesma pergunta em modelos diferentes, uma ao lado da outra. */
type Row = { kind: 'one'; bubble: Bubble } | { kind: 'side'; id: string; bubbles: Bubble[] };

const RUNGS = [
  { key: null, label: 'Automático' }, { key: 'Local', label: 'Local' }, { key: 'Light', label: 'Leve' }, { key: 'Medium', label: 'Médio' }, { key: 'Strong', label: 'Forte' },
];
const RUNG_LABEL: Record<string, string> = { Local: 'modelo local', LocalStrong: 'local forte', Light: 'degrau leve', Medium: 'degrau médio', Strong: 'degrau forte', Top: 'degrau máximo' };
const SUGGESTIONS = [
  'Monte a minha busca a partir do meu currículo',
  'Quais fontes de vagas combinam com a minha área?',
  'Como eu subo a nota das vagas que combinam comigo?',
  'Explique em poucas palavras como o app funciona',
];

/**
 * Assistente: conversa com os modelos da pessoa. A triagem local escolhe o degrau, a resposta insegura sobe para um modelo mais forte,
 * "Comparar" manda a mesma pergunta para famílias diferentes lado a lado e o clipe envia o currículo para o perfil.
 * Ações propostas aparecem como cartões e só acontecem no Aplicar.
 */
@Component({
  selector: 'app-assistant',
  imports: [Icon, Iso, Mark, NgTemplateOutlet],
  templateUrl: './assistant.page.html',
  styleUrl: './assistant.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AssistantPage {
  private readonly router = inject(Router);
  private readonly live = inject(Live);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  protected readonly conversations = query<ConversationView[]>(() => '/assistant/conversations', 0);
  protected readonly current = signal<string | null>(null);
  protected readonly bubbles = signal<Bubble[]>([]);
  protected readonly draft = signal('');
  protected readonly compare = signal(false);
  protected readonly rung = signal<string | null>(null);
  protected readonly applied = signal<Set<string>>(new Set());
  protected readonly reading = signal(false);
  protected readonly rungs = RUNGS;
  protected readonly suggestions = SUGGESTIONS;
  protected readonly ago = ago;
  protected readonly asking = command((body: object) => send<AskResponse>('POST', '/assistant/ask', body));
  protected readonly applying = command((a: AssistantAction) => this.apply(a));

  /** Respostas seguidas do modo comparar (acertos e falhas) viram uma linha só, com uma coluna por modelo. */
  protected readonly rows = computed<Row[]>(() => {
    const rows: Row[] = [];
    for (const b of this.bubbles()) {
      const last = rows.at(-1);
      if (b.compare && last?.kind === 'side') last.bubbles.push(b);
      else if (b.compare) rows.push({ kind: 'side', id: b.id, bubbles: [b] });
      else rows.push({ kind: 'one', bubble: b });
    }
    return rows;
  });
  protected readonly wide = computed(() => this.rows().some((r) => r.kind === 'side'));

  /** Quem está trabalhando nesta conversa agora (roteador classificando, modelo escrevendo). */
  protected readonly working = computed(() => {
    if (!this.asking.running()) return null;
    const mine = this.live.feed().filter((l) => l.kind === 'assistant' && (!this.current() || l.threadId === this.current()));
    const last = mine.filter((l) => l.message.kind === 'ask' || l.message.kind === 'working').at(-1);
    if (!last) return { agent: 'Roteador', text: 'lendo a pergunta' };
    const m = last.message;
    return { agent: m.agent, text: m.agent === 'Roteador' ? 'classificando a pergunta no modelo local' : `escrevendo com ${m.model ?? m.providerId ?? 'o modelo'}${m.effort ? ` (${m.effort})` : ''}` };
  });

  protected async open(id: string | null) {
    this.current.set(id);
    if (!id) { this.bubbles.set([]); return; }
    const turns = await get<ChatTurn[]>(`/assistant/conversations/${id}`);
    this.bubbles.set(turns.map((t) => this.bubble(t)));
    this.scroll();
  }

  private bubble(t: ChatTurn): Bubble {
    const d = parse(t.data);
    return { id: t.id, role: t.role, text: t.text, html: t.role === 'assistant' ? markdown(t.text) : '', actions: (d?.['actions'] as AssistantAction[] | undefined) ?? [], model: (d?.['model'] as string) ?? null,
      provider: (d?.['providerId'] as string) ?? null, rung: (d?.['rung'] as string) ?? null, escalatedFrom: (d?.['escalatedFrom'] as string) ?? null, seconds: (d?.['seconds'] as number) ?? null, compare: !!d?.['compare'] };
  }

  protected async ask(text?: string) {
    const message = (text ?? this.draft()).trim();
    if (!message || this.asking.running()) return;
    this.draft.set('');
    this.bubbles.update((b) => [...b, { id: crypto.randomUUID(), role: 'user', text: message, html: '', actions: [], model: null, provider: null, rung: null, escalatedFrom: null, seconds: null, compare: false }]);
    this.scroll();
    const r = await this.asking.run({ conversationId: this.current(), message, compare: this.compare(), rung: this.rung() });
    if (!r) return;
    const fresh = !this.current();
    this.current.set(r.conversationId);
    const answers = r.answers.map((a): Bubble => a.ok
      ? { id: crypto.randomUUID(), role: 'assistant', text: a.reply, html: markdown(a.reply), actions: a.actions, model: a.model, provider: a.providerId, rung: a.rung, escalatedFrom: a.escalatedFrom, seconds: a.seconds, compare: r.answers.length > 1 }
      : { id: crypto.randomUUID(), role: 'error', text: a.error ?? 'Nenhum modelo respondeu.', html: '', actions: [], model: a.model, provider: a.providerId, rung: a.rung, escalatedFrom: null, seconds: null, compare: r.answers.length > 1 });
    this.bubbles.update((b) => [...b, ...answers]);
    this.scroll();
    if (fresh) await this.conversations.reload();
  }

  /** Clipe: o currículo vai para o perfil (substituindo o atual); quando a leitura termina, o assistente propõe a busca. */
  protected async attach(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.reading.set(true);
    try {
      const before = await get<Profile>('/profile');
      if (before.facts.length > 0) await send('DELETE', '/profile');
      await upload('/profile/documents', file);
      for (let i = 0; i < 120; i++) {
        await new Promise((r) => setTimeout(r, 1500));
        const p = await get<Profile>('/profile');
        const d = p.documents.at(-1);
        if (d?.status === 'Ready') { this.reading.set(false); await this.ask(`Enviei o meu currículo (${file.name}). Leia o meu perfil e proponha a minha busca: palavras-chave, locais e fontes.`); return; }
        if (d?.status === 'Failed') break;
      }
      this.bubbles.update((b) => [...b, { id: crypto.randomUUID(), role: 'error', text: 'Não deu para ler o currículo. Tente PDF, DOCX ou TXT.', html: '', actions: [], model: null, provider: null, rung: null, escalatedFrom: null, seconds: null, compare: false }]);
    } catch { /* o aviso de erro vem do toast */ }
    this.reading.set(false);
  }

  private async apply(a: AssistantAction) {
    if (a.type === 'navigate') { await this.router.navigate([a.values[0]]); return true; }
    const c = (await get<Campaign[]>('/campaigns'))[0];
    if (!c) throw { status: 0, code: 'no-campaign', detail: 'Nenhuma campanha para ajustar.' };
    const body = { mode: c.mode, minFitScore: c.minFitScore, maxSubmissionsPerDay: c.maxSubmissionsPerDay, technologies: c.technologies, locations: c.locations, sources: c.sources };
    switch (a.type) {
      case 'campaign.keywords': body.technologies = a.values; break;
      case 'campaign.locations': body.locations = a.values; break;
      case 'campaign.minScore': body.minFitScore = a.number ?? c.minFitScore; break;
      case 'campaign.mode': body.mode = a.values[0]; break;
      case 'sources.add': body.sources = [...new Set([...c.sources, ...a.values])]; break;
    }
    await send('PUT', `/campaigns/${c.id}`, body);
    return true;
  }
  protected async run(b: Bubble, a: AssistantAction, i: number) {
    if ((await this.applying.run(a)) !== null) this.applied.update((s) => new Set([...s, `${b.id}:${i}`]));
  }

  protected actionText(a: AssistantAction) {
    switch (a.type) {
      case 'sources.add': return a.values.map((s) => describeSource(s).label).join(', ');
      case 'campaign.minScore': return `nota mínima ${a.number}`;
      case 'navigate': return a.values[0];
      default: return a.values.join(', ');
    }
  }
  protected actionVerb(a: AssistantAction) { return a.type === 'navigate' ? 'Abrir' : 'Aplicar'; }
  protected rungLabel(r: string | null) { return r ? (RUNG_LABEL[r] ?? r) : ''; }
  protected logo(provider: string | null) { return !provider ? null : provider === 'ollama' ? 'ollama' : provider.startsWith('claude') || provider.includes('anthropic') ? 'claude' : provider.startsWith('codex') || provider.includes('openai') ? 'openai' : null; }
  protected key(e: KeyboardEvent) { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); void this.ask(); } }
  protected newChat() { this.current.set(null); this.bubbles.set([]); }

  private scroll() { setTimeout(() => { const el = this.scroller()?.nativeElement; el?.scrollTo({ top: el.scrollHeight, behavior: 'smooth' }); }, 30); }
}

function parse(data: string | null): Record<string, unknown> | null {
  if (!data) return null;
  try { return JSON.parse(data) as Record<string, unknown>; } catch { return null; }
}
