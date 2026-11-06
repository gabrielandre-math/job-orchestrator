import { AfterViewInit, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Flow, FlowEdge, FlowNode, get } from '../../core/api';
import { AGENT_ICON, AgentMessage, AgentThread, Live, LiveMessage, PROVIDER_LOGO } from '../../core/live';
import { Conversation } from '../agents/conversation';
import { query } from '../../core/query';
import { clock } from '../../core/format';
import { Theme } from '../../core/theme';

type Route = 'h' | 'turn' | 'down' | 'down-right' | 'down-left' | 'bus';
type Point = [number, number];

/** Um nó posicionado: o bloco e o espaço do nome e do subtítulo embaixo dele. */
interface Placed extends FlowNode { x: number; y: number; size: number; icon: string; logo: string | null; logoColor: string; logoBrand: string; sub: string; lane: string; }
interface Drawn extends FlowEdge { id: string; d: string; lx: number; ly: number; text: string; tone: string; vertical: boolean; lane: string; }
interface Frame { id: string; title: string; icon: string; x: number; y: number; w: number; h: number; lane: string; }

const TILE = 104;
const SOURCE = 72;
const LABEL = 54;
const COL = 252;
const X0 = 300;
const ROW_A = 120;
const ROW_B = 430;
const ROW_C = 720;
const BUS_X = X0 - 64;
const ROW_D = ROW_C + 340;
const ROW_E = ROW_D + 250;
const ROW_F = ROW_E + 250;
const ROW_G = ROW_F + 250;
const STATION = 96;
const SCOL = 232;

/**
 * As estações dos agentes, uma esteira por trabalho: o currículo da vaga, a leitura do seu CV, a varredura da internet e o e-mail
 * para o recrutador. Quem usa modelo mostra qual.
 */
interface Station { id: string; agent: string; kind: 'resume' | 'profile' | 'crawl' | 'mail'; x: number; y: number; sub: string; llm: boolean; }
const STATIONS: Station[] = [
  ...([['Seletor', 'fatos da vaga'], ['Redator', 'escreve'], ['Conferente', 'fato a fato'], ['Revisor', 'outra família'], ['Renderizador', 'PDF'],
    ['Inspetor ATS', 'leitura de ATS'], ['Guardião', 'antes do envio']] as const)
    .map(([agent, sub], i) => ({ id: `resume-${i}`, agent, kind: 'resume' as const, x: X0 + i * SCOL, y: ROW_D, sub, llm: agent === 'Redator' || agent === 'Revisor' })),
  ...([['Leitor', 'tira dados pessoais'], ['Extrator', 'lê o currículo'], ['Conferente', 'trecho no documento']] as const)
    .map(([agent, sub], i) => ({ id: `profile-${i}`, agent, kind: 'profile' as const, x: X0 + i * SCOL, y: ROW_E, sub, llm: agent === 'Extrator' })),
  ...([['Semeador', 'sites da rodada'], ['Robots', 'robots.txt'], ['Coletor', 'Scrapy'], ['Extrator', 'vaga na página'], ['Conferente', 'trecho na página']] as const)
    .map(([agent, sub], i) => ({ id: `crawl-${i}`, agent, kind: 'crawl' as const, x: X0 + i * SCOL, y: ROW_F, sub, llm: agent === 'Extrator' })),
  ...([['Seletor', 'vaga com e-mail'], ['Redator', 'escreve o e-mail'], ['Conferente', 'fato a fato'], ['Guardião', 'limites do envio']] as const)
    .map(([agent, sub], i) => ({ id: `mail-${i}`, agent, kind: 'mail' as const, x: X0 + i * SCOL, y: ROW_G, sub, llm: agent === 'Redator' })),
];
/** O nome de cada esteira e a tela onde ela mora. */
export const BELTS: Record<Station['kind'], { title: string; path: string }> = {
  resume: { title: 'Currículo da vaga', path: '/vagas' }, profile: { title: 'Leitura do seu currículo', path: '/perfil' },
  crawl: { title: 'Varredura da internet', path: '/fontes/varredura' }, mail: { title: 'E-mail para o recrutador', path: '/email' },
};
interface StationView extends Station { icon: string; status: 'working' | 'spoke' | 'done' | 'idle'; bubble: string | null; provider: { file: string; mono: boolean } | null; model: string | null; thread: string | null; }
interface AiEdge { id: string; d: string; lx: number; ly: number; text: string; hot: boolean; }

