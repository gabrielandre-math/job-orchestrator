import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Company } from '@jo/ui/company';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { Campaign, CrawlFound, JobRow, ScraperStatus, send } from '../../core/api';
import { command } from '../../core/command';
import { AGENT_ICON, Live } from '../../core/live';
import { query } from '../../core/query';
import { Wizard } from '../../shared/wizard';

/** APIs públicas que a varredura pode ligar, com o que cada uma pede de chave na fonte. */
const APIS = [
  { key: 'remotive', name: 'Remotive', domain: 'remotive.com', hint: 'remoto, software', source: 'remotive:.net' },
  { key: 'arbeitnow', name: 'Arbeitnow', domain: 'arbeitnow.com', hint: 'Europa e remoto', source: 'arbeitnow:.net' },
  { key: 'himalayas', name: 'Himalayas', domain: 'himalayas.app', hint: 'remoto no mundo', source: 'himalayas:.net' },
  { key: 'jobicy', name: 'Jobicy', domain: 'jobicy.com', hint: 'remoto, por tag', source: 'jobicy:dotnet' },
  { key: 'themuse', name: 'The Muse', domain: 'themuse.com', hint: 'EUA e remoto', source: 'themuse:Software Engineering' },
  { key: 'remoteok', name: 'RemoteOK', domain: 'remoteok.com', hint: 'remoto, por tag', source: 'remoteok:dotnet' },
  { key: 'wwr', name: 'We Work Remotely', domain: 'weworkremotely.com', hint: 'back-end remoto', source: 'wwr:remote-back-end-programming-jobs' },
  { key: 'github', name: 'GitHub backend-br', domain: 'github.com', hint: 'vagas brasileiras', source: 'github:backend-br/vagas' },
  { key: 'hn', name: 'Hacker News', domain: 'news.ycombinator.com', hint: 'Who is hiring', source: 'hn:whoishiring' },
];
const DEPTHS = [
  { key: 'light', label: 'Leve', pages: 20, seconds: 90, hint: '20 páginas por rodada, em 1 min e meio' },
  { key: 'normal', label: 'Normal', pages: 60, seconds: 240, hint: '60 páginas, até 4 min' },
  { key: 'deep', label: 'Profundo', pages: 150, seconds: 600, hint: '150 páginas, até 10 min' },
];
const ASSISTS = [
  { key: 'none', label: 'Sem IA', hint: 'Só vagas com dado estruturado (JSON-LD) e quadros de ATS.' },
  { key: 'Local', label: 'Llama local, rápido', hint: 'De graça, na sua placa de vídeo. Recomendado.' },
  { key: 'LocalStrong', label: 'Llama local, forte', hint: 'Mais preciso em página bagunçada, mais lento.' },
  { key: 'Light', label: 'Nuvem, degrau leve', hint: 'Usa a sua ordem de gasto (Claude, ChatGPT). Gasta cota.' },
];
const STATIONS = ['Semeador', 'Robots', 'Coletor', 'Extrator', 'Conferente'];

/**
 * Varredura da internet: o coletor (Scrapy por baixo dos panos) com assistente de configuração em cinco passos e teste ao vivo.
 * A varredura entra na campanha como fonte web:site e roda junto das outras.
 */
