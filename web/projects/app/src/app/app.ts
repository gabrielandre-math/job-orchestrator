import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, computed, inject, signal, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Icon } from '@jo/ui/icon';
import { Mark } from '@jo/ui/mark';
import { Overview } from './core/api';
import { WelcomeHub } from './core/welcome-hub';
import { query } from './core/query';
import { modeLabel } from './core/format';
import { Theme } from './core/theme';
import { Live } from './core/live';
import { Toasts } from './core/toast';
import { SearchRun } from './core/search';
import { Welcome } from './features/welcome/welcome';
import { Quit } from './features/quit/quit';

interface NavItem { path: string; label: string; icon: string; exact?: boolean; count?: 'review' | 'attention'; }

/** Shell: barra lateral recolhível (lembrada neste navegador), contadores reais e o estado da API. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon, Mark, Welcome, Quit],
  templateUrl: './app.html',
  styleUrl: './app.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.collapsed]': 'collapsed()', '[class.drawer-open]': 'drawer()' },
})
export class App {
  protected readonly theme = inject(Theme);
  protected readonly live = inject(Live);
  protected readonly toasts = inject(Toasts);
  protected readonly search = inject(SearchRun);
  private readonly router = inject(Router);
  private readonly navRef = viewChild<ElementRef<HTMLElement>>('navEl');
  /** O destaque da página atual desliza de um item para o outro em vez de pular. */
  protected readonly glide = signal({ top: 0, height: 0 });
  protected readonly collapsed = signal(read('jo.sidebar') === 'collapsed');
  /** No celular a barra lateral é uma gaveta: abre pelo menu e fecha ao navegar. */
  protected readonly drawer = signal(false);
  /** O instalador ocupa a tela inteira: sem barra lateral, sem consultas ao app (que ainda não está instalado). */
  protected readonly bare = typeof location !== 'undefined' && location.pathname.startsWith('/instalar');
  protected readonly overview = query<Overview>(() => (this.bare ? null : '/overview'), 5000);
  protected readonly quitting = signal(false);
  protected readonly online = computed(() => this.overview.problem() === null && this.overview.value() !== undefined);
  protected readonly mode = computed(() => modeLabel(this.overview.value()?.campaign?.mode ?? ''));
  protected readonly nav: { group: string; items: NavItem[] }[] = [
    { group: 'Operação', items: [
      { path: '/', label: 'Visão geral', icon: 'house', exact: true },
      { path: '/assistente', label: 'Assistente', icon: 'chats' },
      { path: '/busca', label: 'Busca ao vivo', icon: 'magnifying-glass' },
      { path: '/atencao', label: 'Atenção', icon: 'bell-simple', count: 'attention' },
      { path: '/revisao', label: 'Revisão', icon: 'check-square-offset', count: 'review' },
    ] },
    { group: 'Dados', items: [
      { path: '/perfil', label: 'Perfil', icon: 'identification-card' },
      { path: '/vagas', label: 'Vagas', icon: 'briefcase' },
      { path: '/candidaturas', label: 'Candidaturas', icon: 'paper-plane-tilt' },
      { path: '/email', label: 'E-mail', icon: 'envelope-simple' },
    ] },
    { group: 'Sistema', items: [
      { path: '/fontes', label: 'Fontes', icon: 'plugs-connected' },
      { path: '/provedores', label: 'Provedores de IA', icon: 'cpu' },
      { path: '/campanha', label: 'Campanha', icon: 'sliders-horizontal' },
      { path: '/configuracoes', label: 'Configurações', icon: 'gear-six' },
    ] },
  ];

  /** Primeiro contato: abre sozinho enquanto o ambiente não foi montado; "Agora não" vale até recarregar a página. */
  protected readonly hub = inject(WelcomeHub);
  protected readonly welcome = this.hub.open;

  constructor() {
    if (!this.bare) void this.hub.refresh().then((o) => { if (o && !o.done && sessionStorageGet('jo.welcome') !== 'later') this.welcome.set(true); });
    const destroy = inject(DestroyRef);
    const sub = this.router.events.subscribe((e) => { if (e instanceof NavigationEnd) requestAnimationFrame(() => this.measure()); });
    const resize = () => this.measure();
    afterNextRender(() => { this.measure(); addEventListener('resize', resize); });
    destroy.onDestroy(() => { sub.unsubscribe(); removeEventListener('resize', resize); });
  }

  private measure() {
    const active = this.navRef()?.nativeElement.querySelector<HTMLElement>('a.active');
    this.glide.set(active ? { top: active.offsetTop, height: active.offsetHeight } : { top: 0, height: 0 });
  }

  protected doneSources() { return this.search.state()?.sources.filter((x) => x.state === 'done' || x.state === 'failed').length ?? 0; }

  protected count(kind?: NavItem['count']): number {
    const d = this.overview.value();
    if (!d || !kind) return 0;
    return kind === 'review' ? d.applications.awaitingApproval : d.pendingTasks;
  }

  protected closeWelcome() { this.welcome.set(false); sessionStorageSet('jo.welcome', 'later'); void this.hub.refresh(); }

  protected toggle() {
    this.collapsed.update((c) => !c);
    write('jo.sidebar', this.collapsed() ? 'collapsed' : 'open');
    setTimeout(() => this.measure(), 170);
  }
}

function sessionStorageGet(key: string): string | null { try { return sessionStorage.getItem(key); } catch { return null; } }
export function sessionStorageSet(key: string, value: string) { try { sessionStorage.setItem(key, value); } catch { /* sem armazenamento */ } }
function read(key: string): string | null { try { return localStorage.getItem(key); } catch { return null; } }
function write(key: string, value: string) { try { localStorage.setItem(key, value); } catch { /* armazenamento indisponível */ } }
