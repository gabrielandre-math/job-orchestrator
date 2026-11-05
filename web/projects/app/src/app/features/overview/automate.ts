import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { AuthorizationItem, Campaign, Confirmations, ExploreStatus, MailOverview, MailSettings, OnboardingView, Profile, get, send } from '../../core/api';
import { command } from '../../core/command';
import { SearchRun } from '../../core/search';
import { Wizard } from '../../shared/wizard';

interface TimerView { name: string; label: string; enabled: boolean; active: boolean; next: string | null; }
interface UnitsView { available: boolean; reason: string | null; timers: TimerView[]; mode?: 'systemd' | 'app'; }
interface Check { label: string; hint: string; done: boolean; optional?: boolean; step?: number; }
type SendMode = 'ReviewRequired' | 'AutoSubmit' | 'DiscoveryOnly';

const COUNTRIES: [string, string][] = [['BR', 'Brasil'], ['US', 'Estados Unidos'], ['PT', 'Portugal'], ['EU', 'União Europeia'], ['CA', 'Canadá'], ['UK', 'Reino Unido']];
const MODE_LABEL: Record<string, string> = { DiscoveryOnly: 'só buscar', PrepareOnly: 'só preparar', ReviewRequired: 'você aprova cada envio', AutoSubmit: 'automático com autorização' };

/**
 * Automatizar tudo: um botão e um caminho só. Mostra o que já está pronto e guia o resto: confirmar o perfil, escolher como os envios
 * saem (aprovação sua ou automático com autorização e limites), ligar a busca contínua, o Explorador e os timers da noite e, se quiser,
 * o e-mail. No fim, a primeira rodada começa e a Busca ao vivo abre.
 */