/** Onde cada agente fica. A topologia é fixa: o Guardião sempre fica no caminho do envio. */
const POS: Record<string, Point> = {
  scout: [X0, ROW_A], normalizer: [X0 + COL, ROW_A], judge: [X0 + 2 * COL, ROW_A], analyst: [X0 + 3 * COL, ROW_A],
  writer: [X0, ROW_B], preparer: [X0 + COL, ROW_B], guard: [X0 + 2 * COL, ROW_B], sender: [X0 + 3 * COL, ROW_B], verifier: [X0 + 4 * COL, ROW_B],
  reviewer: [X0, ROW_C], attention: [X0 + 2 * COL, ROW_C], tracker: [X0 + 4 * COL, ROW_C],
};
/** Estação isométrica de cada agente (ui/lib/iso-art.ts). */
const ISO: Record<string, string> = {
  scout: 'radar', normalizer: 'identity', judge: 'gate', analyst: 'chart', writer: 'writer', reviewer: 'reviewer',
  preparer: 'form', guard: 'guardian', sender: 'send', verifier: 'receipt', tracker: 'inbox', attention: 'queue',
};
const ICONS: Record<string, string> = {
  scout: 'binoculars', normalizer: 'fingerprint', judge: 'gavel', analyst: 'chart-line-up', writer: 'pen-nib', reviewer: 'seal-check',
  preparer: 'clipboard-text', guard: 'shield-check', sender: 'paper-plane-right', verifier: 'receipt', tracker: 'envelope-simple-open', attention: 'hand-palm',
};
const SUB: Record<string, string> = {
  scout: 'descoberta', normalizer: 'identidade', judge: 'filtros', analyst: 'nota', writer: 'currículo', reviewer: 'checagem de fatos',
  preparer: 'formulário', guard: 'política de envio', sender: 'envio', verifier: 'confirmação', tracker: 'e-mail e funil', attention: 'fila humana',
};
/** Só marcas com logo oficial no Simple Icons; as outras usam ícone neutro e o nome. */
const LOGOS: Record<string, string> = { greenhouse: 'greenhouse', github: 'github', hackernews: 'hackernews' };
/** Cor da logo sobre o bloco escuro: a da marca, ou clara quando a marca é preta. */
const LOGO_COLOR: Record<string, string> = { greenhouse: '#3fd19e', github: '#f2f2f2', hackernews: '#ff7a3d' };
/** No tema claro, a cor oficial da marca. */
const LOGO_BRAND: Record<string, string> = { greenhouse: '#24A47F', github: '#181717', hackernews: '#F0652F' };
/** Etapa de cada agente: define a cor neon do bloco, da moldura e das arestas que saem dele. */
const LANE: Record<string, string> = {
  scout: 'discovery', normalizer: 'discovery', judge: 'discovery', analyst: 'discovery',
  writer: 'application', reviewer: 'application', preparer: 'application', guard: 'application', sender: 'application', verifier: 'application',
  tracker: 'application', attention: 'you',
};
const ROUTES: Record<string, Route> = {
  'scout-normalizer': 'h', 'normalizer-judge': 'h', 'judge-analyst': 'h', 'analyst-writer': 'turn', 'writer-preparer': 'h',
  'preparer-guard': 'h', 'guard-sender': 'h', 'sender-verifier': 'h', 'writer-reviewer': 'down', 'verifier-tracker': 'down',
  'preparer-attention': 'down-right', 'verifier-attention': 'down-left', 'guard-attention': 'down',
};
const TEXT: Record<string, (n: number) => string> = {
  'scout-normalizer': (n) => `${n} vagas`, 'normalizer-judge': (n) => `${n} únicas`, 'judge-analyst': (n) => `${n} elegíveis`,
  'analyst-writer': (n) => `${n} na fila`, 'writer-preparer': (n) => `${n} currículos`, 'preparer-guard': (n) => `${n} formulários`,
  'guard-sender': (n) => `${n} liberados`, 'sender-verifier': (n) => `${n} envios`, 'preparer-attention': (n) => `${n} pendências`,
  'verifier-attention': (n) => `${n} incertas`, 'guard-attention': (n) => `${n} aprovações`, 'writer-reviewer': () => 'fase 3', 'verifier-tracker': () => 'fase 4',
};

/** O que cada agente faz e como roda nesta fase. O que é planejado aparece como planejado. */
const ABOUT: Record<string, { does: string; runs: string; link?: { label: string; path: string; params?: Record<string, string> } }> = {
  scout: { does: 'Busca vagas nas fontes permitidas e guarda uma cópia do que veio.', runs: 'Quadros oficiais (Gupy, Greenhouse, Lever, Ashby e outros), APIs públicas e o coletor, sem IA.', link: { label: 'Ver vagas', path: '/vagas' } },
  normalizer: { does: 'Calcula a identidade canônica e marca como duplicada a mesma vaga vinda de outra fonte.', runs: 'Regra determinística.', link: { label: 'Ver duplicadas', path: '/vagas', params: { status: 'Duplicate' } } },
  judge: { does: 'Aplica os filtros eliminatórios antes da nota: golpe, local, país, palavras essenciais e vaga encerrada.', runs: 'Regra determinística.', link: { label: 'Ver cortadas', path: '/vagas', params: { status: 'Rejected' } } },
  analyst: { does: 'Dá a nota de 0 a 100 com fatores e explicação (palavras, cargo-alvo, senioridade, modalidade). Acima do corte, a vaga entra na fila.', runs: 'Pesos explícitos, sem IA.', link: { label: 'Ver fila', path: '/vagas', params: { status: 'Shortlisted' } } },
  writer: { does: 'Gera o currículo em PDF só com fatos do perfil, reordenados e escritos para a vaga.', runs: 'IA da sua conta, Conferente fato a fato e Inspetor ATS.', link: { label: 'Ver vagas', path: '/vagas' } },
  reviewer: { does: 'Confere se cada linha do currículo tem fonte no perfil, com um modelo de outra família.', runs: 'Modelo de outra família, nunca o mesmo que escreveu.' },
  preparer: { does: 'Preenche o formulário por rótulo e confere o que ficou. Nunca envia.', runs: 'Playwright com contexto isolado e roteador com fallback.', link: { label: 'Ver candidaturas', path: '/candidaturas' } },
  guard: { does: 'Decide se pode enviar: modo da campanha, autorização ativa, aprovação do formulário exato, teto do dia, uma empresa por dia e pendências.', runs: 'Regra determinística, sem IA.', link: { label: 'Abrir revisão', path: '/revisao' } },
  sender: { does: 'Clica em enviar só com o token do Guardião, depois de gravar a intenção.', runs: 'Playwright, um envio por vez.' },
  verifier: { does: 'Exige página de confirmação ou recibo. Envio incerto vai para reconciliação, só lendo.', runs: 'Leitura da página e do portal do ATS.', link: { label: 'Ver confirmadas', path: '/candidaturas', params: { state: 'SubmittedConfirmed' } } },
  tracker: { does: 'Lê a caixa (IMAP) só no que é da busca, liga a resposta ao envio e para o lembrete quando a empresa responde.', runs: 'Classificação no modelo local; o texto não sai da máquina.', link: { label: 'Abrir e-mail', path: '/email' } },
  attention: { does: 'Tudo que só você resolve: aprovações, CAPTCHA, login, perguntas novas e envios incertos.', runs: 'Fila humana.', link: { label: 'Abrir atenção', path: '/atencao' } },
};

