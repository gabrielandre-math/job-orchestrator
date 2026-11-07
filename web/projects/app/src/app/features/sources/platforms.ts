import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { query } from '../../core/query';

interface Platform { id: string; name: string; domains: string[]; prepare: boolean; submit: boolean; confirm: boolean; recover: boolean; }

/**
 * O que o app faz sozinho em cada plataforma, antes de você confiar a noite a ele: preencher, enviar, confirmar e recuperar um envio
 * interrompido. O resto da coleta vira kit para o copiloto, e o envio é seu.
 */
@Component({
  selector: 'jo-platforms',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel">
      <header><h2>Candidatura automática</h2><span class="faint small">o que o app faz sozinho em cada plataforma</span></header>
      <div class="body">
        <table class="matrix">
          <thead><tr><th>Plataforma</th><th>Sites</th><th>Preenche</th><th>Envia</th><th>Confirma</th><th>Recupera envio interrompido</th></tr></thead>
          <tbody>
            @for (p of rows(); track p.id) {
              <tr><td>{{ p.name.replace('Formulário ', '') }}</td><td class="mono small">{{ p.domains.length ? p.domains.join(', ') : 'sites sem login e sem CAPTCHA, fora dos agregadores' }}</td><td>{{ p.prepare ? 'sim' : 'não' }}</td><td>{{ p.submit ? 'sim' : 'não' }}</td>
                <td>{{ p.confirm ? 'recibo na página' : 'não' }}</td><td>{{ p.recover ? 'pergunta ao site' : 'você confere o e-mail de confirmação' }}</td></tr>
            }
            <tr><td>E-mail publicado na vaga</td><td class="faint small">sua conta</td><td>sim</td><td>sim</td><td>servidor aceitou</td><td>fica em dúvida e espera você</td></tr>
            <tr><td>Gupy, LinkedIn e outros sites</td><td class="faint small">no seu navegador</td><td>copiloto</td><td>você</td><td>você registra</td><td>não se aplica</td></tr>
          </tbody>
        </table>
        <p class="faint small note">Coletar uma vaga não quer dizer conseguir enviar sozinho. Cada candidatura mostra, na tela dela, qual destes caminhos vale.</p>
      </div>
    </section>
  `,
  styles: `
    :host { display: block; }
    .small { font-size: 12px; }
    .body { display: grid; gap: 10px; padding: 12px 16px 14px; }
    .matrix { width: 100%; border-collapse: collapse; font-size: 12.5px; }
    .matrix th, .matrix td { text-align: left; padding: 6px 8px; border-bottom: 1px solid var(--border); vertical-align: top; }
    .matrix th { font-weight: 500; color: var(--text-2); white-space: nowrap; }
    .note { margin: 0; }
    @media (max-width: 720px) { .matrix { display: block; overflow-x: auto; } }
  `,
})
export class Platforms {
  protected readonly platforms = query<Platform[]>(() => '/automation/platforms', 300000);
  protected readonly rows = computed(() => this.platforms.value() ?? []);
}
