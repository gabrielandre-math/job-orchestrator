import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { query } from '../../core/query';

interface Coverage { source: string; jobs: number; title: number; company: number; location: number; description: number; salary: number; applyEmail: number; }

const ORIGIN: Record<string, string> = { 'alertas:email': 'Alertas por e-mail', 'manual:linkedin.com': 'Captura pelo favorito' };

/**
 * LinkedIn, sem robô: o que entra por cada caminho e quanto cada campo vem preenchido de verdade. O LinkedIn proíbe automação;
 * vagas chegam pelos alertas e pelas páginas que você captura, e o perfil pelo export.
 */
@Component({
  selector: 'jo-linkedin',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel">
      <header><h2>LinkedIn</h2><span class="faint small">sessão local, alertas e captura</span></header>
      <div class="body">
        <ul class="modes">
          <li><strong>Perfil</strong><span class="muted">O export do LinkedIn (ZIP) traz experiências, formação, competências, certificados e idiomas, sem IA. <a routerLink="/perfil" [queryParams]="{ conectar: 'linkedin' }">Conectar</a></span></li>
          <li><strong>Busca pela conta</strong><span class="muted">A sessão local pode buscar por termos, ler detalhes e alimentar a campanha. Você faz login na janela separada. <a routerLink="/testes">Conectar e configurar busca</a></span></li>
          <li><strong>Vagas por alerta</strong><span class="muted">O alerta de vaga do LinkedIn chega no seu e-mail com título, empresa, local e o link da vaga. A descrição completa não vem no e-mail.</span></li>
          <li><strong>Vagas da sua aba</strong><span class="muted">Na extensão, clique em Ler esta página e confira a prévia. A captura traz cartões ou os detalhes abertos, atualizando a mesma vaga sem duplicar. <a routerLink="/testes">Preparar teste</a></span></li>
          <li><strong>Candidatura</strong><span class="muted">No LinkedIn, copie o currículo e as respostas do copiloto e envie pelo site. A extensão de preenchimento não executa nessa plataforma.</span></li>
          <li><strong>Recrutador</strong><span class="muted">Só quando a vaga publica um contato. O app não procura nem deduz e-mail de ninguém.</span></li>
          <li><strong>Laboratório</strong><span class="muted"><span class="mono">scripts/labs.sh</span> roda os extratores e o CAPTCHA contra páginas locais sintéticas.</span></li>
        </ul>
        @if (rows().length > 0) {
          <table class="cov">
            <thead><tr><th>Origem</th><th>Vagas</th><th>Título</th><th>Empresa</th><th>Local</th><th>Descrição</th><th>Salário</th><th>E-mail de candidatura</th></tr></thead>
            <tbody>
              @for (r of rows(); track r.source) {
                <tr><td>{{ origin(r.source) }}</td><td class="num">{{ r.jobs }}</td><td class="num">{{ r.title }}</td><td class="num">{{ r.company }}</td><td class="num">{{ r.location }}</td>
                  <td class="num">{{ r.description }}</td><td class="num">{{ r.salary }}</td><td class="num">{{ r.applyEmail }}</td></tr>
              }
            </tbody>
          </table>
          <p class="faint small note">Vagas com link do LinkedIn, por campo preenchido. Descrição conta a partir de 200 caracteres.</p>
        } @else {
          <p class="faint small note">Nenhuma vaga do LinkedIn ainda. Crie um alerta no LinkedIn ou capture uma vaga aberta.</p>
        }
      </div>
    </section>
  `,
  styles: `
    :host { display: block; }
    .small { font-size: 12px; }
    .body { display: grid; gap: 12px; padding: 12px 16px 14px; }
    .modes { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; }
    .modes li { display: grid; grid-template-columns: minmax(120px, 160px) minmax(0, 1fr); gap: 10px; font-size: 12.5px; line-height: 1.5; }
    .modes a { color: var(--accent); text-decoration: none; font-weight: 500; }
    .cov { width: 100%; border-collapse: collapse; font-size: 12px; }
    .cov th, .cov td { text-align: left; padding: 6px 8px; border-bottom: 1px solid var(--border); white-space: nowrap; }
    .cov th { font-weight: 500; color: var(--text-2); }
    .cov .num { font-variant-numeric: tabular-nums; }
    .note { margin: 0; }
    @media (max-width: 720px) { .modes li { grid-template-columns: 1fr; gap: 2px; } .cov { display: block; overflow-x: auto; } }
  `,
})
export class LinkedIn {
  protected readonly coverage = query<Coverage[]>(() => '/jobs/linkedin-coverage', 120000);
  protected readonly rows = computed(() => this.coverage.value() ?? []);
  protected origin(source: string) { return ORIGIN[source] ?? source; }
}
