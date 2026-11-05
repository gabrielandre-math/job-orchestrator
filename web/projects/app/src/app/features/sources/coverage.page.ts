import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { query } from '../../core/query';
import { SOURCE_TABS, Tabs } from '../../shared/tabs';

interface Country { iso2: string; iso3: string; name: string; continent: string; status: string; research: string; portals: number; apiCandidates: number; wapes: number; recordIds: string[]; platforms: string[]; pendingPlatforms: string[]; open: number; brazilOk: number; brazilPossible: number; restricted: number }
interface Continent { code: string; name: string; countries: number; integrated: number; pending: number; jobsSeen: number; seeds: number; unresearched: number; open: number }
interface Coverage { at: string; totals: Record<string, number>; continents: Continent[]; countries: Country[]; globalPlatforms: string[]; unmatchedOpen: number }

const STATUS: { key: string; label: string; hint: string }[] = [
  { key: 'INTEGRATED', label: 'Integrado', hint: 'fonte do próprio país com conector, portão aberto e sondagem com vaga' },
  { key: 'CONNECTOR_PENDING', label: 'Conector pendente', hint: 'fonte do país no registro, ainda sem conector ou sem sondagem boa' },
  { key: 'JOBS_SEEN', label: 'Vagas vistas', hint: 'vaga aberta com local no país, vinda de fonte global' },
  { key: 'SOME_SEEDS_NO_LIVE_TEST', label: 'Pistas sem teste', hint: 'a auditoria tem portal, API candidata ou agência, sem teste ao vivo' },
  { key: 'UNRESEARCHED', label: 'Não pesquisado', hint: 'nada pesquisado ainda; nenhuma URL inventada' },
];
const TOTALS: { key: string; label: string }[] = [
  { key: 'configured', label: 'Fontes configuradas' }, { key: 'authorized', label: 'Autorizadas' }, { key: 'working', label: 'Funcionando' },
  { key: 'companyBoards', label: 'Quadros de empresa na campanha' }, { key: 'companiesWithOpenJobs', label: 'Empresas com vaga aberta' }, { key: 'openUnique', label: 'Vagas únicas abertas' },
  { key: 'brazilConfirmed', label: 'Confirmadas para quem mora no Brasil' }, { key: 'brazilPossible', label: 'Possíveis para quem mora no Brasil' }, { key: 'brazilRestricted', label: 'Restritas' },
  { key: 'openWithDate', label: 'Abertas com data da fonte' },
];

/**
 * Cobertura no mundo (especificação V2.2): os 249 países e territórios da auditoria do usuário, cada um com o estado que sai do que existe de
 * verdade, os números desta execução separados (fontes, empresas, vagas, elegibilidade de quem mora no Brasil) e as lacunas por continente.
 */
@Component({
  selector: 'app-coverage',
  imports: [Icon, PageHero, Tabs],
  templateUrl: './coverage.page.html',
  styleUrl: './coverage.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CoveragePage {
  protected readonly tabs = SOURCE_TABS;
  protected readonly data = query<Coverage>(() => '/coverage', 60000);
  protected readonly status = STATUS;
  protected readonly totalsList = TOTALS;
  protected readonly filter = signal<string | null>(null);
  protected readonly search = signal('');
  protected readonly open = signal<string | null>(null);

  protected readonly stats = computed<HeroStat[]>(() => {
    const t = this.data.value()?.totals ?? {};
    const all = this.data.value()?.countries ?? [];
    return [
      { label: 'Países integrados', value: t['countriesIntegrated'] ?? 0, tone: 'ok' },
      { label: 'Com vaga aberta vista', value: t['countriesWithJobs'] ?? 0, tone: 'accent' },
      { label: 'Pistas sem teste', value: all.filter((c) => c.status === 'SOME_SEEDS_NO_LIVE_TEST').length },
      { label: 'Não pesquisados', value: t['countriesUnresearched'] ?? 0, tone: 'warn', note: 'de 249 países e territórios' },
    ];
  });
  protected readonly counts = computed(() => STATUS.map((s) => ({ ...s, n: (this.data.value()?.countries ?? []).filter((c) => c.status === s.key).length })));
  protected readonly groups = computed(() => {
    const d = this.data.value();
    if (!d) return [];
    const q = fold(this.search());
    const f = this.filter();
    return d.continents.map((k) => ({
      ...k,
      items: d.countries.filter((c) => c.continent === k.code && (!f || c.status === f) && (!q || fold(`${c.name} ${c.iso2} ${c.iso3}`).includes(q)))
        .sort((a, b) => STATUS.findIndex((s) => s.key === a.status) - STATUS.findIndex((s) => s.key === b.status) || b.open - a.open || a.name.localeCompare(b.name)),
    })).filter((k) => k.items.length > 0);
  });
  protected readonly selected = computed(() => this.data.value()?.countries.find((c) => c.iso2 === this.open()) ?? null);

  protected seg(k: Continent, part: number) { return k.countries ? (part / k.countries) * 100 : 0; }
  protected label(key: string) { return STATUS.find((s) => s.key === key)?.label ?? key; }
  protected pick(iso: string) { this.open.set(this.open() === iso ? null : iso); }
}

function fold(s: string) { return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().trim(); }
