import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, inject, input, signal, viewChild } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { Icon } from '@jo/ui/icon';

export interface TabItem { path: string; label: string; icon: string; exact?: boolean }

/** Abas de uma área (ex.: Fontes): o destaque desliza até a aba atual, como na barra lateral. */
@Component({
  selector: 'jo-tabs',
  imports: [RouterLink, RouterLinkActive, Icon],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav #bar [attr.aria-label]="label()">
      <span class="glider" aria-hidden="true" [style.transform]="'translateX(' + glide().left + 'px)'" [style.width.px]="glide().width" [style.opacity]="glide().width ? 1 : 0"></span>
      @for (t of items(); track t.path) {
        <a [routerLink]="t.path" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: t.exact ?? true }"><jo-icon [name]="t.icon" [size]="15" />{{ t.label }}</a>
      }
    </nav>`,
  styles: `
    :host { display: block; }
    nav { position: relative; display: inline-flex; gap: 2px; padding: 3px; border-radius: 6px; border: 1px solid var(--line); background: var(--surface); max-width: 100%; overflow-x: auto; scrollbar-width: none; }
    .glider { position: absolute; top: 3px; bottom: 3px; left: 0; border-radius: 4px; background: var(--surface-3); transition: transform 360ms cubic-bezier(0.25, 1.1, 0.4, 1), width 300ms cubic-bezier(0.2, 0.8, 0.2, 1), opacity 200ms ease; }
    a { position: relative; z-index: 1; display: inline-flex; align-items: center; gap: 7px; height: 30px; padding: 0 12px; border-radius: 4px; color: var(--text-2); text-decoration: none; font: 500 12.5px var(--font-ui); white-space: nowrap; transition: color 200ms ease, transform 140ms ease; }
    a:hover { color: var(--text); }
    a:active { transform: scale(0.97); }
    a.active { color: var(--text); }
    a.active jo-icon { color: var(--accent); }
  `,
})
export class Tabs {
  readonly items = input.required<TabItem[]>();
  readonly label = input('Seções');
  private readonly bar = viewChild<ElementRef<HTMLElement>>('bar');
  protected readonly glide = signal({ left: 0, width: 0 });

  constructor() {
    const sub = inject(Router).events.subscribe((e) => { if (e instanceof NavigationEnd) requestAnimationFrame(() => this.measure()); });
    afterNextRender(() => this.measure());
    inject(DestroyRef).onDestroy(() => sub.unsubscribe());
  }

  private measure() {
    const a = this.bar()?.nativeElement.querySelector<HTMLElement>('a.active');
    this.glide.set(a ? { left: a.offsetLeft, width: a.offsetWidth } : { left: 0, width: 0 });
  }
}

export const SOURCE_TABS: TabItem[] = [
  { path: '/fontes', label: 'Na campanha', icon: 'list-checks' },
  { path: '/fontes/varredura', label: 'Varredura da internet', icon: 'globe' },
  { path: '/fontes/adapters', label: 'Conectores', icon: 'plugs-connected' },
  { path: '/fontes/registro', label: 'Registro', icon: 'shield-check' },
  { path: '/fontes/cobertura', label: 'Cobertura', icon: 'map-pin' },
];
