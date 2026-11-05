import { ChangeDetectionStrategy, Component, WritableSignal, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Company } from '@jo/ui/company';
import { Count } from '@jo/ui/count';
import { Empty } from '@jo/ui/empty';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Area, ChartRow, Columns, HBars } from '@jo/ui/charts';
import { Score } from '@jo/ui/score';
import { Tech } from '@jo/ui/tech';
import { Campaign, JobDetail, JobRow, Profile, send } from '../../core/api';
import { command } from '../../core/command';
import { ago, jobLabel, jobTone } from '../../core/format';
import { AGENT_ICON, Live } from '../../core/live';
import { query } from '../../core/query';
import { PLATFORMS } from '../../core/sources';

interface Tally { key: string; value: number }
interface JobStats { byStatus: Tally[]; bySource: Tally[]; scores: Tally[]; byModality: Tally[]; byContract: Tally[]; bySeniority: Tally[]; topStack: Tally[]; perDay: { day: string; value: number }[]; total: number }
interface Resume { id: string; language: string; mode: string; writtenBy: string | null; reviewedBy: string | null; reviewed: boolean; pages: number; createdAt: string; fileName: string; threadId: string }
interface Compose { ok: boolean; versionId: string | null; mode: string; reviewed: boolean; error: string | null; threadId: string | null }

const SOURCE_LOGO: Record<string, { file: string; mono: boolean }> = {
  greenhouse: { file: 'greenhouse', mono: false }, github: { file: 'github', mono: true }, hn: { file: 'hackernews', mono: false },
};
const PIPELINE = ['Seletor', 'Redator', 'Conferente', 'Revisor', 'Renderizador', 'Inspetor ATS'];
const LEVELS = ['Estágio', 'Júnior', 'Pleno', 'Sênior', 'Especialista', 'Liderança'];
/** Elegibilidade de quem mora no Brasil (especificação V2), na ordem do melhor para o pior. */
export const ELIGIBILITY: Record<string, { label: string; tone: 'ok' | 'warn' | 'bad' | '' }> = {
  BR_ELIGIBLE_CONFIRMED: { label: 'Aberta ao Brasil', tone: 'ok' }, BR_ELIGIBLE_POSSIBLE: { label: 'Remota, a confirmar', tone: '' },
  RELOCATION_WITH_SPONSORSHIP: { label: 'Visto ou mudança pagos', tone: 'ok' }, REVIEW: { label: 'Anúncio contraditório', tone: 'warn' },
  RELOCATION_UNVERIFIED: { label: 'No exterior, sem visto dito', tone: 'warn' }, UNKNOWN: { label: 'Sem local', tone: '' },
  BR_RESTRICTED: { label: 'Restrita a outro país', tone: 'bad' },
};
const ORDER = Object.keys(ELIGIBILITY);
const MODES: Record<string, string> = { Remoto: 'globe-hemisphere-west', 'Híbrido': 'arrows-left-right', Presencial: 'buildings' };
interface Facet { key: string; n: number }
type Sort = 'score' | 'new' | 'company';

/**
 * Vagas: tudo que as fontes trouxeram, com os números em gráficos isométricos, a logo da empresa, a stack com ícones
 * (o que você tem e o que falta), a nota explicada e o currículo da vaga gerado ali mesmo, com os agentes ao vivo.
 */
