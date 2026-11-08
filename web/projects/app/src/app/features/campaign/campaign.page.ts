import { ChangeDetectionStrategy, Component, computed, effect, signal } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { Campaign, send } from '../../core/api';
import { Help } from '../../shared/help';
import { SETTINGS_TABS, Tabs } from '../../shared/tabs';
import { command } from '../../core/command';
import { query } from '../../core/query';
import { ago } from '../../core/format';

/**
 * O que buscar: cargos, palavras, locais e a nota de corte. The app finds, scores and prepares; sending is always yours, with the
 * copilot, so the campaign always prepares and never sends by itself.
 */
@Component({
  selector: 'app-campaign',
  imports: [PageHero, Icon, Help, Tabs],
  templateUrl: './campaign.page.html',
  styleUrl: './campaign.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CampaignPage {
  protected readonly tabs = SETTINGS_TABS;
  protected readonly list = query<Campaign[]>(() => '/campaigns');
  protected readonly form = signal({ minFitScore: 85, maxSubmissionsPerDay: 15, technologies: '', titles: '', locations: '', sources: '' });
  protected readonly saved = signal(false);
  protected readonly save = command((c: Campaign) => send('PUT', `/campaigns/${c.id}`, this.payload()));
  protected readonly run = command((c: Campaign) => send('POST', `/campaigns/${c.id}/runs`), 'Busca pedida. As fontes começam a ser lidas.');
  protected readonly ago = ago;
  /** Exemplo da ajuda da nota de corte: uma nota acima e outra abaixo do valor no campo. */
  protected readonly above = computed(() => Math.min(100, Number(this.form().minFitScore) + 5));
  protected readonly below = computed(() => (Number(this.form().minFitScore) >= 10 ? Number(this.form().minFitScore) - 10 : null));
  protected heroStats(c: Campaign): HeroStat[] {
    return [
      { label: 'Cargos', value: (c.titles ?? []).length },
      { label: 'Palavras-chave', value: c.technologies.length },
      { label: 'Fontes', value: c.sources.length },
      { label: 'Nota de corte', value: c.minFitScore, tone: 'accent' },
    ];
  }

  constructor() {
    effect(() => {
      const c = this.list.value()?.[0];
      if (c) this.form.set({ minFitScore: c.minFitScore, maxSubmissionsPerDay: c.maxSubmissionsPerDay, technologies: c.technologies.join(', '), titles: (c.titles ?? []).join(', '), locations: c.locations.join(', '), sources: c.sources.join('\n') });
    });
  }

  protected set(key: string, value: string | number) { this.form.update((f) => ({ ...f, [key]: value })); this.saved.set(false); }

  private payload() {
    const f = this.form();
    const split = (s: string, by: RegExp) => s.split(by).map((x) => x.trim()).filter(Boolean);
    // The sources are kept as they are (Fontes edits them); the mode is always "prepare": you send, with the copilot.
    return { mode: 'PrepareOnly', minFitScore: Number(f.minFitScore), maxSubmissionsPerDay: Number(f.maxSubmissionsPerDay), technologies: split(f.technologies, /,/), titles: split(f.titles, /,/), locations: split(f.locations, /,/), sources: split(f.sources, /\n/) };
  }

  protected async submit(c: Campaign) {
    if ((await this.save.run(c)) !== null || !this.save.problem()) { this.saved.set(!this.save.problem()); await this.list.reload(); }
  }
}
