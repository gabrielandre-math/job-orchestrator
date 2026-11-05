import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ChartRow, Columns } from '@jo/ui/charts';
import { Company } from '@jo/ui/company';
import { Count } from '@jo/ui/count';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { get } from '../../core/api';
import { ago } from '../../core/format';
import { markdown } from '../../core/markdown';
import { query } from '../../core/query';
import { AGENT_ICON } from '../../core/live';
import { SearchRun } from '../../core/search';

type StationState = 'idle' | 'working' | 'done';
interface Station { key: string; iso: string; label: string; value: number | null; sub: string; state: StationState }

/**
 * Busca ao vivo: a rodada da campanha desenhada em tempo real. Cinco estações isométricas (fontes, área, sem repetidas, nota, fila)
 * ligadas por esteiras que só correm quando há trabalho passando, as fontes uma a uma, a narração em frases e o resultado.
 */
@Component({
  selector: 'app-search',
  imports: [RouterLink, Columns, Company, Count, Icon, Iso],
  templateUrl: './search.page.html',
  styleUrl: './search.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchPage {
  protected readonly run = inject(SearchRun);
  protected readonly s = this.run.state;
  protected readonly icons = AGENT_ICON;
  protected readonly ago = ago;

  protected readonly reports = query<{ name: string; at: string; summary: string }[]>(() => '/night/reports', 60000);
  protected readonly opened = signal<{ name: string; html: string } | null>(null);
  protected async openReport(name: string) {
    if (this.opened()?.name === name) { this.opened.set(null); return; }
    const r = await get<{ name: string; markdown: string }>(`/night/reports/${name}`);
    this.opened.set({ name, html: markdown(r.markdown.split('\n').slice(1).join('\n')) });
  }
  protected reportDate(name: string) {
    const [y, m, d, hm] = name.split('-');
    return `${d}/${m}/${y} às ${hm?.slice(0, 2)}h${hm?.slice(2)}`;
  }

  constructor() { void this.run.load(); }

  protected readonly stations = computed<Station[]>(() => {
    const s = this.s();
    const running = !!s?.running;
    const sourcesDone = !!s && s.sources.length > 0 && s.sources.every((x) => x.state === 'done' || x.state === 'failed');
    const st = (done: boolean, working: boolean): StationState => (done ? 'done' : working && running ? 'working' : 'idle');
    const n = s?.sources.length ?? 0;
    const finished = s?.sources.filter((x) => x.state === 'done' || x.state === 'failed').length ?? 0;
    return [
      { key: 'sources', iso: 'radar', label: 'Fontes', value: s ? finished : null, sub: s ? `${finished} de ${n} consultadas` : 'APIs, quadros de ATS e sites', state: st(!!s && (s.fresh !== null || !running), !!s) },
      { key: 'area', iso: 'reader', label: 'Da sua área', value: s ? s.kept : null, sub: s ? `de ${s.listed} vagas listadas` : 'o que combina com a campanha', state: st(!!s && (s.fresh !== null || !running), !!s && s.kept > 0) },
      { key: 'fresh', iso: 'checker', label: 'Sem repetidas', value: s?.fresh ?? null, sub: s?.fresh !== null && s ? `novas; ${s.again} já conhecidas, ${s.duplicates} repetidas` : 'a mesma vaga em duas fontes conta uma vez', state: st(s?.fresh !== null && !!s, sourcesDone) },
      { key: 'score', iso: 'chart', label: 'Nota', value: s?.above ?? null, sub: s?.above !== null && s ? `com ${s.cut} ou mais, de ${s.scored} avaliadas` : 'quanto a vaga combina com você', state: st(s?.scored !== null && !!s, s?.fresh !== null && !!s) },
      { key: 'queue', iso: 'queue', label: 'Na fila', value: s && !s.running ? s.shortlisted : null, sub: s && !s.running ? 'novas para currículo e candidatura' : 'vão para o currículo sob medida', state: st(!!s && !s.running && s.stage === 'done', s?.scored !== null && !!s) },
    ];
  });

  /** A esteira entre duas estações corre quando a da esquerda já entrega e a da direita ainda trabalha. */
  protected flowing(i: number) {
    const a = this.stations()[i], b = this.stations()[i + 1];
    return !!this.s()?.running && a.state !== 'idle' && b.state !== 'done';
  }

  protected readonly bands = computed<ChartRow[]>(() => {
    const s = this.s();
    if (!s?.bands) return [];
    return s.bands.map((v, i) => ({ label: String(i * 10), value: v, tone: i * 10 >= s.cut ? 'accent' as const : undefined, hint: `nota de ${i * 10} a ${i * 10 + 9}: ${v}` }));
  });
  /** Narração sem eco: o "consultando" some quando a mesma fonte já respondeu. */
  protected readonly narration = computed(() => {
    const all = this.s()?.messages ?? [];
    const answered = new Set(all.map((m) => parse(m.data)).filter((d) => d?.['stage'] === 'source' && d['state'] !== 'working').map((d) => String(d!['source'])));
    return all.filter((m) => { const d = parse(m.data); return !(d?.['stage'] === 'source' && d['state'] === 'working' && answered.has(String(d['source']))); }).reverse().slice(0, 40);
  });
  protected readonly elapsed = computed(() => {
    const s = this.s();
    if (!s?.startedAt) return '';
    const end = s.running ? Date.now() : new Date(s.lastAt).getTime();
    const sec = Math.max(0, Math.round((end - new Date(s.startedAt).getTime()) / 1000));
    return sec >= 60 ? `${Math.floor(sec / 60)} min ${sec % 60} s` : `${sec} s`;
  });

  protected readonly Math = Math;
  protected time(at: string) { return new Date(at).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' }); }
  protected sourceText(x: { state: string; listed: number; kept: number; error: string | null; seconds: number | null }) {
    switch (x.state) {
      case 'working': return 'consultando…';
      case 'done': return `${x.listed} ${x.listed === 1 ? 'vaga' : 'vagas'}, ${x.kept} da sua área${x.seconds !== null ? ` · ${x.seconds.toLocaleString('pt-BR')} s` : ''}`;
      case 'failed': return 'não respondeu nesta rodada';
      default: return 'na fila';
    }
  }
}

function parse(data: string | null | undefined): Record<string, unknown> | null {
  if (!data) return null;
  try { return JSON.parse(data) as Record<string, unknown>; } catch { return null; }
}
