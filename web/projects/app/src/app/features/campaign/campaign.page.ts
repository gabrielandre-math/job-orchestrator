import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { DomSanitizer } from '@angular/platform-browser';
import { Campaign, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';
import { ago } from '../../core/format';

/** Campanha: critérios, limites e até onde o sistema pode ir sozinho. Envio automático só com autorização (fase 6). */
@Component({
  selector: 'app-campaign',
  imports: [PageHero],
  templateUrl: './campaign.page.html',
  styleUrl: './campaign.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CampaignPage {
  /** Botão da barra de favoritos: abre "Salvar vaga" com o link, o título e só o texto que você selecionou. */
  protected readonly bookmarklet = inject(DomSanitizer).bypassSecurityTrustUrl(
    "javascript:(()=>{const s=String(getSelection()).slice(0,8000);window.open('http://127.0.0.1:7410/salvar?url='+encodeURIComponent(location.href)+'&title='+encodeURIComponent(document.title)+'&text='+encodeURIComponent(s),'_blank')})()");
  protected readonly list = query<Campaign[]>(() => '/campaigns');
  protected readonly form = signal({ mode: 'PrepareOnly', minFitScore: 85, maxSubmissionsPerDay: 15, technologies: '', locations: '', sources: '' });
  protected readonly saved = signal(false);
  protected readonly save = command((c: Campaign) => send('PUT', `/campaigns/${c.id}`, this.payload()));
  protected readonly run = command((c: Campaign) => send('POST', `/campaigns/${c.id}/runs`));
  protected readonly ago = ago;
  protected heroStats(c: Campaign): HeroStat[] {
    return [
      { label: 'Fontes', value: c.sources.length },
      { label: 'Tecnologias', value: c.technologies.length },
      { label: 'Nota de corte', value: c.minFitScore, tone: 'accent' },
      { label: 'Envios por dia', value: c.maxSubmissionsPerDay },
    ];
  }
  protected readonly modes = [
    { key: 'DiscoveryOnly', label: 'Só descobrir', hint: 'Busca e dá nota. Não prepara nada.' },
    { key: 'PrepareOnly', label: 'Só preparar', hint: 'Prepara currículo e formulário. Nunca envia.' },
    { key: 'ReviewRequired', label: 'Revisão sua', hint: 'Prepara e espera você aprovar o formulário exato.' },
    { key: 'AutoSubmit', label: 'Automático', hint: 'Exige autorização com escopo e limite, criada na fase 6.', locked: true },
  ];

  constructor() {
    effect(() => {
      const c = this.list.value()?.[0];
      if (c) this.form.set({ mode: c.mode, minFitScore: c.minFitScore, maxSubmissionsPerDay: c.maxSubmissionsPerDay, technologies: c.technologies.join(', '), locations: c.locations.join(', '), sources: c.sources.join('\n') });
    });
  }

  protected set(key: string, value: string | number) { this.form.update((f) => ({ ...f, [key]: value })); this.saved.set(false); }

  private payload() {
    const f = this.form();
    const split = (s: string, by: RegExp) => s.split(by).map((x) => x.trim()).filter(Boolean);
    return { mode: f.mode, minFitScore: Number(f.minFitScore), maxSubmissionsPerDay: Number(f.maxSubmissionsPerDay), technologies: split(f.technologies, /,/), locations: split(f.locations, /,/), sources: split(f.sources, /\n/) };
  }

  protected async submit(c: Campaign) {
    if ((await this.save.run(c)) !== null || !this.save.problem()) { this.saved.set(!this.save.problem()); await this.list.reload(); }
  }
}