@Component({
  selector: 'app-jobs',
  imports: [Area, Badge, Columns, Company, Count, Empty, HBars, Icon, Iso, Score, Tech],
  templateUrl: './jobs.page.html',
  styleUrl: './jobs.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'open.set(null)' },
})
export class JobsPage {
  private readonly router = inject(Router);
  private readonly live = inject(Live);
  readonly status = input<string | undefined>();
  protected readonly search = signal('');
  protected readonly remoteOnly = signal(false);
  // Filtros da lista: tecnologias, fonte, modalidade, senioridade e contrato (vários de cada, qualquer um vale), mais os extras e a ordem.
  protected readonly techSel = signal<ReadonlySet<string>>(new Set());
  protected readonly sourceSel = signal<ReadonlySet<string>>(new Set());
  protected readonly modeSel = signal<ReadonlySet<string>>(new Set());
  protected readonly levelSel = signal<ReadonlySet<string>>(new Set());
  protected readonly contractSel = signal<ReadonlySet<string>>(new Set());
  protected readonly eligSel = signal<ReadonlySet<string>>(new Set());
  /** Padrão da especificação: vaga restrita a outro país sai da lista até você pedir. */
  protected readonly hideRestricted = signal(true);
  protected readonly eligibility = ELIGIBILITY;
  protected readonly withSalary = signal(false);
  protected readonly atsOnly = signal(false);
  protected readonly minScore = signal(0);
  protected readonly sort = signal<Sort>('score');
  protected readonly allTech = signal(false);
  protected readonly modes = MODES;
  protected readonly sorts: { key: Sort; label: string }[] = [{ key: 'score', label: 'Nota' }, { key: 'new', label: 'Mais novas' }, { key: 'company', label: 'Empresa' }];
  protected readonly open = signal<string | null>(null);
  protected readonly all = query<JobRow[]>(() => '/jobs', 8000);
  protected readonly stats = query<JobStats>(() => '/jobs/stats', 15000);
  protected readonly profile = query<Profile>(() => '/profile', 0);
  protected readonly campaigns = query<Campaign[]>(() => '/campaigns', 0);
  /** Crédito que a fonte exige (Remotive, Himalayas e outras do registro): aparece em cada vaga que veio dela. */
  private readonly registry = query<{ sources: { prefix: string; name: string; docsUrl: string; attribution: string | null }[] }>(() => '/sources/registry', 0);
  protected credit(source: string) {
    const prefix = source.split(':')[0];
    const p = this.registry.value()?.sources.find((x) => x.prefix === prefix && x.attribution);
    if (!p) return null;
    let site = p.docsUrl;
    try { site = `https://${new URL(p.docsUrl).hostname}`; } catch { /* mantém a documentação */ }
    return { name: p.name, text: p.attribution!, site };
  }
  /** Universal: as palavras-chave da campanha (de qualquer área) que aparecem no título ou na descrição da vaga aberta. */
  protected readonly matched = computed(() => {
    const d = this.detail.value();
    const words = this.campaigns.value()?.[0]?.technologies ?? [];
    if (!d || words.length === 0) return [];
    const text = ` ${fold(d.job.title)} ${fold(d.description ?? '')} `;
    return words.filter((w) => fold(w).length > 1 && text.includes(fold(w)));
  });
  protected readonly detail = query<JobDetail>(() => (this.open() ? `/jobs/${this.open()}` : null));
  protected readonly resumes = query<Resume[]>(() => (this.open() ? `/materials/resumes?applicationId=${this.open()}` : null), 0);
  protected readonly composing = command((id: string) => send<Compose>('POST', `/materials/preview/${id}`), 'Currículo pronto. O PDF está na lista desta vaga.');
  protected readonly label = jobLabel;
  protected readonly tone = jobTone;
  protected readonly ago = ago;
  protected readonly icons = AGENT_ICON;
  protected readonly pipeline = PIPELINE;
  protected readonly filters = [
    { key: undefined, label: 'Todas' }, { key: 'Shortlisted', label: 'Na fila' }, { key: 'Scored', label: 'Abaixo do corte' },
    { key: 'Rejected', label: 'Cortadas' }, { key: 'Duplicate', label: 'Duplicadas' }, { key: 'Expired', label: 'Encerradas' },
  ];

  /** O que você tem: skills e tecnologias das experiências do perfil. */
  private readonly mine = computed(() => new Set((this.profile.value()?.facts ?? []).filter((f) => f.status !== 'Rejected')
    .flatMap((f) => (f.kind === 'skill' ? [f.statement] : f.technologies)).map(fold)));
  protected has(tech: string) {
    const m = this.mine();
    if (m.size === 0) return null;
    const t = fold(tech);
    return m.has(t) || [...m].some((x) => x.startsWith(t) || t.startsWith(x));
  }

  protected readonly counts = computed(() => {
    const c: Record<string, number> = {};
    for (const j of this.all.value() ?? []) c[j.status] = (c[j.status] ?? 0) + 1;
    return c;
  });