@Component({
  selector: 'app-sweep',
  imports: [RouterLink, Badge, Company, Icon, Iso, PageHero, Wizard],
  templateUrl: './sweep.page.html',
  styleUrl: './sweep.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SweepPage {
  private readonly live = inject(Live);
  protected readonly status = query<ScraperStatus>(() => '/scraper/status', 4000);
  protected readonly campaigns = query<Campaign[]>(() => '/campaigns', 0);
  protected readonly jobs = query<JobRow[]>(() => '/jobs', 0);
  protected readonly install = command((_: void) => send('POST', '/scraper/install'), 'Instalando o coletor. Leva um minuto.');
  protected readonly saveSettings = command((b: object) => send('PUT', '/scraper/settings', b));
  protected readonly saveCampaign = command((x: { id: string; body: object }) => send('PUT', `/campaigns/${x.id}`, x.body), 'Varredura salva na campanha.');
  protected readonly testing = command((seeds: string) => send<{ found: number; postings: CrawlFound[] }>('POST', '/scraper/test', { seeds }));
  protected readonly apis = APIS;
  protected readonly depths = DEPTHS;
  protected readonly assists = ASSISTS;
  protected readonly icons = AGENT_ICON;

  protected readonly wizard = signal(false);
  protected readonly step = signal(1);
  protected readonly sites = signal('');
  protected readonly picked = signal<Set<string>>(new Set());
  protected readonly depth = signal('normal');
  protected readonly assist = signal('Local');
  protected readonly emails = signal(true);
  protected readonly seed = signal('');
  protected readonly result = signal<{ found: number; postings: CrawlFound[] } | null>(null);

  protected readonly campaign = computed(() => this.campaigns.value()?.[0] ?? null);
  protected readonly webSources = computed(() => (this.campaign()?.sources ?? []).filter((s) => s.startsWith('web:')));
  /** Sugestões: domínios de empresas das vagas que já chegaram e ainda não estão na varredura. */
  protected readonly suggestions = computed(() => {
    const have = new Set(this.webSources().flatMap((s) => s.slice(4).split(',')));
    const domains = (this.jobs.value() ?? []).map((j) => j.companyDomain).filter((d): d is string => !!d && !have.has(d));
    return [...new Set(domains)].slice(0, 12);
  });

  protected readonly stats = computed<HeroStat[]>(() => {
    const c = this.campaign();
    const sources = c?.sources ?? [];
    return [
      { label: 'Sites na varredura', value: this.webSources().reduce((n, s) => n + s.slice(4).split(',').length, 0), tone: 'accent' },
      { label: 'APIs ligadas', value: sources.filter((s) => APIS.some((a) => s.startsWith(a.key + ':'))).length },
      { label: 'Páginas por rodada', value: this.status.value()?.maxPages ?? 0 },
      { label: 'Cache local (MB)', value: Math.round(this.status.value()?.cacheMb ?? 0) },
    ];
  });

  /** A esteira do teste, ao vivo: a última fala de cada estação do fio "crawl" mais recente. */
  protected readonly stations = computed(() => {
    const feed = this.live.feed().filter((l) => l.kind === 'crawl');
    const thread = feed.at(-1)?.threadId;
    const mine = feed.filter((l) => l.threadId === thread);
    return STATIONS.map((agent) => {
      const last = mine.filter((l) => l.message.agent === agent || (l.message.to === agent && l.message.kind !== 'note')).at(-1);
      const working = !!last && (last.message.kind === 'ask' || last.message.kind === 'working');
      return { agent, icon: AGENT_ICON[agent] ?? (agent === 'Coletor' ? 'radar' : agent === 'Semeador' ? 'selector' : 'model'), title: last?.message.title ?? '', state: !last ? 'idle' : working ? 'working' : last.message.ok ? 'done' : 'bad' };
    });
  });

  protected open() {
    const c = this.campaign();
    const sources = c?.sources ?? [];
    this.sites.set(this.webSources().map((s) => s.slice(4)).join('\n'));
    this.picked.set(new Set(APIS.filter((a) => sources.some((s) => s.startsWith(a.key + ':'))).map((a) => a.key)));
    const st = this.status.value();
    this.depth.set(DEPTHS.find((d) => d.pages === st?.maxPages)?.key ?? 'normal');
    this.assist.set(st && (st.maxAiPages ?? 8) === 0 ? 'none' : st?.assist ?? 'Local');
    this.emails.set(st?.collectEmails ?? true);
    this.seed.set(this.siteList()[0] ?? this.suggestions()[0] ?? '');
    this.result.set(null);
    this.step.set(1);
    this.wizard.set(true);
  }

  protected readonly onApis = computed(() => APIS.filter((a) => (this.campaign()?.sources ?? []).some((s) => s.startsWith(a.key + ':'))));

  protected minutes(s: number) { return s >= 60 ? `${Math.round(s / 6) / 10} min` : `${s} s`; }
  protected assistLabel(st: ScraperStatus) { return st.maxAiPages === 0 ? 'sem IA' : (ASSISTS.find((a) => a.key === st.assist)?.label ?? 'Llama local, rápido'); }
  protected host(url: string) { try { return new URL(url).hostname.replace(/^www\./, ''); } catch { return null; } }
  protected explain(agent: string) {
    return ({ Semeador: 'escolhe os sites da rodada', Robots: 'confere o robots.txt', Coletor: 'visita as páginas', Extrator: 'tira as vagas da página', Conferente: 'confere cada vaga no texto' } as Record<string, string>)[agent] ?? '';
  }

  protected siteList() { return this.sites().split(/[\n,]/).map((s) => s.trim().replace(/^https?:\/\//, '').replace(/\/$/, '')).filter((s) => s.length > 3); }
  protected addSuggestion(d: string) { this.sites.update((s) => (s.trim() ? `${s.trim()}\n${d}` : d)); }
  protected toggleApi(key: string) { this.picked.update((p) => { const n = new Set(p); if (n.has(key)) n.delete(key); else n.add(key); return n; }); }

  protected async test() {
    const seed = this.seed().trim();
    if (!seed) return;
    await this.applySettings();
    this.result.set(await this.testing.run(seed));
  }

  private async applySettings() {
    const d = DEPTHS.find((x) => x.key === this.depth())!;
    await this.saveSettings.run({ maxPages: d.pages, maxSeconds: d.seconds, maxAiPages: this.assist() === 'none' ? 0 : 8, assist: this.assist() === 'none' ? null : this.assist(), collectEmails: this.emails() });
  }

  /** Salva tudo: escolhas do coletor e as fontes na campanha (sites viram uma fonte web: por site). */
  protected async save() {
    const c = this.campaign();
    if (!c) return;
    await this.applySettings();
    const kept = c.sources.filter((s) => !s.startsWith('web:') && !APIS.some((a) => s.startsWith(a.key + ':')));
    const apis = APIS.filter((a) => this.picked().has(a.key)).map((a) => c.sources.find((s) => s.startsWith(a.key + ':')) ?? a.source);
    const sources = [...kept, ...apis, ...this.siteList().map((s) => `web:${s}`)];
    if ((await this.saveCampaign.run({ id: c.id, body: { mode: c.mode, minFitScore: c.minFitScore, maxSubmissionsPerDay: c.maxSubmissionsPerDay, technologies: c.technologies, locations: c.locations, sources } })) !== null) {
      this.wizard.set(false);
      await this.campaigns.reload();
    }
  }
}