@Component({
  selector: 'app-automate',
  imports: [Icon, Iso, Wizard],
  templateUrl: './automate.html',
  styleUrl: './automate.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Automate {
  readonly close = output<void>();
  private readonly router = inject(Router);
  private readonly search = inject(SearchRun);
  protected readonly steps = ['O que falta', 'Seu perfil', 'Envio', 'Busca sozinha', 'E-mail', 'Pronto'];
  protected readonly countries = COUNTRIES;
  protected readonly step = signal(1);
  protected readonly loading = signal(true);

  protected readonly onboarding = signal<OnboardingView | null>(null);
  protected readonly profile = signal<Profile | null>(null);
  protected readonly campaign = signal<Campaign | null>(null);
  protected readonly active = signal<AuthorizationItem | null>(null);
  protected readonly explore = signal<ExploreStatus | null>(null);
  protected readonly units = signal<UnitsView | null>(null);
  protected readonly mail = signal<MailOverview | null>(null);
  private mailSettings: MailSettings | null = null;

  protected readonly form = signal<Confirmations | null>(null);
  protected readonly decisions = signal<Record<string, boolean>>({});
  protected readonly dirty = signal(false);

  protected readonly sendMode = signal<SendMode>('ReviewRequired');
  protected readonly days = signal(7);
  protected readonly perDay = signal(3);
  protected readonly minScore = signal(85);
  protected readonly accepted = signal(false);
  protected readonly authText = signal('');

  protected readonly searchOn = signal(true);
  protected readonly searchEvery = signal(60);
  protected readonly explorerOn = signal(true);
  protected readonly explorerEvery = signal(10);
  protected readonly nightOn = signal(false);

  protected readonly mailMode = signal<'Review' | 'Auto'>('Review');
  protected readonly mailConsent = signal(false);
  protected readonly mailConsentText = signal('');

  protected readonly confirming = command((body: { confirmations: Confirmations; decisions: Record<string, boolean> }) => send('PUT', '/profile/confirmations', body), 'Perfil confirmado.');
  protected readonly granting = command((x: { id: string; body: object }) => send('POST', `/campaigns/${x.id}/authorizations`, x.body), 'Envio automático autorizado.');
  protected readonly saving = command((x: { path: string; method: 'PUT' | 'POST'; body: object }) => send(x.method, x.path, x.body));

  protected readonly inferred = computed(() => (this.profile()?.facts ?? []).filter((f) => f.status === 'Inferred'));
  protected readonly undecided = computed(() => this.inferred().filter((f) => !(f.id in this.decisions())).length);
  protected readonly ready = computed(() => this.profile()?.status === 'Ready');
  protected readonly nightAll = computed(() => (this.units()?.timers ?? []).length > 0 && (this.units()?.timers ?? []).every((t) => t.enabled));
  protected readonly maxCap = computed(() => Math.min(15, this.campaign()?.maxSubmissionsPerDay ?? 5));

  protected readonly checks = computed<Check[]>(() => {
    const o = this.onboarding(), p = this.profile(), c = this.campaign(), e = this.explore(), u = this.units(), m = this.mail(), a = this.active();
    return [
      { label: 'IA conectada', done: !!o?.aiReady, hint: o?.aiReady ? o.accounts.slice(0, 3).join(', ') : 'conecte em Provedores de IA: assinatura, chave ou modelo local' },
      { label: 'Perfil confirmado', done: p?.status === 'Ready', step: 2, hint: p?.status === 'Ready' ? `${p.facts.filter((f) => f.status !== 'Rejected').length} fatos, confirmado em ${this.date(p.confirmedAt)}` : p?.status === 'Empty' ? 'envie o currículo no Perfil' : 'pisos, modalidade, início e inglês' },
      { label: 'Busca montada', done: !!c && c.technologies.length > 0 && c.sources.length > 0, hint: c ? `${c.technologies.length} palavras, ${c.titles?.length ?? 0} cargos, ${c.sources.length} fontes` : 'monte no primeiro contato' },
      { label: 'Como os envios saem', done: !!c && c.mode !== 'DiscoveryOnly', step: 3, hint: c ? (a ? `automático até ${this.date(a.expiresAt)}, até ${a.maxPerDay} por dia` : MODE_LABEL[c.mode] ?? c.mode) : '' },
      { label: 'Busca sozinha e Explorador', done: !!c?.enabled && !!e?.enabled, step: 4, hint: c?.enabled ? `a cada ${this.every(e?.searchIntervalMinutes ?? 60)}; Explorador ${e?.enabled ? `a cada ${e.intervalMinutes} min` : 'desligado'}` : 'desligada: só busca quando você aperta' },
      { label: 'Rodadas da noite', done: this.nightAll(), optional: true, step: 4, hint: u?.available === false ? 'sem systemd aqui' : this.nightAll() ? 'timers ligados: 23h05, 1h35, 4h05' : 'opcional: timers do systemd' },
      { label: 'E-mail de candidatura', done: !!m?.connected, optional: true, step: 5, hint: m?.connected ? (m.mode === 'Auto' ? 'automático com consentimento' : 'você aprova cada e-mail') : 'opcional: para vagas que pedem currículo por e-mail' },
    ];
  });
  protected readonly missing = computed(() => this.checks().filter((c) => !c.done && !c.optional).length);

  protected readonly summary = computed(() => {
    const c = this.campaign(), e = this.explore(), m = this.mail(), a = this.active();
    return [
      { on: this.ready(), label: 'Perfil', text: this.ready() ? 'confirmado; só fatos do currículo vão para os formulários' : 'ainda não confirmado: os envios esperam por isso' },
      { on: !!c && c.mode !== 'DiscoveryOnly', label: 'Envios', text: a && c?.mode === 'AutoSubmit' ? `automáticos até ${this.date(a.expiresAt)}, até ${a.maxPerDay} por dia, nota a partir de ${a.minScore}` : MODE_LABEL[c?.mode ?? ''] ?? '' },
      { on: !!c?.enabled, label: 'Busca', text: c?.enabled ? `sozinha a cada ${this.every(e?.searchIntervalMinutes ?? 60)}, em ${c.sources.length} fontes` : 'só quando você aperta Buscar agora' },
      { on: !!e?.enabled, label: 'Explorador', text: e?.enabled ? `procura fontes novas a cada ${e.intervalMinutes} min` : 'desligado' },
      { on: this.nightAll(), label: 'Noite', text: this.nightAll() ? 'rodadas às 23h05, 1h35 e 4h05, resumo às 7h45' : 'timers desligados' },
      { on: !!m?.connected, label: 'E-mail', text: m?.connected ? (m.mode === 'Auto' ? 'automático, com consentimento' : 'você aprova cada e-mail') : 'não conectado' },
    ];
  });

  constructor() {
    void get<{ text: string }>('/authorizations/text').then((t) => this.authText.set(t.text)).catch(() => undefined);
    void get<{ text: string }>('/mail/consent').then((t) => this.mailConsentText.set(t.text)).catch(() => undefined);
    void this.load(true);
  }

  private async load(first = false) {
    const [o, p, cs, e, u, m] = await Promise.all([
      get<OnboardingView>('/onboarding').catch(() => null), get<Profile>('/profile').catch(() => null), get<Campaign[]>('/campaigns').catch(() => []),
      get<ExploreStatus>('/explore/status').catch(() => null), get<UnitsView>('/units').catch(() => null), get<MailOverview>('/mail/overview').catch(() => null),
    ]);
    const c = cs[0] ?? null;
    const auths = c ? await get<AuthorizationItem[]>(`/campaigns/${c.id}/authorizations`).catch(() => []) : [];
    this.onboarding.set(o); this.profile.set(p); this.campaign.set(c); this.explore.set(e); this.units.set(u); this.mail.set(m);
    this.active.set(auths.find((a) => a.status === 'ativa') ?? null);
    if (m?.connected) this.mailSettings = await get<MailSettings>('/mail/settings').catch(() => null);
    if (first) {
      if (p) {
        const english = p.facts.find((f) => f.kind === 'language' && /ingl|english/i.test(f.statement))?.level ?? null;
        this.form.set({ ...p.confirmations, englishLevel: p.confirmations.englishLevel ?? this.level(english) });
      }
      if (c) {
        this.sendMode.set(c.mode === 'AutoSubmit' ? 'AutoSubmit' : c.mode === 'DiscoveryOnly' ? 'ReviewRequired' : 'ReviewRequired');
        this.minScore.set(Math.max(c.minFitScore, 85));
        this.perDay.set(Math.min(3, this.maxCap()));
      }
      if (e) { this.explorerOn.set(e.enabled); this.explorerEvery.set(e.intervalMinutes); this.searchEvery.set(e.searchIntervalMinutes); }
      this.nightOn.set(this.nightAll() || !!u?.available);
      if (this.mailSettings) { this.mailMode.set(this.mailSettings.mode === 'Auto' ? 'Auto' : 'Review'); this.mailConsent.set(!!this.mailSettings.autoConsentAt); }
      this.loading.set(false);
    }
  }

  /** O nível do fato do currículo ("avançado") vira a opção do formulário. */
  private level(text: string | null) {
    if (!text) return null;
    const t = text.toLowerCase();
    return t.includes('nativ') ? 'native' : t.includes('flu') ? 'fluent' : t.includes('avan') || t.includes('advanced') ? 'advanced' : t.includes('inter') ? 'intermediate' : t.includes('bás') || t.includes('basic') ? 'basic' : null;
  }

  protected set<K extends keyof Confirmations>(key: K, value: Confirmations[K]) { this.form.update((f) => (f ? { ...f, [key]: value } : f)); this.dirty.set(true); }
  protected toggleIn(key: 'authorizedCountries' | 'modalities', value: string) {
    const list = this.form()?.[key] ?? [];
    this.set(key, list.includes(value) ? list.filter((x) => x !== value) : [...list, value]);
  }
  protected money(key: 'floorCltBrl' | 'floorPjBrl' | 'floorUsdMonth', raw: string) {
    const digits = raw.replace(/\D/g, '');
    this.set(key, digits ? Number(digits) : null);
  }
  protected decide(id: string, ok: boolean) { this.decisions.update((d) => ({ ...d, [id]: ok })); this.dirty.set(true); }
  protected num(e: Event) { return +(e.target as HTMLInputElement).value; }
  protected every(m: number) { return m < 60 ? `${m} min` : m === 60 ? '1 hora' : `${m / 60} horas`; }
  protected date(iso: string | null | undefined) { return iso ? new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: 'short' }) : ''; }

  protected readonly busy = computed(() => this.confirming.running() || this.granting.running() || this.saving.running());
  protected readonly canNext = computed(() => {
    switch (this.step()) {
      case 2: return !this.form() || this.undecided() === 0;
      case 3: return this.sendMode() !== 'AutoSubmit' || !!this.active() || (this.accepted() && this.ready());
      case 5: return !this.mail()?.connected || this.mailMode() !== 'Auto' || this.mailConsent();
      default: return true;
    }
  });

  /** Cada passo grava o que você escolheu antes de seguir; erro mostra o motivo e fica no passo. */
  protected async next() {
    const ok = await this.apply(this.step());
    if (!ok) return;
    await this.load();
    this.step.update((s) => Math.min(6, s + 1));
  }

  private async apply(step: number): Promise<boolean> {
    const c = this.campaign();
    switch (step) {
      case 2: {
        const f = this.form();
        if (!f || (this.ready() && !this.dirty())) return true;
        return (await this.confirming.run({ confirmations: f, decisions: this.decisions() })) !== null;
      }
      case 3: {
        if (!c) return true;
        if (this.sendMode() === 'AutoSubmit') {
          if (this.active()) return c.mode === 'AutoSubmit' || (await this.putCampaign(c, { mode: 'AutoSubmit' }));
          return (await this.granting.run({ id: c.id, body: { days: this.days(), maxPerDay: this.perDay(), minScore: this.minScore(), adapters: [], accepted: this.accepted() } })) !== null;
        }
        return c.mode === this.sendMode() || (await this.putCampaign(c, { mode: this.sendMode() }));
      }
      case 4: {
        if (c && c.enabled !== this.searchOn() && !(await this.putCampaign(c, { night: this.searchOn() }))) return false;
        if ((await this.saving.run({ method: 'PUT', path: '/explore/settings', body: { enabled: this.explorerOn(), intervalMinutes: this.explorerEvery(), searchIntervalMinutes: this.searchEvery() } })) === null) return false;
        if (this.units()?.available && this.nightOn() !== this.nightAll()) return (await this.saving.run({ method: 'PUT', path: '/units/night', body: { on: this.nightOn() } })) !== null;
        return true;
      }
      case 5: {
        const s = this.mailSettings;
        if (!this.mail()?.connected || !s) return true;
        const mode = this.mailMode();
        if ((s.mode === 'Auto') === (mode === 'Auto') && s.mode !== 'Draft') return true;
        return (await this.saving.run({ method: 'PUT', path: '/mail/settings', body: { mode, dailyCap: s.dailyCap, followUpDays: s.followUpDays, windowStart: s.windowStart, windowEnd: s.windowEnd, attachResume: s.attachResume, consent: mode === 'Auto' && this.mailConsent() } })) !== null;
      }
      default: return true;
    }
  }

  private async putCampaign(c: Campaign, patch: { mode?: string; night?: boolean }) {
    const body = { mode: patch.mode ?? c.mode, minFitScore: c.minFitScore, maxSubmissionsPerDay: c.maxSubmissionsPerDay, technologies: c.technologies, titles: c.titles, locations: c.locations, sources: c.sources, night: patch.night ?? c.enabled };
    return (await this.saving.run({ method: 'PUT', path: `/campaigns/${c.id}`, body })) !== null;
  }

  protected back() { this.step.update((s) => Math.max(1, s - 1)); }

  protected goMail() { this.close.emit(); void this.router.navigate(['/email']); }

  /** Primeira rodada agora, o Explorador junto, e a Busca ao vivo aberta para acompanhar. */
  protected async start() {
    void this.search.start();
    if (this.explore()?.enabled) void send('POST', '/explore/run').catch(() => undefined);
    this.close.emit();
    await this.router.navigate(['/busca']);
  }
}
