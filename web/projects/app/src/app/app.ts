import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Overview } from './core/api';
import { query } from './core/query';
import { modeLabel } from './core/format';
import { Theme } from './core/theme';

/** Shell: navegação só com texto, contadores reais e o estado da API. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  protected readonly theme = inject(Theme);
  protected readonly overview = query<Overview>(() => '/overview', 5000);
  protected readonly online = computed(() => this.overview.problem() === null && this.overview.value() !== undefined);
  protected readonly review = computed(() => this.overview.value()?.applications.awaitingApproval ?? 0);
  protected readonly attention = computed(() => this.overview.value()?.pendingTasks ?? 0);
  protected readonly mode = computed(() => modeLabel(this.overview.value()?.campaign?.mode ?? ''));
  protected readonly nav = [
    { group: 'Operação', items: [
      { path: '/', label: 'Visão geral', exact: true },
      { path: '/fluxo', label: 'Fluxo' },
      { path: '/atencao', label: 'Atenção', count: 'attention' },
      { path: '/revisao', label: 'Revisão', count: 'review' },
    ] },
    { group: 'Dados', items: [
      { path: '/vagas', label: 'Vagas' },
      { path: '/candidaturas', label: 'Candidaturas' },
    ] },
    { group: 'Sistema', items: [
      { path: '/adapters', label: 'Adapters' },
      { path: '/campanha', label: 'Campanha' },
    ] },
  ];

  protected count(kind?: string): number {
    return kind === 'review' ? this.review() : kind === 'attention' ? this.attention() : 0;
  }
}