@Component({
  selector: 'app-flow',
  imports: [RouterLink, Badge, Icon, Iso, Conversation],
  templateUrl: './flow.page.html',
  styleUrl: './flow.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlowPage implements AfterViewInit {
  private readonly destroy = inject(DestroyRef);
  protected readonly theme = inject(Theme);
  private readonly viewport = viewChild.required<ElementRef<HTMLDivElement>>('viewport');

  protected readonly live = signal(true);
  protected readonly flow = query<Flow>(() => '/flow', 3000);
  protected readonly selected = signal<string | null>(null);
  protected readonly tx = signal(40);
  protected readonly ty = signal(24);
  protected readonly k = signal(0.8);
  protected readonly size = signal({ w: 1200, h: 700 });
  protected readonly clock = clock;
  protected readonly liveFeed = inject(Live);
  protected readonly STATION = STATION;
  protected readonly ISO = ISO;
  protected readonly icons = AGENT_ICON;
  protected readonly logos = PROVIDER_LOGO;
  protected readonly feedOpen = signal(true);
  protected readonly talking = signal<{ thread: string | null; focus: string | null } | null>(null);
  private readonly baseline = signal<LiveMessage[]>([]);
  private readonly now = signal(Date.now());
  private moved = false;
  private fitted = false;
  private drag: { x: number; y: number } | null = null;

  protected readonly nodes = computed<Placed[]>(() => {
    const f = this.flow.value();
    if (!f) return [];
    const sources = f.nodes.filter((n) => n.id.startsWith('source-'));
    return f.nodes.map((n) => {
      if (n.id.startsWith('source-')) {
        const platform = n.id.slice('source-'.length);
        const i = sources.indexOf(n);
        return { ...n, x: 60, y: ROW_A - 4 + i * (SOURCE + LABEL + 10), size: SOURCE, icon: 'globe', logo: LOGOS[platform] ?? null, logoColor: LOGO_COLOR[platform] ?? '', logoBrand: LOGO_BRAND[platform] ?? '', sub: n.role, lane: 'discovery' };
      }
      const [x, y] = POS[n.id] ?? [X0 + 4 * COL, ROW_C];
      return { ...n, x, y, size: TILE, icon: ICONS[n.id] ?? 'globe', logo: null, logoColor: '', logoBrand: '', sub: SUB[n.id] ?? n.role.toLowerCase(), lane: LANE[n.id] ?? 'application' };
    });
  });

  protected readonly edges = computed<Drawn[]>(() => {
    const byId = new Map(this.nodes().map((n) => [n.id, n]));
    return (this.flow.value()?.edges ?? []).flatMap((e) => {
      const a = byId.get(e.from);
      const b = byId.get(e.to);
      if (!a || !b) return [];
      const key = `${e.from}-${e.to}`;
      const route: Route = e.from.startsWith('source-') ? 'bus' : (ROUTES[key] ?? 'h');
      const points = path(route, a, b);
      const [lx, ly, vertical] = labelAt(route, points);
      const tone = e.kind === 'planned' ? 'planned' : e.kind === 'fallback' ? (e.count > 0 ? 'fallback' : 'idle') : e.count > 0 ? 'flow' : 'idle';
      const text = route === 'bus' ? '' : (TEXT[key]?.(e.count) ?? String(e.count));
      return [{ ...e, id: key, d: rounded(points, 12), lx, ly, text, tone, vertical, lane: a.lane }];
    });
  });

  /** O último fio de cada esteira mais o que chega ao vivo: cada estação mostra o que fez por último. */
  protected readonly stations = computed<StationView[]>(() => {
    const feed = [...this.baseline(), ...this.liveFeed.feed()];
    const now = this.now();
    return STATIONS.map((st) => {
      const mine = feed.filter((l) => l.kind === st.kind && (l.message.agent === st.agent || (l.message.to === st.agent && (l.message.kind === 'working' || l.message.kind === 'answer' || l.message.kind === 'fail'))));
      const last = mine.at(-1);
      const age = last ? now - new Date(last.message.at).getTime() : Infinity;
      const pending = last && (last.message.kind === 'working' || (last.message.kind === 'ask' && last.message.agent === st.agent)) && age < 10 * 60_000;
      const said = [...mine].reverse().find((l) => l.message.agent === st.agent || l.message.kind === 'answer');
      const answered = [...mine].reverse().find((l) => (l.message.kind === 'answer' || l.message.kind === 'working') && l.message.providerId);
      const status = pending ? 'working' : age < 9000 ? 'spoke' : last ? 'done' : 'idle';
      return {
        ...st, icon: AGENT_ICON[st.agent] ?? 'model', status, thread: last?.threadId ?? null,
        bubble: status === 'idle' || status === 'done' ? null : (said?.message.title ?? null),
        provider: st.llm && answered?.message.providerId ? PROVIDER_LOGO[answered.message.providerId] ?? null : null,
        model: st.llm && answered?.message.model ? `${answered.message.providerId?.startsWith('claude') ? 'Claude' : answered.message.providerId === 'codex' ? 'ChatGPT' : 'Llama'} · ${answered.message.model === 'padrão do CLI' ? 'Codex' : answered.message.model}` : null,
      };
    });
  });

  /** Arestas da esteira: o rótulo é a última passagem de uma estação para a próxima; acende por dez segundos. */
  protected readonly aiEdges = computed<AiEdge[]>(() => {
    const feed = [...this.baseline(), ...this.liveFeed.feed()];
    const now = this.now();
    const views = this.stations();
    const out: AiEdge[] = [];
    for (let i = 0; i < views.length - 1; i++) {
      const a = views[i], b = views[i + 1];
      if (a.kind !== b.kind) continue;
      const hand = [...feed].reverse().find((l) => l.kind === a.kind && l.message.agent === a.agent && l.message.to === b.agent);
      const y = a.y + STATION / 2;
      const x1 = a.x + STATION, x2 = b.x - 3;
      const text = hand ? short(hand.message.title, 20) : '';
      out.push({ id: `${a.id}-${b.id}`, d: `M ${x1} ${y} L ${x2} ${y}`, lx: (x1 + x2) / 2, ly: y, text, hot: !!hand && now - new Date(hand.message.at).getTime() < 10_000 });
    }
    return out;
  });

  /** O painel ao vivo: as últimas mensagens de todas as esteiras, a mais nova embaixo. */
  protected readonly ticker = computed(() => {
    const feed = [...this.baseline(), ...this.liveFeed.feed()];
    const seen = new Set<string>();
    return feed.filter((l) => !seen.has(l.message.id) && seen.add(l.message.id)).slice(-9);
  });

  /** Pacotes de luz nas arestas de cima: quando um número aumenta entre duas leituras, um pacote corre por aquela aresta. */
  private previous = new Map<string, number>();
  /** Quando cada aresta recebeu algo por último: só ela pulsa, por 30 s. Pulsar todas o tempo todo pesava a página. */
  private readonly bumped = new Map<string, number>();
  protected fresh(id: string) { return this.now() - (this.bumped.get(id) ?? 0) < 30_000; }
  protected readonly flowPackets = signal<{ id: string; d: string; lane: string }[]>([]);

  /** Pacotes de luz: cada passagem de uma estação para a próxima nos últimos 1,4 s corre pela aresta. */
  protected readonly packets = computed(() => {
    const now = this.now();
    const edges = new Map(this.aiEdges().map((e) => [e.id, e]));
    const views = this.stations();
    return this.liveFeed.feed().filter((l) => l.message.to && now - new Date(l.message.at).getTime() < 1400).flatMap((l) => {
      const i = views.findIndex((v) => v.kind === l.kind && v.agent === l.message.agent);
      const next = views[i + 1];
      const edge = i >= 0 && next && next.kind === l.kind && next.agent === l.message.to ? edges.get(`${views[i].id}-${next.id}`) : undefined;
      return edge ? [{ id: l.message.id, d: edge.d }] : [];
    });
  });

  protected ago(iso: string) {
    const s = Math.max(0, Math.round((this.now() - new Date(iso).getTime()) / 1000));
    return s < 60 ? `${s} s` : s < 3600 ? `${Math.round(s / 60)} min` : new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' });
  }
  protected isPending(l: LiveMessage) {
    const t = this.ticker();
    const i = t.indexOf(l);
    return l.message.kind === 'working' && !t.slice(i + 1).some((n) => n.threadId === l.threadId && n.message.providerId === l.message.providerId && (n.message.kind === 'answer' || n.message.kind === 'fail'));
  }

  protected readonly aiFrame = computed<Frame>(() => {
    const xs = STATIONS.map((s) => s.x), ys = STATIONS.map((s) => s.y);
    const x = Math.min(...xs) - 30, y = Math.min(...ys) - 64;
    return { id: 'ai', title: 'Agentes de IA', icon: 'cpu', x, y, w: Math.max(...xs) + STATION + 30 - x, h: Math.max(...ys) + STATION + LABEL + 24 - y, lane: 'ai' };
  });

  protected readonly frames = computed<Frame[]>(() => {
    const n = this.nodes();
    const box = (id: string, title: string, icon: string, members: Placed[], lane: string): Frame[] => {
      if (members.length === 0) return [];
      const x = Math.min(...members.map((m) => m.x)) - 30;
      const y = Math.min(...members.map((m) => m.y)) - 54;
      const r = Math.max(...members.map((m) => m.x + m.size)) + 30;
      const b = Math.max(...members.map((m) => m.y + m.size + LABEL)) + 14;
      return [{ id, title, icon, x, y, w: r - x, h: b - y, lane }];
    };
    const pick = (ids: string[]) => n.filter((p) => ids.includes(p.id));
    return [
      ...box('sources', 'Fontes', 'globe', n.filter((p) => p.id.startsWith('source-')), 'discovery'),
      ...box('discovery', 'Descoberta', 'magnifying-glass', pick(['scout', 'normalizer', 'judge', 'analyst']), 'discovery'),
      ...box('application', 'Candidatura', 'files', pick(['writer', 'preparer', 'guard', 'sender', 'verifier']), 'application'),
      ...box('you', 'Com você', 'user', pick(['attention']), 'you'),
      ...(n.length > 0 ? [this.aiFrame()] : []),
    ];
  });

  protected readonly bounds = computed(() => {
    const items = [
      ...this.frames().map((f) => ({ x: f.x, y: f.y, r: f.x + f.w, b: f.y + f.h })),
      ...this.nodes().map((p) => ({ x: p.x, y: p.y, r: p.x + p.size, b: p.y + p.size + LABEL })),
    ];
    if (this.nodes().length === 0) return { x: 0, y: 0, w: 1, h: 1 };
    const x = Math.min(...items.map((i) => i.x)) - 30;
    const y = Math.min(...items.map((i) => i.y)) - 30;
    return { x, y, w: Math.max(...items.map((i) => i.r)) + 30 - x, h: Math.max(...items.map((i) => i.b)) + 30 - y };
  });

  protected readonly current = computed(() => this.nodes().find((n) => n.id === this.selected()) ?? null);
  protected readonly about = computed(() => {
    const id = this.selected();
    if (!id) return null;
    return ABOUT[id] ?? { does: 'Fonte de vagas cadastrada na campanha.', runs: 'API pública de vagas, sem IA.', link: { label: 'Ver vagas', path: '/vagas' } };
  });

  protected readonly transform = computed(() => `translate(${this.tx()}px, ${this.ty()}px) scale(${this.k()})`);
  protected readonly grid = computed(() => ({ size: `${40 * this.k()}px ${40 * this.k()}px`, pos: `${this.tx()}px ${this.ty()}px` }));

  /** Minimapa: escala para caber em 168 x 100 e o retângulo do que está visível. */
  protected readonly mini = computed(() => {
    const b = this.bounds();
    const s = Math.min(168 / b.w, 100 / b.h);
    const v = this.size();
    return {
      s, w: b.w * s, h: b.h * s,
      nodes: [...this.nodes().map((n) => ({ id: n.id, status: n.status, x: (n.x - b.x) * s, y: (n.y - b.y) * s, w: n.size * s })),
        ...this.stations().map((n) => ({ id: n.id, status: n.status === 'working' ? 'attention' : n.status === 'idle' ? 'idle' : 'active', x: (n.x - b.x) * s, y: (n.y - b.y) * s, w: STATION * s }))],
      view: { x: (-this.tx() / this.k() - b.x) * s, y: (-this.ty() / this.k() - b.y) * s, w: (v.w / this.k()) * s, h: (v.h / this.k()) * s },
    };
  });

  constructor() {
    effect(() => {
      const edges = this.edges();
      const fresh: { id: string; d: string; lane: string }[] = [];
      for (const e of edges) {
        const before = this.previous.get(e.id);
        if (before !== undefined && e.count > before && e.tone === 'flow') {
          fresh.push({ id: `${e.id}-${e.count}-${Date.now()}`, d: e.d, lane: e.lane });
          this.bumped.set(e.id, Date.now());
        }
        this.previous.set(e.id, e.count);
      }
      if (fresh.length > 0) {
        this.flowPackets.update((p) => [...p, ...fresh]);
        setTimeout(() => this.flowPackets.update((p) => p.filter((x) => !fresh.includes(x))), 1600);
      }
    });
    const tick = setInterval(() => this.now.set(Date.now()), 500);
    this.destroy.onDestroy(() => clearInterval(tick));
    void this.loadBaseline();
    effect(() => {
      if (!this.fitted && this.nodes().length > 0) {
        this.fitted = true;
        queueMicrotask(() => this.fit());
      }
    });
  }

  ngAfterViewInit() {
    const observer = new ResizeObserver(([entry]) => {
      this.size.set({ w: entry.contentRect.width, h: entry.contentRect.height });
      if (!this.moved && this.nodes().length > 0) this.fit();
    });
    observer.observe(this.viewport().nativeElement);
    this.destroy.onDestroy(() => observer.disconnect());
  }

  protected fit() {
    const b = this.bounds();
    const v = this.size();
    const k = clamp(Math.min(v.w / b.w, v.h / b.h), 0.3, 1);
    this.k.set(k);
    this.tx.set((v.w - b.w * k) / 2 - b.x * k);
    this.ty.set((v.h - b.h * k) / 2 - b.y * k);
  }

  protected zoom(factor: number, px = this.size().w / 2, py = this.size().h / 2) {
    this.moved = true;
    const k = this.k();
    const next = clamp(k * factor, 0.3, 1.8);
    this.tx.set(px - ((px - this.tx()) * next) / k);
    this.ty.set(py - ((py - this.ty()) * next) / k);
    this.k.set(next);
  }

  /** Rolagem dá zoom: para quem usa o PC por área de trabalho remota (Moonlight), onde a pinça do trackpad não chega. */
  protected readonly wheelZooms = signal(read('jo.canvas.wheelZooms') === '1');
  protected toggleWheel() {
    this.wheelZooms.update((v) => !v);
    write('jo.canvas.wheelZooms', this.wheelZooms() ? '1' : '0');
  }

  protected wheel(e: WheelEvent) {
    e.preventDefault();
    this.moved = true;
    cancelAnimationFrame(this.frame);
    const r = this.viewport().nativeElement.getBoundingClientRect();
    // Como no n8n e no Figma: roda rola, pinça (Ctrl + roda no navegador), Ctrl ou Option + roda dão zoom, Shift + roda rola de lado.
    // Com "Rolar dá zoom" ligado, a roda amplia e Shift + roda move.
    const zoom = e.ctrlKey || e.metaKey || e.altKey || (this.wheelZooms() && !e.shiftKey);
    if (zoom) {
      // Pinça manda deltas pequenos e contínuos; roda de mouse manda degraus de 100. Os dois viram um zoom suave.
      const delta = e.deltaMode === 1 ? e.deltaY * 33 : e.deltaY;
      this.zoom(Math.exp(-clamp(delta, -120, 120) * (e.ctrlKey && !this.wheelZooms() ? 0.006 : 0.0035)), e.clientX - r.left, e.clientY - r.top);
    } else if (e.shiftKey) this.tx.update((x) => x - (e.deltaY || e.deltaX));
    else { this.tx.update((x) => x - e.deltaX); this.ty.update((y) => y - e.deltaY); }
  }

  // Gestos: um dedo (ou o mouse) arrasta com inércia; dois dedos fazem pinça e arrastam juntos; toque duplo aproxima;
  // toque com dois dedos afasta. Trackpad: pinça chega como Ctrl + roda (wheel).
  private readonly pointers = new Map<number, { x: number; y: number }>();
  private pinch: { dist: number; k: number; wx: number; wy: number } | null = null;
  private gesture: { t: number; x: number; y: number; travel: number; fingers: number } | null = null;
  private lastTap = { t: 0, x: 0, y: 0 };
  private velocity = { x: 0, y: 0, t: 0 };
  private frame = 0;

  private local(x: number, y: number) {
    const r = this.viewport().nativeElement.getBoundingClientRect();
    return { x: x - r.left, y: y - r.top };
  }

  protected down(e: PointerEvent) {
    const target = e.target as HTMLElement;
    if (target.closest('.mini, .controls, .drawer, .feed, app-conversation')) return;
    const onNode = !!target.closest('.node');
    cancelAnimationFrame(this.frame);
    this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
    const now = performance.now();
    if (this.pointers.size === 1) {
      // Em cima de um nó, o toque rápido abre o nó; só vira arrasto depois de andar alguns pixels.
      this.nodeTouch = onNode ? e.pointerId : null;
      if (!onNode) { this.capture(e.pointerId); this.drag = { x: e.clientX, y: e.clientY }; this.moved = true; }
      this.gesture = { t: now, x: e.clientX, y: e.clientY, travel: 0, fingers: 1 };
      this.velocity = { x: 0, y: 0, t: now };
    } else if (this.pointers.size === 2) {
      for (const id of this.pointers.keys()) this.capture(id);
      this.nodeTouch = null;
      this.moved = true;
      const [a, b] = [...this.pointers.values()];
      const mid = this.local((a.x + b.x) / 2, (a.y + b.y) / 2);
      this.pinch = { dist: Math.max(1, Math.hypot(b.x - a.x, b.y - a.y)), k: this.k(), wx: (mid.x - this.tx()) / this.k(), wy: (mid.y - this.ty()) / this.k() };
      this.drag = null;
      if (this.gesture && now - this.gesture.t < 200) this.gesture.fingers = 2;
    }
  }

  private nodeTouch: number | null = null;
  private capture(id: number) {
    try { this.viewport().nativeElement.setPointerCapture(id); } catch { /* ponteiro já saiu */ }
  }

  protected move(e: PointerEvent) {
    const prev = this.pointers.get(e.pointerId);
    if (!prev) return;
    // Mouse sem botão apertado no meio de um arrasto: o "soltar" se perdeu (comum em área de trabalho remota). O arrasto acaba aqui.
    if (e.pointerType === 'mouse' && e.buttons === 0) { this.up(e); return; }
    this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
    if (this.gesture) this.gesture.travel += Math.hypot(e.clientX - prev.x, e.clientY - prev.y);
    if (this.pinch && this.pointers.size >= 2) {
      const [a, b] = [...this.pointers.values()];
      const mid = this.local((a.x + b.x) / 2, (a.y + b.y) / 2);
      const k = clamp(this.pinch.k * (Math.hypot(b.x - a.x, b.y - a.y) / this.pinch.dist), 0.3, 1.8);
      // O ponto do mundo que estava entre os dedos continua entre os dedos: pinça e arrasto ao mesmo tempo.
      this.k.set(k);
      this.tx.set(mid.x - this.pinch.wx * k);
      this.ty.set(mid.y - this.pinch.wy * k);
      return;
    }
    if (this.nodeTouch === e.pointerId && this.gesture && this.gesture.travel > 8) {
      this.nodeTouch = null;
      this.capture(e.pointerId);
      this.drag = { x: e.clientX, y: e.clientY };
      this.moved = true;
    }
    if (!this.drag) return;
    const dx = e.clientX - prev.x, dy = e.clientY - prev.y;
    this.tx.update((x) => x + dx);
    this.ty.update((y) => y + dy);
    const t = performance.now();
    const dt = Math.max(8, t - this.velocity.t);
    this.velocity = { x: 0.7 * (dx / dt) + 0.3 * this.velocity.x, y: 0.7 * (dy / dt) + 0.3 * this.velocity.y, t };
  }

  protected up(e?: PointerEvent) {
    if (e) this.pointers.delete(e.pointerId);
    else this.pointers.clear();
    if (this.pointers.size < 2) this.pinch = null;
    if (this.pointers.size === 1) {
      // Saiu um dedo da pinça: o que ficou continua arrastando.
      const [p] = [...this.pointers.values()];
      this.drag = { x: p.x, y: p.y };
      this.velocity = { x: 0, y: 0, t: performance.now() };
      return;
    }
    if (this.pointers.size > 0) return;
    this.drag = null;
    const g = this.gesture;
    this.gesture = null;
    const tappedNode = this.nodeTouch !== null;
    this.nodeTouch = null;
    if (!g || !e || (tappedNode && g.fingers === 1)) return;
    const now = performance.now();
    const quick = now - g.t < 260 && g.travel < 12;
    const at = this.local(e.clientX, e.clientY);
    if (quick && g.fingers === 2) { this.animateZoom(1 / 1.6, at.x, at.y); return; }
    if (quick) {
      const double = now - this.lastTap.t < 320 && Math.hypot(e.clientX - this.lastTap.x, e.clientY - this.lastTap.y) < 36;
      this.lastTap = double ? { t: 0, x: 0, y: 0 } : { t: now, x: e.clientX, y: e.clientY };
      if (double) {
        if (e.shiftKey || e.altKey) this.animateZoom(1 / 1.6, at.x, at.y);
        else if (this.k() >= 1.5) this.animateFit();
        else this.animateZoom(1.6, at.x, at.y);
      }
      return;
    }
    this.glide();
  }

  /** Inércia: o canvas continua na velocidade do arrasto e desacelera até parar. */
  private glide() {
    let { x: vx, y: vy } = this.velocity;
    if (Math.hypot(vx, vy) < 0.25 || now() - this.velocity.t > 80) return;
    let last = now();
    const step = () => {
      const t = now();
      const dt = Math.min(32, t - last);
      last = t;
      this.tx.update((x) => x + vx * dt);
      this.ty.update((y) => y + vy * dt);
      const decay = Math.pow(0.94, dt / 16);
      vx *= decay;
      vy *= decay;
      if (Math.hypot(vx, vy) > 0.02) this.frame = requestAnimationFrame(step);
    };
    this.frame = requestAnimationFrame(step);
  }

  /** Zoom e enquadramento animados (220 ms, saída suave). Com movimento reduzido no sistema, vai direto. */
  private animateTo(k: number, tx: number, ty: number) {
    cancelAnimationFrame(this.frame);
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) { this.k.set(k); this.tx.set(tx); this.ty.set(ty); return; }
    const from = { k: this.k(), tx: this.tx(), ty: this.ty() };
    const start = now();
    const step = () => {
      const p = Math.min(1, (now() - start) / 220);
      const ease = 1 - Math.pow(1 - p, 3);
      this.k.set(from.k + (k - from.k) * ease);
      this.tx.set(from.tx + (tx - from.tx) * ease);
      this.ty.set(from.ty + (ty - from.ty) * ease);
      if (p < 1) this.frame = requestAnimationFrame(step);
    };
    this.frame = requestAnimationFrame(step);
  }

  protected animateZoom(factor: number, px = this.size().w / 2, py = this.size().h / 2) {
    this.moved = true;
    const k = this.k();
    const next = clamp(k * factor, 0.3, 1.8);
    this.animateTo(next, px - ((px - this.tx()) * next) / k, py - ((py - this.ty()) * next) / k);
  }

  protected animateFit() {
    const b = this.bounds();
    const v = this.size();
    const k = clamp(Math.min(v.w / b.w, v.h / b.h), 0.3, 1);
    this.animateTo(k, (v.w - b.w * k) / 2 - b.x * k, (v.h - b.h * k) / 2 - b.y * k);
  }

  /** Minimapa: arrastar o retângulo leva a vista junto. */
  private miniDrag = false;
  protected miniDown(e: PointerEvent) {
    this.miniDrag = true;
    (e.currentTarget as Element).setPointerCapture(e.pointerId);
    this.jump(e);
  }
  protected miniMove(e: PointerEvent) { if (this.miniDrag) this.jump(e); }
  protected miniUp() { this.miniDrag = false; }

  protected key(e: KeyboardEvent) {
    const pan = e.shiftKey ? 240 : 80;
    if (e.key === '+' || e.key === '=') this.animateZoom(1.25);
    else if (e.key === '-') this.animateZoom(1 / 1.25);
    else if (e.key === '0') this.animateFit();
    else if (e.key === '1') this.animateZoom(1 / this.k());
    else if (e.key.startsWith('Arrow')) {
      e.preventDefault();
      this.moved = true;
      const d = { ArrowLeft: [pan, 0], ArrowRight: [-pan, 0], ArrowUp: [0, pan], ArrowDown: [0, -pan] }[e.key] ?? [0, 0];
      this.animateTo(this.k(), this.tx() + d[0], this.ty() + d[1]);
    }
    else if (e.key === 'Escape') { this.selected.set(null); this.talking.set(null); }
  }

  protected jump(e: MouseEvent | PointerEvent) {
    const m = this.mini();
    const b = this.bounds();
    const r = (e.currentTarget as SVGElement).getBoundingClientRect();
    this.moved = true;
    this.tx.set(this.size().w / 2 - ((e.clientX - r.left) / m.s + b.x) * this.k());
    this.ty.set(this.size().h / 2 - ((e.clientY - r.top) / m.s + b.y) * this.k());
  }

  /** Rótulo de cada esteira, à esquerda da primeira estação, com o atalho para a tela dela. */
  protected readonly belts = (Object.keys(BELTS) as Station['kind'][]).map((k) => {
    const first = STATIONS.find((st) => st.kind === k)!;
    return { kind: k, ...BELTS[k], x: first.x - 4, y: first.y - 34 };
  });

  protected toggle(id: string) { this.selected.update((s) => (s === id ? null : id)); }

  /** O último fio de cada esteira, para as estações mostrarem o que fizeram antes desta tela abrir. */
  private async loadBaseline() {
    try {
      const threads = await get<AgentThread[]>('/agents/threads?limit=40');
      const picks = ['resume', 'profile', 'crawl', 'mail'].map((k) => threads.find((t) => t.kind === k)).filter((t): t is AgentThread => !!t);
      const lists = await Promise.all(picks.map(async (t) => (await get<AgentMessage[]>(`/agents/threads/${t.id}`)).map((m) => ({ threadId: t.id, kind: t.kind, subject: t.subject, message: m }))));
      this.baseline.set(lists.flat());
    } catch { /* sem conversas ainda */ }
  }

  protected talk(s?: StationView) { this.talking.set({ thread: s?.thread ?? null, focus: s?.agent ?? null }); }
  protected statusLabel(s: string) { return ({ active: 'Com itens', idle: 'Parado', attention: 'Precisa de você', planned: 'Planejado' } as Record<string, string>)[s] ?? s; }
  protected statusTone(s: string) { return ({ active: 'ok', idle: 'neutral', attention: 'warn', planned: 'neutral' } as Record<string, string>)[s] ?? 'neutral'; }
}

