import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Icon } from '@jo/ui/icon';
import { Mark } from '@jo/ui/mark';
import { Overview } from './core/api';
import { query } from './core/query';
import { modeLabel } from './core/format';
import { Theme } from './core/theme';
import { Live } from './core/live';

interface NavItem { path: string; label: string; icon: string; exact?: boolean; count?: 'review' | 'attention'; }

/** Shell: barra lateral recolhível (lembrada neste navegador), contadores reais e o estado da API. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, Mark],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.collapsed]': 'collapsed()' },
})
export class App {
  protected readonly theme = inject(Theme);
  protected readonly live = inject(Live);
  protected readonly collapsed = signal(read('jo.sidebar') === 'collapsed');
  protected readonly overview = query<Overview>(() => '/overview', 5000);
  protected readonly online = computed(() => this.overview.problem() === null && this.overview.value() !== undefined);
  protected readonly mode = computed(() => modeLabel(this.overview.value()?.campaign?.mode ?? ''));
  protected readonly nav: { group: string; items: NavItem[] }[] = [
    { group: 'Operação', items: [
      { path: '/', label: 'Visão geral', icon: 'house', exact: true },
      { path: '/fluxo', label: 'Fluxo', icon: 'flow-arrow' },
      { path: '/atencao', label: 'Atenção', icon: 'bell-simple', count: 'attention' },
      { path: '/revisao', label: 'Revisão', icon: 'check-square-offset', count: 'review' },
    ] },
    { group: 'Dados', items: [
      { path: '/vagas', label: 'Vagas', icon: 'briefcase' },
      { path: '/candidaturas', label: 'Candidaturas', icon: 'paper-plane-tilt' },
    ] },
    { group: 'Sistema', items: [
      { path: '/adapters', label: 'Adapters', icon: 'plugs-connected' },
      { path: '/provedores', label: 'Provedores de IA', icon: 'cpu' },
      { path: '/campanha', label: 'Campanha', icon: 'sliders-horizontal' },
    ] },
  ];

  protected count(kind?: NavItem['count']): number {
    const d = this.overview.value();
    if (!d || !kind) return 0;
    return kind === 'review' ? d.applications.awaitingApproval : d.pendingTasks;
  }

  protected toggle() {
    this.collapsed.update((c) => !c);
    write('jo.sidebar', this.collapsed() ? 'collapsed' : 'open');
  }
}

function read(key: string): string | null { try { return localStorage.getItem(key); } catch { return null; } }
function write(key: string, value: string) { try { localStorage.setItem(key, value); } catch { /* armazenamento indisponível */ } }
