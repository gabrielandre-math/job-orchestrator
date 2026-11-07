import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Mark } from '@jo/ui/mark';
import { Account, Campaign, OnboardingView, Profile, Provider, SetupProposal, SourceTestResult, get, send, upload } from '../../core/api';
import { command } from '../../core/command';
import { AGENT_ICON, Live } from '../../core/live';
import { SearchRun } from '../../core/search';
import { describeSource } from '../../core/sources';
import { Wizard } from '../../shared/wizard';
import { Connect } from '../providers/connect';
import { LinkedInImport } from '../profile/linkedin-import';

interface Pick { source: string; why: string; on: boolean; test: SourceTestResult | null; testing: boolean }

/**
 * Primeiro contato, na mesma moldura do conectar conta: boas-vindas, a IA da pessoa, o currículo (lido ao vivo pelos agentes),
 * a busca proposta pelo assistente a partir do currículo (cada fonte testada de verdade) e pronto, com a primeira busca já rodando.
 */
@Component({
  selector: 'app-welcome',
  imports: [Company, Connect, Icon, Iso, LinkedInImport, Mark, Wizard],
  templateUrl: './welcome.html',
  styleUrl: './welcome.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Welcome {
  private readonly router = inject(Router);
  private readonly live = inject(Live);
  private readonly search = inject(SearchRun);
  readonly close = output<void>();

  protected readonly step = signal(1);
  protected readonly state = signal<OnboardingView | null>(null);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly providers = signal<Provider[]>([]);
  protected readonly connecting = signal(false);
  /** Conectar o LinkedIn sem sair do primeiro contato: o assistente dele abre por cima e, ao fechar, o perfil já vem atualizado. */
  protected readonly linkedin = signal(false);
  protected readonly profile = signal<Profile | null>(null);
  protected readonly reading = signal(false);
  protected readonly dragging = signal(false);
  protected readonly proposal = signal<SetupProposal | null>(null);
  protected readonly keywords = signal<string[]>([]);
  protected readonly titles = signal<string[]>([]);
  protected readonly locations = signal<string[]>([]);
  protected readonly minScore = signal(75);
  protected readonly picks = signal<Pick[]>([]);
  protected readonly newKeyword = signal('');
  protected readonly newPlace = signal('');
  protected readonly icons = AGENT_ICON;
  protected readonly describe = describeSource;

  protected readonly importing = command((x: { file: File; replace: boolean }) => upload<unknown>(`/profile/documents${x.replace ? '?replace=true' : ''}`, x.file));
  protected readonly proposing = command((_: void) => send<SetupProposal>('POST', '/assistant/setup'));
  protected readonly applying = command(async (_: void) => {
    const campaigns = await get<Campaign[]>('/campaigns');
    const c = campaigns[0];
    if (!c) throw { status: 0, code: 'no-campaign', detail: 'Nenhuma campanha para configurar.' };
    const sources = [...new Set([...this.picks().filter((p) => p.on).map((p) => p.source)])];
    await send('PUT', `/campaigns/${c.id}`, { mode: c.mode, minFitScore: this.minScore(), maxSubmissionsPerDay: c.maxSubmissionsPerDay, technologies: this.keywords(), titles: this.titles(), locations: this.locations(), sources, name: this.proposal()?.area || undefined });
    await send('PUT', '/onboarding', { done: true });
  }, 'Ambiente pronto. A primeira busca já começou.');

  /** A conversa dos agentes lendo o currículo, ao vivo. */
  protected readonly readingFeed = computed(() => this.live.feed().filter((l) => l.kind === 'profile').slice(-5).reverse());
  protected readonly realProfile = computed(() => { const p = this.profile(); return !!p && !p.synthetic && p.facts.length > 0; });
  protected readonly doc = computed(() => this.profile()?.documents.at(-1) ?? null);
  protected readonly readyAccounts = computed(() => this.providers().filter((p) => p.available));

  constructor() { void this.refresh(); }

  private async refresh() {
    try {
      const [state, accounts, providers, profile] = await Promise.all([get<OnboardingView>('/onboarding'), get<Account[]>('/accounts'), get<Provider[]>('/providers'), get<Profile>('/profile')]);
      this.state.set(state); this.accounts.set(accounts); this.providers.set(providers); this.profile.set(profile);
    } catch { /* a tela mostra o que tiver */ }
  }

  protected async connected() { await this.refresh(); }

  protected async drop(e: DragEvent) {
    e.preventDefault();
    this.dragging.set(false);
    const f = e.dataTransfer?.files?.[0];
    if (f) await this.read(f);
  }
  protected async choose(e: Event) {
    const f = (e.target as HTMLInputElement).files?.[0];
    if (f) await this.read(f);
  }

  /**
   * Um currículo novo substitui o perfil inteiro (o de teste ou o antigo), para nunca misturar duas pessoas. A troca acontece no servidor
   * só depois que o arquivo novo foi lido sem erro: um arquivo ruim deixa o perfil anterior como estava.
   */
  private async read(file: File) {
    if ((await this.importing.run({ file, replace: !!this.profile()?.facts.length })) === null) return;
    this.reading.set(true);
    try {
      for (let i = 0; i < 120; i++) {
        await new Promise((r) => setTimeout(r, 1500));
        const p = await get<Profile>('/profile');
        this.profile.set(p);
        const d = p.documents.at(-1);
        if (d && (d.status === 'Ready' || d.status === 'Failed')) break;
      }
    } finally {
      this.reading.set(false);
    }
  }

  protected async propose() {
    this.step.set(4);
    if (this.proposal()) return;
    const p = await this.proposing.run();
    if (!p) return;
    this.proposal.set(p);
    this.keywords.set(p.keywords); this.titles.set(p.titles); this.locations.set(p.locations); this.minScore.set(p.minFitScore || 75);
    this.picks.set(p.sources.map((s) => ({ source: s.source, why: s.why, on: true, test: null, testing: true })));
    // Cada fonte proposta é testada de verdade: a que não responde fica desmarcada.
    await Promise.all(p.sources.map(async (s) => {
      let r: SourceTestResult | null = null;
      try { r = await send<SourceTestResult>('POST', '/sources/test', { source: s.source }); } catch { r = null; }
      this.picks.update((all) => all.map((x) => (x.source === s.source ? { ...x, test: r, testing: false, on: !!r?.ok } : x)));
    }));
  }
  protected async redo() { this.proposal.set(null); await this.propose(); }

  protected toggle(source: string) { this.picks.update((all) => all.map((x) => (x.source === source ? { ...x, on: !x.on } : x))); }
  protected drop1(list: 'keywords' | 'locations' | 'titles', v: string) { this[list].update((l) => l.filter((x) => x !== v)); }
  protected addKeyword() { const v = this.newKeyword().trim(); if (v && !this.keywords().includes(v)) this.keywords.update((l) => [...l, v]); this.newKeyword.set(''); }
  protected addPlace() { const v = this.newPlace().trim(); if (v && !this.locations().includes(v)) this.locations.update((l) => [...l, v]); this.newPlace.set(''); }

  protected async finish() {
    if ((await this.applying.run()) === null) return;
    this.step.set(5);
    await this.search.start();
  }
  /** O passo seguinte do primeiro uso: fecha este e abre o Automatizar tudo no Início. */
  protected automate() { this.close.emit(); void this.router.navigate(['/'], { queryParams: { configurar: '1' } }); }
  protected go(path: string) { this.close.emit(); void this.router.navigate([path]); }
  protected async later() { this.close.emit(); }
  protected providerLogo(p: Provider) { return p.family === 'anthropic' ? 'claude' : p.family === 'openai' ? 'openai' : p.family === 'local' ? 'ollama' : null; }
}