function clamp(v: number, min: number, max: number) { return Math.min(max, Math.max(min, v)); }
function now() { return performance.now(); }
function read(key: string) { try { return localStorage.getItem(key); } catch { return null; } }
function write(key: string, value: string) { try { localStorage.setItem(key, value); } catch { /* armazenamento bloqueado */ } }
function short(s: string, n: number) { return s.length > n ? s.slice(0, n - 1).trimEnd() + '…' : s; }

/** Pontos de uma aresta em ângulo reto. "Embaixo" sai abaixo do nome e do subtítulo, para não cortar o texto. */
function path(route: Route, a: Placed, b: Placed): Point[] {
  const right: Point = [a.x + a.size, a.y + a.size / 2];
  const below: Point = [a.x + a.size / 2, a.y + a.size + LABEL + 4];
  const left: Point = [b.x - 3, b.y + b.size / 2];
  const top: Point = [b.x + b.size / 2, b.y - 3];
  switch (route) {
    case 'h': return [right, left];
    case 'down': return [below, top];
    // Troca de raia: desce, corre pelo corredor e entra pela esquerda do destino, sem cortar o título da moldura.
    case 'turn': { const cy = (below[1] + b.y - 60) / 2; return [below, [below[0], cy], [b.x - 42, cy], [b.x - 42, left[1]], left]; }
    case 'down-right': return [below, [below[0], b.y + b.size / 2], left];
    // Sai deslocado do centro para não correr junto da linha do nó planejado logo abaixo.
    case 'down-left': { const x = below[0] - 26; return [[x, below[1]], [x, b.y + b.size / 2], [b.x + b.size + 3, b.y + b.size / 2]]; }
    case 'bus': return [right, [BUS_X, right[1]], [BUS_X, left[1]], left];
  }
}

