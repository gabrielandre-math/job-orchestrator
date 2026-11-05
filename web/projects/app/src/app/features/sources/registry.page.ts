import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { ChartRow, HBars } from '@jo/ui/charts';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { Campaign, get, send } from '../../core/api';
import { command } from '../../core/command';
import { ago } from '../../core/format';
import { query } from '../../core/query';
import { SOURCE_TABS, Tabs } from '../../shared/tabs';

interface Probe { prefix: string; source: string | null; ok: boolean; count: number; http: number | null; seconds: number; error: string | null; at: string }
interface Entry {
  prefix: string; name: string; kind: string; access: string; lifecycle: string; countries: string[]; docsUrl: string; termsUrl: string | null; attribution: string | null;
  refresh: string; catalogIds: string[]; notes: string; credential: string | null; credentialPresent: boolean; disabled: boolean; gate: string | null; probe: Probe | null; connector: boolean;
}
interface Registry { at: string; experimentalOn: boolean; sources: Entry[]; catalog: { name: string; compiledOn: string; records: number; byMethod: Record<string, number>; byCategory: Record<string, number>; matchedToConnectors: number }; probedAt: string | null }

const ORDER = ['ACTIVE', 'EXPERIMENTAL', 'CREDENTIALS_REQUIRED', 'DISCOVERY_ONLY', 'PARTNER_ONLY', 'BLOCKED'];
const LABEL: Record<string, { title: string; hint: string }> = {
  ACTIVE: { title: 'Ativas', hint: 'API ou feed público documentado pela própria fonte' },
  EXPERIMENTAL: { title: 'Experimentais', hint: 'endpoint que o site público usa, sem contrato; pode mudar' },
  CREDENTIALS_REQUIRED: { title: 'Esperando chave', hint: 'API oficial que pede a chave da pessoa' },
  DISCOVERY_ONLY: { title: 'Só descoberta', hint: 'serve para achar empresas e links, não para coletar' },
  PARTNER_ONLY: { title: 'Só com parceria', hint: 'contrato com a fonte; nunca coletada' },
  BLOCKED: { title: 'Bloqueadas', hint: 'a política da fonte proíbe; nunca coletada' },
};
const ACCESS: Record<string, string> = { OFFICIAL_PUBLIC: 'oficial pública', OBSERVED: 'observada', CREDENTIALS_REQUIRED: 'com chave', PARTNER_ONLY: 'parceria' };

/**
 * Registro de fontes (especificação V2): cada plataforma com o estado, a documentação e os termos que valem, a chave quando pede, o portão
 * (por que não coleta agora) e a prova da última sondagem real (código HTTP, vagas e tempo). Desligar uma plataforma fecha o portão dela.
 */
@Component({
  selector: 'app-registry',
  imports: [Company, HBars, Icon, PageHero, Tabs],
  templateUrl: './registry.page.html',
  styleUrl: './registry.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RegistryPage {
  protected readonly tabs = SOURCE_TABS;
  protected readonly registry = query<Registry>(() => '/sources/registry', 0);
  protected readonly filter = signal<string | null>(null);
  protected readonly ago = ago;
  protected readonly label = LABEL;
  protected readonly access = ACCESS;
  protected readonly probing = command(async (_: void) => {
    const samples = (await get<Campaign[]>('/campaigns'))[0]?.sources ?? [];
    return send<Probe[]>('POST', '/sources/registry/probe', { samples });
  }, (r) => `Sondagem pronta: ${r.filter((p) => p.ok && p.count > 0).length} de ${r.filter((p) => p.source).length} responderam com vaga.`);
  protected readonly toggling = command((x: { prefix: string; disabled: boolean }) => send('PUT', '/sources/registry/disable', x));
  protected readonly experimental = command((on: boolean) => send('PUT', '/sources/registry/experimental', { on }), (_) => 'Preferência das experimentais salva.');

  protected readonly all = computed(() => this.registry.value()?.sources ?? []);
  protected readonly groups = computed(() => {
    const f = this.filter();
    return ORDER.map((key) => ({ key, ...LABEL[key], items: this.all().filter((s) => s.lifecycle === key && (!f || f === key)) })).filter((g) => g.items.length > 0);
  });
  protected readonly counts = computed(() => ORDER.map((key) => ({ key, title: LABEL[key].title, n: this.all().filter((s) => s.lifecycle === key).length })).filter((c) => c.n > 0));
  protected readonly stats = computed<HeroStat[]>(() => {
    const all = this.all();
    const probed = all.filter((s) => s.probe?.source);
    return [
      { label: 'Plataformas no registro', value: all.length },
      { label: 'Com conector no app', value: all.filter((s) => s.connector).length },
      { label: 'Autorizadas agora', value: all.filter((s) => !s.gate && (s.lifecycle === 'ACTIVE' || s.lifecycle === 'EXPERIMENTAL')).length, tone: 'accent' },
      { label: 'Funcionando na sondagem', value: probed.filter((s) => s.probe!.ok && s.probe!.count > 0).length, tone: 'ok', note: probed.length ? `de ${probed.length} sondadas` : 'sem sondagem ainda' },
    ];
  });
  protected readonly methods = computed<ChartRow[]>(() => Object.entries(this.registry.value()?.catalog.byMethod ?? {}).sort((a, b) => b[1] - a[1]).slice(0, 8)
    .map(([label, value]) => ({ label: label.replaceAll('_', ' '), value })));

  protected host(url: string) { try { return new URL(url).hostname.replace(/^(www|docs|developer|developers|api)\./, ''); } catch { return null; } }
  protected locked(s: Entry) { return s.lifecycle === 'BLOCKED' || s.lifecycle === 'PARTNER_ONLY'; }
  protected probeText(p: Probe | null) {
    if (!p) return 'ainda não sondada';
    if (!p.source) return p.error ?? 'não sondada';
    return p.ok ? `${p.count} ${p.count === 1 ? 'vaga' : 'vagas'} em ${p.seconds.toLocaleString('pt-BR')} s` : (p.error ?? 'falhou');
  }
  protected async toggle(s: Entry) { if ((await this.toggling.run({ prefix: s.prefix, disabled: !s.disabled })) !== null) await this.registry.reload(); }
  protected async probe() { if ((await this.probing.run()) !== null) await this.registry.reload(); }
  protected async setExperimental(on: boolean) { if ((await this.experimental.run(on)) !== null) await this.registry.reload(); }
}
