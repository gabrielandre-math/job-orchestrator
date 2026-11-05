import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { Campaign, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';
import { SearchRun } from '../../core/search';
import { SourceInfo, describeSource } from '../../core/sources';
import { SOURCE_TABS, Tabs } from '../../shared/tabs';
import { AddSource } from './add-source';

const GROUPS: { kind: SourceInfo['kind']; title: string; hint: string }[] = [
  { kind: 'ats', title: 'Empresas', hint: 'quadros de vagas oficiais (ATS)' },
  { kind: 'api', title: 'APIs públicas', hint: 'agregadores abertos' },
  { kind: 'community', title: 'Comunidades', hint: 'repositórios e fios de vagas' },
  { kind: 'site', title: 'Sites de carreiras', hint: 'lidos pelo coletor' },
];

/** Fontes: de onde vêm as vagas da campanha, agrupadas, com o resultado da última rodada de cada uma e o assistente para adicionar. */
@Component({
  selector: 'app-sources',
  imports: [RouterLink, AddSource, Company, Icon, PageHero, Tabs],
  templateUrl: './sources.page.html',
  styleUrl: './sources.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SourcesPage {
  private readonly search = inject(SearchRun);
  protected readonly tabs = SOURCE_TABS;
  protected readonly campaigns = query<Campaign[]>(() => '/campaigns', 0);
  protected readonly adding = signal(false);
  protected readonly removing = signal<string | null>(null);
  protected readonly saving = command((x: { c: Campaign; sources: string[] }) => send('PUT', `/campaigns/${x.c.id}`, {
    mode: x.c.mode, minFitScore: x.c.minFitScore, maxSubmissionsPerDay: x.c.maxSubmissionsPerDay, technologies: x.c.technologies, locations: x.c.locations, sources: x.sources,
  }));
  protected readonly campaign = computed(() => this.campaigns.value()?.[0] ?? null);
  protected readonly last = computed(() => new Map((this.search.state()?.sources ?? []).map((s) => [s.source, s])));
  protected readonly groups = computed(() => {
    const all = (this.campaign()?.sources ?? []).map(describeSource);
    return GROUPS.map((g) => ({ ...g, items: all.filter((s) => s.kind === g.kind) })).filter((g) => g.items.length > 0);
  });
  protected readonly stats = computed<HeroStat[]>(() => {
    const all = (this.campaign()?.sources ?? []).map(describeSource);
    const run = this.search.state();
    return [
      { label: 'Fontes na campanha', value: all.length, tone: 'accent' },
      { label: 'Empresas', value: all.filter((s) => s.kind === 'ats').length },
      { label: 'APIs e comunidades', value: all.filter((s) => s.kind === 'api' || s.kind === 'community').length },
      { label: 'Vagas na última rodada', value: run?.listed ?? 0, note: run ? `${run.kept} da sua área` : undefined },
    ];
  });

  constructor() { void this.search.load(); }

  protected async add(source: string) {
    const c = this.campaign();
    if (!c || c.sources.includes(source)) return;
    if ((await this.saving.run({ c, sources: [...c.sources, source] })) !== null) await this.campaigns.reload();
  }

  protected async remove(source: string) {
    if (this.removing() !== source) { this.removing.set(source); setTimeout(() => this.removing.update((r) => (r === source ? null : r)), 4000); return; }
    const c = this.campaign();
    if (!c) return;
    this.removing.set(null);
    if ((await this.saving.run({ c, sources: c.sources.filter((s) => s !== source) })) !== null) await this.campaigns.reload();
  }

  protected lastText(source: string) {
    const x = this.last().get(source);
    if (!x) return 'ainda não consultada';
    if (x.state === 'failed') return 'não respondeu na última rodada';
    if (x.state !== 'done') return 'consultando agora…';
    return `${x.listed} ${x.listed === 1 ? 'vaga' : 'vagas'}, ${x.kept} da sua área`;
  }
}
