import { AfterViewInit, ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Flow, FlowEdge, FlowNode } from '../../core/api';
import { query } from '../../core/query';
import { clock } from '../../core/format';

/** Posição de um nó no canvas, em coordenadas do mundo. */
interface Placed extends FlowNode { x: number; y: number; w: number; h: number; lane: number; }
interface Drawn extends FlowEdge { id: string; d: string; mx: number; my: number; }

const W = 216;
const H = 88;
const SOURCE_H = 60;
const STEP_X = 276;
const LANE_Y = [56, 296, 536];
const LANES = ['Descoberta', 'Candidatura', 'Envio e acompanhamento'];

/** Onde cada agente fica: [raia, coluna]. A topologia é fixa: o Guardião sempre fica no caminho do envio. */
const SLOTS: Record<string, [number, number]> = {
  scout: [0, 1], normalizer: [0, 2], judge: [0, 3], analyst: [0, 4],
  writer: [1, 0], reviewer: [1, 1], preparer: [1, 2], guard: [1, 3],
  sender: [2, 0], verifier: [2, 1], tracker: [2, 2], attention: [2, 3],
};

/** O que cada agente faz e como roda nesta fase. Texto fixo e honesto: o que é planejado aparece como planejado. */
const ABOUT: Record<string, { does: string; runs: string; link?: { label: string; path: string; params?: Record<string, string> } }> = {
  scout: { does: 'Busca vagas nas fontes permitidas e guarda uma cópia do que veio.', runs: 'APIs públicas no formato Greenhouse, sem IA.', link: { label: 'Ver vagas', path: '/vagas' } },
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
  imports: [RouterLink, Badge],
  templateUrl: './flow.page.html',
  styleUrl: './flow.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FlowPage implements AfterViewInit {
  private readonly destroy = inject(DestroyRef);
  private readonly viewport = viewChild.required<ElementRef<HTMLDivElement>>('viewport');

  protected readonly live = signal(true);
  protected readonly flow = query<Flow>(() => '/flow', 3000);
  protected readonly selected = signal<string | null>(null);
  protected readonly tx = signal(40);
  protected readonly ty = signal(24);
  protected readonly k = signal(0.9);
  protected readonly size = signal({ w: 1200, h: 700 });
  protected readonly lanes = LANES.map((label, i) => ({ label, y: LANE_Y[i] - 34 }));
  protected readonly clock = clock;
  private fitted = false;
  private moved = false;
  private drag: { x: number; y: number } | null = null;

  protected readonly nodes = computed<Placed[]>(() => {
    const f = this.flow.value();
    if (!f) return [];
    const sources = f.nodes.filter((n) => n.id.startsWith('source-'));
    const stack = sources.length * SOURCE_H + Math.max(0, sources.length - 1) * 12;
    const top = LANE_Y[0] + H / 2 - stack / 2;
    return f.nodes.map((n) => {
      if (n.id.startsWith('source-')) {
        const i = sources.indexOf(n);
        const parts = n.label.split('/');
        return { ...n, label: parts.at(-1) || n.label, role: parts[0] || n.role, x: 0, y: top + i * (SOURCE_H + 12), w: W, h: SOURCE_H, lane: 0 };
      }
      const [lane, col] = SLOTS[n.id] ?? [2, 4];
      return { ...n, x: col * STEP_X, y: LANE_Y[lane], w: W, h: H, lane };
    });
  });

  protected readonly edges = computed<Drawn[]>(() => {
    const byId = new Map(this.nodes().map((n) => [n.id, n]));
    return (this.flow.value()?.edges ?? []).flatMap((e) => {
      const a = byId.get(e.from);
      const b = byId.get(e.to);
      return a && b ? [route(e, a, b)] : [];
    });
  });

  protected readonly bounds = computed(() => {
    const n = this.nodes();
    if (n.length === 0) return { x: 0, y: 0, w: 1, h: 1 };
    const x = Math.min(...n.map((p) => p.x)) - 40;
    const y = Math.min(...n.map((p) => p.y)) - 60;
    return { x, y, w: Math.max(...n.map((p) => p.x + p.w)) + 40 - x, h: Math.max(...n.map((p) => p.y + p.h)) + 40 - y };
  });

  protected readonly current = computed(() => this.nodes().find((n) => n.id === this.selected()) ?? null);
  protected readonly about = computed(() => {
    const id = this.selected();
    if (!id) return null;
    return ABOUT[id] ?? { does: 'Fonte de vagas cadastrada na campanha.', runs: 'API pública no formato Greenhouse, sem IA.', link: { label: 'Ver vagas', path: '/vagas' } };
  });

  protected readonly transform = computed(() => `translate(${this.tx()}px, ${this.ty()}px) scale(${this.k()})`);
  protected readonly grid = computed(() => ({ size: `${22 * this.k()}px ${22 * this.k()}px`, pos: `${this.tx()}px ${this.ty()}px` }));

  /** Minimapa: escala para caber em 200 x 120 e o retângulo do que está visível. */
  protected readonly mini = computed(() => {
    const b = this.bounds();
    const s = Math.min(168 / b.w, 100 / b.h);
    const v = this.size();
    return {
      s, w: b.w * s, h: b.h * s,
      nodes: this.nodes().map((n) => ({ id: n.id, status: n.status, x: (n.x - b.x) * s, y: (n.y - b.y) * s, w: n.w * s, h: n.h * s })),
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
    const el = this.viewport().nativeElement;
    const observer = new ResizeObserver(([entry]) => {
      this.size.set({ w: entry.contentRect.width, h: entry.contentRect.height });
      if (!this.moved && this.nodes().length > 0) this.fit();
    });
    observer.observe(el);
    this.destroy.onDestroy(() => observer.disconnect());
  }

  protected fit() {
    const b = this.bounds();
    const v = this.size();
    const k = clamp(Math.min(v.w / b.w, v.h / b.h), 0.35, 1.05);
    this.k.set(k);
    this.tx.set((v.w - b.w * k) / 2 - b.x * k);
    this.ty.set((v.h - b.h * k) / 2 - b.y * k);
  }

  protected zoom(factor: number, px = this.size().w / 2, py = this.size().h / 2) {
    this.moved = true;
    const k = this.k();
    const next = clamp(k * factor, 0.35, 1.8);
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

  /** Clique no minimapa centraliza a vista naquele ponto. */
  protected jump(e: MouseEvent) {
    const m = this.mini();
    const b = this.bounds();
    const r = (e.currentTarget as SVGElement).getBoundingClientRect();
    const wx = (e.clientX - r.left) / m.s + b.x;
    const wy = (e.clientY - r.top) / m.s + b.y;
    this.tx.set(this.size().w / 2 - wx * this.k());
    this.ty.set(this.size().h / 2 - wy * this.k());
  }

  protected toggle(id: string) { this.selected.update((s) => (s === id ? null : id)); }
  protected statusLabel(s: string) { return { active: 'Com itens', idle: 'Parado', attention: 'Precisa de você', planned: 'Planejado' }[s] ?? s; }
  protected statusTone(s: string) { return { active: 'ok', idle: 'neutral', attention: 'warn', planned: 'neutral' }[s] ?? 'neutral'; }
}

function clamp(v: number, min: number, max: number) { return Math.min(max, Math.max(min, v)); }

/** Desenha a aresta: mesma raia em curva suave (arco quando pula um nó); troca de raia em S, de baixo para cima. */
function route(e: FlowEdge, a: Placed, b: Placed): Drawn {
  const id = `${e.from}-${e.to}`;
  let p0: [number, number], p1: [number, number], p2: [number, number], p3: [number, number];
  if (a.lane === b.lane && b.x > a.x) {
    p0 = [a.x + a.w, a.y + a.h / 2];
    p3 = [b.x, b.y + b.h / 2];
    const skip = b.x - a.x > STEP_X * 1.5;
    const lift = skip ? (e.kind === 'fallback' ? 86 : -86) : 0;
    const dx = (p3[0] - p0[0]) / 2;
    p1 = [p0[0] + dx, p0[1] + lift];
    p2 = [p3[0] - dx, p3[1] + lift];
  } else {
    p0 = [a.x + a.w / 2, a.y + a.h];
    p3 = [b.x + b.w / 2, b.y];
    const dy = Math.max(60, (p3[1] - p0[1]) / 2);
    p1 = [p0[0], p0[1] + dy];
    p2 = [p3[0], p3[1] - dy];
  }
  const mx = (p0[0] + 3 * p1[0] + 3 * p2[0] + p3[0]) / 8;
  const my = (p0[1] + 3 * p1[1] + 3 * p2[1] + p3[1]) / 8;
  return { ...e, id, d: `M ${p0[0]} ${p0[1]} C ${p1[0]} ${p1[1]}, ${p2[0]} ${p2[1]}, ${p3[0]} ${p3[1]}`, mx, my };
}