  /** O que existe em cada filtro, com a contagem sobre todas as vagas (as mais comuns primeiro; senioridade na ordem da carreira). */
  protected readonly facets = computed(() => {
    const jobs = this.all.value() ?? [];
    const tally = (keys: (j: JobRow) => (string | null | undefined)[]): Facet[] => {
      const m = new Map<string, number>();
      for (const j of jobs) for (const k of new Set(keys(j))) if (k) m.set(k, (m.get(k) ?? 0) + 1);
      return [...m].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0], 'pt-BR')).map(([key, n]) => ({ key, n }));
    };
    const level = tally((j) => [j.seniority]);
    return {
      tech: tally((j) => j.stack), source: tally((j) => [j.source.split(':')[0]]), mode: tally((j) => [j.modality]),
      level: level.sort((a, b) => (LEVELS.indexOf(a.key) + 99) % 99 - (LEVELS.indexOf(b.key) + 99) % 99), contract: tally((j) => [j.contract]),
      elig: tally((j) => [j.eligibility]).sort((a, b) => ORDER.indexOf(a.key) - ORDER.indexOf(b.key)),
      salary: jobs.filter((j) => j.salaryText).length, ats: jobs.filter((j) => j.applyChannel === 'ats').length,
    };
  });
  protected readonly techShown = computed(() => this.allTech() ? this.facets().tech : this.facets().tech.slice(0, 14));

  protected readonly activeCount = computed(() =>
    [this.techSel(), this.sourceSel(), this.modeSel(), this.levelSel(), this.contractSel(), this.eligSel()].reduce((n, s) => n + s.size, 0)
    + (this.withSalary() ? 1 : 0) + (this.atsOnly() ? 1 : 0) + (this.minScore() > 0 ? 1 : 0) + (this.status() ? 1 : 0) + (this.search().trim() ? 1 : 0));

  protected readonly rows = computed(() => {
    const s = this.search().trim().toLowerCase();
    const any = (sel: ReadonlySet<string>, v: string | null | undefined) => sel.size === 0 || (!!v && sel.has(v));
    const list = (this.all.value() ?? []).filter((j) => (!this.status() || j.status === this.status())
      && (!this.remoteOnly() || j.modality === 'Remoto')
      && (this.techSel().size === 0 || j.stack.some((t) => this.techSel().has(t)))
      && any(this.sourceSel(), j.source.split(':')[0]) && any(this.modeSel(), j.modality) && any(this.levelSel(), j.seniority) && any(this.contractSel(), j.contract)
      && any(this.eligSel(), j.eligibility) && (!this.hideRestricted() || this.eligSel().has('BR_RESTRICTED') || j.eligibility !== 'BR_RESTRICTED')
      && (!this.withSalary() || !!j.salaryText) && (!this.atsOnly() || j.applyChannel === 'ats') && (this.minScore() === 0 || (j.score ?? 0) >= this.minScore())
      && (!s || `${j.company} ${j.title} ${j.stack.join(' ')}`.toLowerCase().includes(s)));
    const by: Record<Sort, (a: JobRow, b: JobRow) => number> = {
      score: (a, b) => (b.score ?? -1) - (a.score ?? -1), new: (a, b) => b.firstSeenAt.localeCompare(a.firstSeenAt), company: (a, b) => a.company.localeCompare(b.company, 'pt-BR'),
    };
    return list.sort(by[this.sort()]);
  });

  protected toggle(sel: WritableSignal<ReadonlySet<string>>, key: string) {
    transition(() => sel.update((s) => { const n = new Set(s); if (n.has(key)) n.delete(key); else n.add(key); return n; }));
  }
  protected clearFilters() {
    transition(() => {
      for (const sel of [this.techSel, this.sourceSel, this.modeSel, this.levelSel, this.contractSel, this.eligSel]) sel.set(new Set());
      this.hideRestricted.set(true);
      this.withSalary.set(false); this.atsOnly.set(false); this.minScore.set(0); this.search.set(''); this.remoteOnly.set(false);
      void this.router.navigate([], { queryParams: { status: null }, queryParamsHandling: 'merge' });
    });
  }
  protected platformName(key: string) { return PLATFORMS[key]?.name ?? key; }

  // Gráficos: funil, fontes, notas, novas por dia, stack e o perfil das vagas.
  protected readonly funnel = computed<ChartRow[]>(() => {
    const st = this.stats.value();
    if (!st) return [];
    const by = (k: string) => st.byStatus.find((x) => x.key === k)?.value ?? 0;
    const unique = st.total - by('Duplicate');
    const eligible = unique - by('Rejected') - by('Expired');
    return [
      { label: 'Encontradas', value: st.total, tone: 'muted' }, { label: 'Únicas', value: unique, tone: 'muted' }, { label: 'Elegíveis', value: eligible },
      { label: 'Na fila', value: by('Shortlisted'), tone: 'accent' },
    ];
  });
  protected readonly sources = computed<ChartRow[]>(() => (this.stats.value()?.bySource ?? []).map((s) => ({
    label: s.key === 'hn' ? 'Hacker News' : s.key.charAt(0).toUpperCase() + s.key.slice(1), value: s.value,
    logo: SOURCE_LOGO[s.key] ? `/logos/${SOURCE_LOGO[s.key].file}.svg` : null, mono: SOURCE_LOGO[s.key]?.mono,
  })));
  protected readonly scores = computed<ChartRow[]>(() => {
    const by = new Map((this.stats.value()?.scores ?? []).map((s) => [Number(s.key), s.value]));
    return Array.from({ length: 10 }, (_, i) => ({ label: String(i * 10), value: by.get(i * 10) ?? 0, tone: i >= 8 ? 'accent' as const : undefined, hint: `nota de ${i * 10} a ${i * 10 + 9}` }));
  });
  protected readonly days = computed<ChartRow[]>(() => (this.stats.value()?.perDay ?? []).map((d, i, all) => ({
    label: i === all.length - 1 ? 'hoje' : d.day.split('-').reverse().slice(0, 2).join('/'), value: d.value, hint: `${d.day.split('-').reverse().join('/')}: ${d.value} vagas`,
  })));
  protected readonly stack = computed(() => {
    const top = this.stats.value()?.topStack ?? [];
    const max = Math.max(1, ...top.map((t) => t.value));
    return top.map((t) => ({ ...t, pct: (t.value / max) * 100, have: this.has(t.key) }));
  });
  protected readonly shape = computed(() => {
    const st = this.stats.value();
    if (!st) return [];
    const part = (title: string, list: Tally[], names: Record<string, string>) => {
      const total = Math.max(1, list.reduce((a, b) => a + b.value, 0));
      return { title, parts: list.map((x, i) => ({ label: names[x.key] ?? x.key, value: x.value, pct: (x.value / total) * 100, i })) };
    };
    return [
      part('Modalidade', st.byModality, { unknown: 'Sem dado' }),
      part('Vínculo', st.byContract, { unknown: 'Sem dado' }),
      part('Senioridade', st.bySeniority, { unknown: 'Sem dado' }),
    ];
  });

  /** Os agentes trabalhando no currículo desta vaga, ao vivo (o fio tem o título da vaga no assunto). */
  protected readonly stations = computed(() => {
    const d = this.detail.value();
    if (!d) return [];
    const subject = `Currículo para ${d.job.title.slice(0, 90)}`;
    const feed = this.live.feed().filter((l) => l.kind === 'resume' && l.subject.startsWith(subject));
    const lastThread = feed.at(-1)?.threadId;
    const mine = feed.filter((l) => l.threadId === lastThread);
    return PIPELINE.map((agent) => {
      const said = mine.filter((l) => l.message.agent === agent || (l.message.to === agent && l.message.kind !== 'note'));
      const last = said.at(-1);
      const working = !!last && (last.message.kind === 'ask' || last.message.kind === 'working');
      return { agent, icon: AGENT_ICON[agent] ?? 'model', state: !last ? 'idle' : working ? 'working' : last.message.ok ? 'done' : 'bad', title: last?.message.title ?? '' };
    });
  });

  protected async compose(id: string) {
    const r = await this.composing.run(id);
    if (r) await this.resumes.reload();
  }

  protected filter(key: string | undefined) {
    transition(() => void this.router.navigate([], { queryParams: { status: key ?? null }, queryParamsHandling: 'merge' }));
  }
  protected toggleRemote() { transition(() => this.remoteOnly.update((v) => !v)); }

  protected count(key: string | undefined) { return key ? (this.counts()[key] ?? 0) : (this.all.value()?.length ?? 0); }
  protected source(s: string) { return s.replace(/^[a-z]+:/, '').replace(/^https?:\/\//, ''); }
  protected platform(s: string) { return SOURCE_LOGO[s.split(':')[0]] ?? null; }
  protected channel(c: string) { return ({ ats: 'Formulário do ATS', manual: 'Pelo site, com você', email: 'Por e-mail', link: 'Pelo link da vaga' } as Record<string, string>)[c] ?? c; }
  protected company(name: string) { return name.replace(/\s+-\s+linkedin$/i, ''); }
  protected modeLabel(m: string) { return ({ ai: 'escrito pela IA', local: 'rascunho local', plain: 'só os fatos, sem IA' } as Record<string, string>)[m] ?? m; }

  /** Descrição legível: parágrafos e listas a partir do texto puro que as fontes entregam. */
  protected readonly blocks = computed(() => {
    const text = (this.detail.value()?.description ?? '').replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/\r/g, '');
    const out: { kind: 'p' | 'h' | 'ul'; text?: string; items?: string[] }[] = [];
    for (const raw of text.split(/\n/)) {
      const line = raw.trim();
      if (!line) continue;
      const bullet = /^([-•*·▪●]|\d+[.)])\s+/.test(line);
      const clean = line.replace(/^([-•*·▪●]|\d+[.)])\s+/, '');
      if (bullet) {
        const last = out.at(-1);
        if (last?.kind === 'ul') last.items!.push(clean); else out.push({ kind: 'ul', items: [clean] });
      } else if (line.length < 60 && /[:?]$|^[A-ZÀ-Ý][^.!]{2,50}$/.test(line)) out.push({ kind: 'h', text: line.replace(/:$/, '') });
      else out.push({ kind: 'p', text: line });
    }
    return out;
  });
}

/** Troca de filtro com View Transitions: os cartões deslizam para o lugar novo em vez de pular. */
function transition(change: () => void) {
  const doc = document as Document & { startViewTransition?: (cb: () => void) => unknown };
  if (doc.startViewTransition && !matchMedia('(prefers-reduced-motion: reduce)').matches) doc.startViewTransition(change);
  else change();
}

function fold(s: string) { return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9#+]/g, ''); }
