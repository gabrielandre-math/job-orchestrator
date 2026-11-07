import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Mark } from '@jo/ui/mark';
import { get, send } from '../../core/api';
import { Wizard } from '../../shared/wizard';
import { TermsDialog } from '../terms/terms';

interface Option { label: string; hint: string; }
interface Info { version: string; os: 'linux' | 'macos' | 'windows'; osLabel: string; source: string; target: string; installed: boolean; devData: boolean; running: boolean; done: boolean; options: { menu: Option; autostart: Option; scan: string }; }
interface Check { id: string; label: string; icon: string; state: 'ok' | 'warn' | 'fail'; detail: string; fix: string | null; blocking: boolean; }
interface Step { id: string; label: string; icon: string; state: 'waiting' | 'working' | 'done' | 'failed'; detail: string; }
interface State { running: boolean; done: boolean; error: string | null; steps: Step[]; log: string[]; }

/**
 * O instalador do executável: a mesma identidade do app, em cinco passos. Boas-vindas, a conferência da máquina (com o comando de cada
 * correção para copiar), como instalar, a esteira da instalação ao vivo e o fim, quando esta mesma janela vira o app.
 */
@Component({
  selector: 'app-install',
  imports: [Icon, Iso, Mark, Wizard, TermsDialog],
  templateUrl: './install.page.html',
  styleUrl: './install.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class InstallPage {
  protected readonly steps = ['Boas-vindas', 'Termos', 'Sua máquina', 'Como instalar', 'Instalando', 'Pronto'];
  protected readonly step = signal(1);
  protected readonly info = signal<Info | null>(null);
  protected readonly checks = signal<Check[] | null>(null);
  protected readonly checking = signal(false);
  protected readonly state = signal<State | null>(null);
  protected readonly menu = signal(true);
  protected readonly autostart = signal(true);
  protected readonly migrate = signal(true);
  protected readonly opening = signal(false);
  protected readonly copied = signal<string | null>(null);
  private timer?: ReturnType<typeof setInterval>;

  protected readonly blocked = computed(() => (this.checks() ?? []).some((c) => c.blocking && c.state === 'fail'));
  protected readonly warnings = computed(() => (this.checks() ?? []).filter((c) => c.state !== 'ok').length);
  protected readonly progress = computed(() => {
    const s = this.state()?.steps ?? [];
    return s.length ? s.filter((x) => x.state === 'done').length / s.length : 0;
  });
  protected readonly current = computed(() => this.state()?.steps.find((s) => s.state === 'working') ?? null);

  private readonly router = inject(Router);

  constructor() {
    // The installer page only exists while the installer runs (job-orchestrator --setup). With the app already installed and running,
    // the address leads to the panel instead of a wizard whose calls have nowhere to go.
    void fetch('/api/setup/info', { headers: { accept: 'application/json' } }).then(async (r) => {
      if (r.ok) this.info.set(await r.json() as Info);
      else if (r.status === 404) void this.router.navigateByUrl('/');
    }).catch(() => undefined);
    inject(DestroyRef).onDestroy(() => clearInterval(this.timer));
  }

  protected async next() {
    const s = this.step();
    if (s === 1) { this.step.set(2); return; }
    if (s === 3) { this.step.set(4); return; }
    if (s === 4) { await this.install(); }
  }

  /** Termos aceitos: segue para a conferência da máquina; recusados, o instalador fecha sem instalar nada. */
  protected async terms(accepted: boolean) {
    if (!accepted) { await this.quit(); return; }
    this.step.set(3);
    await this.check();
  }

  protected async check() {
    this.checking.set(true);
    this.checks.set(null);
    try { this.checks.set(await get<Check[]>('/setup/checks')); } catch { this.checks.set([]); }
    this.checking.set(false);
  }

  protected async install() {
    this.step.set(5);
    try { await send('POST', '/setup/install', { menu: this.menu(), autostart: this.autostart(), migrate: this.migrate() && !!this.info()?.devData }); } catch { /* já rodando: a esteira mostra */ }
    clearInterval(this.timer);
    this.timer = setInterval(async () => {
      try {
        const s = await get<State>('/setup/state');
        this.state.set(s);
        if (!s.running && (s.done || s.error)) { clearInterval(this.timer); if (s.done) setTimeout(() => this.step.set(6), 900); }
      } catch { /* o instalador volta já */ }
    }, 700);
  }

  /** O app instalado sobe em segundo plano; quando responde, esta janela vira o app (e o primeiro contato começa, se for a primeira vez). */
  protected async open() {
    this.opening.set(true);
    try { await send('POST', '/setup/launch'); } catch { /* segue esperando */ }
    const wait = setInterval(async () => {
      try {
        const r = await get<{ up: boolean }>('/setup/app-up');
        if (r.up) { clearInterval(wait); location.replace('http://127.0.0.1:7410/'); }
      } catch { /* ainda subindo */ }
    }, 800);
  }

  protected async quit() {
    try { await send('POST', '/setup/quit'); } catch { /* já saiu */ }
    window.close();
  }

  protected async copy(text: string) {
    try { await navigator.clipboard.writeText(text); this.copied.set(text); setTimeout(() => this.copied.set(null), 1600); } catch { /* sem área de transferência */ }
  }

  protected stateLabel(s: Check['state']) { return ({ ok: 'Pronto', warn: 'Aviso', fail: 'Falta' } as const)[s]; }
}
