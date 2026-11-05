import { AfterViewInit, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Icon } from '@jo/ui/icon';
import { Flow, FlowEdge, FlowNode } from '../../core/api';
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

/** Onde cada agente fica. A topologia é fixa: o Guardião sempre fica no caminho do envio. */
const POS: Record<string, Point> = {
  scout: [X0, ROW_A], normalizer: [X0 + COL, ROW_A], judge: [X0 + 2 * COL, ROW_A], analyst: [X0 + 3 * COL, ROW_A],
  writer: [X0, ROW_B], preparer: [X0 + COL, ROW_B], guard: [X0 + 2 * COL, ROW_B], sender: [X0 + 3 * COL, ROW_B], verifier: [X0 + 4 * COL, ROW_B],
  reviewer: [X0, ROW_C], attention: [X0 + 2 * COL, ROW_C], tracker: [X0 + 4 * COL, ROW_C],
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
  scout: { does: 'Busca vagas nas fontes permitidas e guarda uma cópia do que veio.', runs: 'APIs públicas de vagas e do GitHub, sem IA.', link: { label: 'Ver vagas', path: '/vagas' } },
  normalizer: { does: 'Calcula a identidade canônica e marca como duplicada a mesma vaga vinda de outra fonte.', runs: 'Regra determinística.', link: { label: 'Ver duplicadas', path: '/vagas', params: { status: 'Duplicate' } } },
  judge: { does: 'Aplica os filtros eliminatórios antes da nota: local, país, stack essencial e vaga encerrada.', runs: 'Regra determinística.', link: { label: 'Ver cortadas', path: '/vagas', params: { status: 'Rejected' } } },
  analyst: { does: 'Dá a nota de 0 a 100 com fatores e explicação. Acima do corte, a vaga entra na fila.', runs: 'Pesos explícitos, sem IA nesta fase.', link: { label: 'Ver fila', path: '/vagas', params: { status: 'Shortlisted' } } },
  writer: { does: 'Gera o currículo em PDF só com fatos do perfil, reordenados para a vaga.', runs: 'Template e Chromium. Claude entra na fase 3.' },
  reviewer: { does: 'Confere se cada linha do currículo tem fonte no perfil, com um modelo de outra família.', runs: 'Planejado para a fase 3.' },
  preparer: { does: 'Preenche o formulário por rótulo e confere o que ficou. Nunca envia.', runs: 'Playwright com contexto isolado e roteador com fallback.', link: { label: 'Ver candidaturas', path: '/candidaturas' } },
  guard: { does: 'Decide se pode enviar: modo da campanha, aprovação do formulário exato, limite diário e pendências.', runs: 'Regra determinística, sem IA.', link: { label: 'Abrir revisão', path: '/revisao' } },
  sender: { does: 'Clica em enviar só com o token do Guardião, depois de gravar a intenção.', runs: 'Playwright, um envio por vez.' },
  verifier: { does: 'Exige página de confirmação ou recibo. Envio incerto vai para reconciliação, só lendo.', runs: 'Leitura da página e do portal do ATS.', link: { label: 'Ver confirmadas', path: '/candidaturas', params: { state: 'SubmittedConfirmed' } } },
  tracker: { does: 'Lê os e-mails de ATS e move o funil, inclusive candidaturas feitas por você.', runs: 'Planejado para a fase 4.' },
  attention: { does: 'Tudo que só você resolve: aprovações, CAPTCHA, login, perguntas novas e envios incertos.', runs: 'Fila humana.', link: { label: 'Abrir atenção', path: '/atencao' } },
};

@Component({
  selector: 'app-flow',
  imports: [RouterLink, Badge, Icon],
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
    ];
  });

  protected readonly bounds = computed(() => {
    const items = [
      ...this.frames().map((f) => ({ x: f.x, y: f.y, r: f.x + f.w, b: f.y + f.h })),
      ...this.nodes().map((p) => ({ x: p.x, y: p.y, r: p.x + p.size, b: p.y + p.size + LABEL })),
    ];
    if (items.length === 0) return { x: 0, y: 0, w: 1, h: 1 };
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
      nodes: this.nodes().map((n) => ({ id: n.id, status: n.status, x: (n.x - b.x) * s, y: (n.y - b.y) * s, w: n.size * s })),
      view: { x: (-this.tx() / this.k() - b.x) * s, y: (-this.ty() / this.k() - b.y) * s, w: (v.w / this.k()) * s, h: (v.h / this.k()) * s },
    };
  });

  constructor() {
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

  protected wheel(e: WheelEvent) {
    e.preventDefault();
    this.moved = true;
    const r = this.viewport().nativeElement.getBoundingClientRect();
    // Como no n8n e no Figma: roda rola, Ctrl + roda (ou pinça) dá zoom, Shift + roda rola de lado.
    if (e.ctrlKey || e.metaKey) this.zoom(Math.exp(-e.deltaY * 0.004), e.clientX - r.left, e.clientY - r.top);
    else if (e.shiftKey) this.tx.update((x) => x - e.deltaY);
    else { this.tx.update((x) => x - e.deltaX); this.ty.update((y) => y - e.deltaY); }
  }

  protected down(e: PointerEvent) {
    if ((e.target as HTMLElement).closest('.node, .mini, .controls, .drawer')) return;
    this.drag = { x: e.clientX, y: e.clientY };
    this.moved = true;
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  }

  protected move(e: PointerEvent) {
    if (!this.drag) return;
    this.tx.update((x) => x + e.clientX - this.drag!.x);
    this.ty.update((y) => y + e.clientY - this.drag!.y);
    this.drag = { x: e.clientX, y: e.clientY };
  }

  protected up() { this.drag = null; }

  protected key(e: KeyboardEvent) {
    if (e.key === '+' || e.key === '=') this.zoom(1.2);
    else if (e.key === '-') this.zoom(1 / 1.2);
    else if (e.key === '0') this.fit();
    else if (e.key === 'Escape') this.selected.set(null);
  }

  protected jump(e: MouseEvent) {
    const m = this.mini();
    const b = this.bounds();
    const r = (e.currentTarget as SVGElement).getBoundingClientRect();
    this.moved = true;
    this.tx.set(this.size().w / 2 - ((e.clientX - r.left) / m.s + b.x) * this.k());
    this.ty.set(this.size().h / 2 - ((e.clientY - r.top) / m.s + b.y) * this.k());
  }

  protected toggle(id: string) { this.selected.update((s) => (s === id ? null : id)); }
  protected statusLabel(s: string) { return ({ active: 'Com itens', idle: 'Parado', attention: 'Precisa de você', planned: 'Planejado' } as Record<string, string>)[s] ?? s; }
  protected statusTone(s: string) { return ({ active: 'ok', idle: 'neutral', attention: 'warn', planned: 'neutral' } as Record<string, string>)[s] ?? 'neutral'; }
}

function clamp(v: number, min: number, max: number) { return Math.min(max, Math.max(min, v)); }

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
