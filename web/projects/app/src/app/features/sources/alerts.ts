import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { query } from '../../core/query';

interface AlertsView { count: number; connected: boolean; recent: { sender: string; title: string; company: string; location: string; url: string; receivedAt: string }[]; }

const SITES = [
  { name: 'LinkedIn', domain: 'linkedin.com', url: 'https://www.linkedin.com/jobs/', how: 'busque o cargo e ligue "Alerta de vaga"' },
  { name: 'Indeed', domain: 'indeed.com', url: 'https://br.indeed.com/', how: 'busque e escolha receber novas vagas por e-mail' },
  { name: 'Catho', domain: 'catho.com.br', url: 'https://www.catho.com.br/', how: 'salve a busca com aviso por e-mail' },
  { name: 'InfoJobs', domain: 'infojobs.com.br', url: 'https://www.infojobs.com.br/', how: 'crie o alerta na busca de vagas' },
  { name: 'Glassdoor', domain: 'glassdoor.com.br', url: 'https://www.glassdoor.com.br/', how: 'ligue o alerta de vagas da busca' },
  { name: 'Vagas.com', domain: 'vagas.com.br', url: 'https://www.vagas.com.br/', how: 'ative o aviso de vagas por e-mail' },
];

/**
 * Alertas de vaga por e-mail: o caminho para os sites que bloqueiam robô. A pessoa cria o alerta no site, o alerta chega na caixa dela e
 * o app tira as vagas do e-mail. Nenhum desses sites é aberto por robô.
 */
@Component({
  selector: 'jo-alerts',
  imports: [Company, Icon, RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel">
      <header><h2>Alertas de vaga por e-mail</h2><span class="faint small">LinkedIn, Indeed e outros sites que não aceitam robô</span></header>
      <div class="body">
        <p class="muted lead">Crie o alerta no site com as palavras do seu currículo. O alerta chega no seu e-mail e o app traz as vagas dele para a busca, com nota. Nenhum desses sites é aberto por robô; a candidatura segue pelo copiloto.</p>
        <ul class="sites">
          @for (s of sites; track s.name) {
            <li><jo-company [name]="s.name" [domain]="s.domain" [size]="22" /><span><a [href]="s.url" target="_blank" rel="noopener">{{ s.name }}</a><span class="faint small">{{ s.how }}</span></span></li>
          }
        </ul>
        <p class="state small">
          @if (!view()?.connected) { <jo-icon name="envelope-simple" [size]="14" />Conecte o seu e-mail para o app ler os alertas. <a routerLink="/email">Conectar e-mail</a> }
          @else { <jo-icon name="check" [size]="14" />{{ view()?.count ?? 0 }} {{ view()?.count === 1 ? 'vaga chegou' : 'vagas chegaram' }} por alertas nos últimos 14 dias. }
        </p>
      </div>
    </section>
  `,
  styles: `
    :host { display: block; }
    .small { font-size: 12px; }
    .body { display: grid; gap: 12px; padding: 12px 16px 14px; }
    .lead { margin: 0; font-size: 12.5px; line-height: 1.55; max-width: 92ch; }
    .sites { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fill, minmax(min(260px, 100%), 1fr)); gap: 8px; }
    .sites li { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 10px; align-items: center; padding: 8px 10px; border-radius: 6px; background: var(--surface-2); }
    .sites span { display: grid; min-width: 0; }
    .sites a, .state a { color: var(--accent); text-decoration: none; font-weight: 500; }
    .state { display: flex; align-items: center; gap: 8px; margin: 0; color: var(--text-2); }
  `,
})
export class Alerts {
  protected readonly sites = SITES;
  protected readonly alerts = query<AlertsView>(() => '/mail/alerts', 60000);
  protected readonly view = computed(() => this.alerts.value() ?? null);
}