/** Onde vai o rótulo: no meio do maior trecho horizontal (acima da linha) ou ao lado do trecho vertical. */
function labelAt(route: Route, p: Point[]): [number, number, boolean] {
  if (route === 'down') return [p[0][0] + 10, (p[0][1] + p[1][1]) / 2, true];
  let best = 0;
  let at: [number, number, boolean] = [p[0][0], p[0][1], false];
  for (let i = 0; i < p.length - 1; i++) {
    const [x1, y1] = p[i];
    const [x2, y2] = p[i + 1];
    if (y1 === y2 && Math.abs(x2 - x1) > best) { best = Math.abs(x2 - x1); at = [(x1 + x2) / 2, y1, false]; }
  }
  return at;
}

/** Linha poligonal com cantos arredondados, como num diagrama de arquitetura. */
function rounded(p: Point[], r: number): string {
  let d = `M ${p[0][0]} ${p[0][1]}`;
  for (let i = 1; i < p.length - 1; i++) {
    const [px, py] = p[i - 1];
    const [cx, cy] = p[i];
    const [nx, ny] = p[i + 1];
    const r1 = Math.min(r, Math.hypot(cx - px, cy - py) / 2, Math.hypot(nx - cx, ny - cy) / 2);
    const [ax, ay] = toward(cx, cy, px, py, r1);
    const [bx, by] = toward(cx, cy, nx, ny, r1);
    d += ` L ${ax} ${ay} Q ${cx} ${cy} ${bx} ${by}`;
  }
  const [lx, ly] = p[p.length - 1];
  return `${d} L ${lx} ${ly}`;
}

function toward(x: number, y: number, tx: number, ty: number, dist: number): Point {
  const len = Math.hypot(tx - x, ty - y) || 1;
  return [x + ((tx - x) / len) * dist, y + ((ty - y) / len) * dist];
}
