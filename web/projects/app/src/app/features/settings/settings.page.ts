import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { PageHero } from '@jo/ui/page-hero';
import { Campaign, ExploreStatus, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';

interface Privacy { localOnly: boolean; retentionDays: number }
interface TimerView { name: string; label: string; enabled: boolean; active: boolean; next: string | null; }
interface UnitsView { available: boolean; reason: string | null; timers: TimerView[]; mode?: 'systemd' | 'app'; }
const PHRASE = 'APAGAR TUDO';

/**
 * Configurações e segurança: busca da noite (os timers do systemd e o comando para instalar), modo privado (só o modelo local),
 * retenção, exportar os seus dados, revogar tudo e apagar tudo com a frase digitada.
 */
@Component({
  selector: 'app-settings',
  imports: [Icon, PageHero],
  templateUrl: './settings.page.html',
  styleUrl: './settings.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SettingsPage {
  protected readonly settings = query<Privacy>(() => '/settings', 0);
  protected readonly campaigns = query<Campaign[]>(() => '/campaigns', 0);
  protected readonly campaign = computed(() => this.campaigns.value()?.[0] ?? null);
  protected readonly explore = query<ExploreStatus>(() => '/explore/status', 0);
  protected readonly units = query<UnitsView>(() => '/units', 0);
  protected readonly nightAll = computed(() => (this.units.value()?.timers ?? []).length > 0 && (this.units.value()?.timers ?? []).every((t) => t.enabled));
  protected readonly exploreSaving = command((b: { enabled: boolean; intervalMinutes: number; searchIntervalMinutes: number }) => send('PUT', '/explore/settings', b), 'Busca contínua atualizada.');
  protected readonly unitsSaving = command((on: boolean) => send<UnitsView>('PUT', '/units/night', { on }), (r) => (r.timers.every((t) => t.enabled) ? 'Timers da noite ligados.' : 'Timers da noite desligados.'));
  protected readonly phrase = PHRASE;
  protected readonly typed = signal('');
  protected readonly arming = signal(false);
  protected readonly copied = signal(false);
  protected readonly install = 'scripts/install-units.sh --enable';
  protected readonly saving = command((p: Privacy) => send('PUT', '/settings', p), 'Configuração salva.');
  protected readonly nightSaving = command((x: { c: Campaign; on: boolean }) => send('PUT', `/campaigns/${x.c.id}`, {
    mode: x.c.mode, minFitScore: x.c.minFitScore, maxSubmissionsPerDay: x.c.maxSubmissionsPerDay, technologies: x.c.technologies, locations: x.c.locations, sources: x.c.sources, night: x.on,
  }), (r) => 'Busca da noite atualizada.');
  protected readonly revoking = command((_: void) => send<{ authorizations: number; mail: number }>('POST', '/settings/revoke'), (r) => `Revogado: ${r.authorizations} autorizações de envio e ${r.mail} envio automático de e-mail.`);
  protected readonly wiping = command((_: void) => send('POST', '/settings/wipe', { confirm: this.typed() }), 'Tudo apagado. Contas de IA e campanha continuam.');

  protected async privacy(localOnly: boolean) { const s = this.settings.value(); if (s && (await this.saving.run({ ...s, localOnly })) !== null) await this.settings.reload(); }
  protected async retention(days: number) { const s = this.settings.value(); if (s && (await this.saving.run({ ...s, retentionDays: days })) !== null) await this.settings.reload(); }
  protected async night(on: boolean) { const c = this.campaign(); if (c && (await this.nightSaving.run({ c, on })) !== null) await this.campaigns.reload(); }
  protected async continuous(patch: Partial<{ enabled: boolean; intervalMinutes: number; searchIntervalMinutes: number }>) {
    const e = this.explore.value();
    if (!e) return;
    if ((await this.exploreSaving.run({ enabled: e.enabled, intervalMinutes: e.intervalMinutes, searchIntervalMinutes: e.searchIntervalMinutes, ...patch })) !== null) await this.explore.reload();
  }
  protected async timers(on: boolean) { if ((await this.unitsSaving.run(on)) !== null) await this.units.reload(); }
  protected every(m: number) { return m < 60 ? `${m} min` : m === 60 ? '1 hora' : `${m / 60} horas`; }
  protected when(iso: string | null) { return iso ? new Date(iso).toLocaleString('pt-BR', { weekday: 'short', hour: '2-digit', minute: '2-digit' }) : ''; }
  protected async revoke() {
    if (!this.arming()) { this.arming.set(true); setTimeout(() => this.arming.set(false), 5000); return; }
    this.arming.set(false);
    await this.revoking.run();
  }
  protected async wipe() { if (this.typed().trim() === PHRASE && (await this.wiping.run()) !== null) { this.typed.set(''); await this.campaigns.reload(); } }
  protected async copy() { try { await navigator.clipboard.writeText(this.install); this.copied.set(true); setTimeout(() => this.copied.set(false), 1500); } catch { /* sem área de transferência */ } }
}
