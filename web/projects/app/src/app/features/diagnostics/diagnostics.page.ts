import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { SETTINGS_TABS, Tabs } from '../../shared/tabs';
import { DatePipe } from '@angular/common';
import { Icon } from '@jo/ui/icon';
import { PageHero } from '@jo/ui/page-hero';
import { query } from '../../core/query';

export interface TrailEntry { id: number; at: string; kind: string; level: 'info' | 'warn' | 'error'; name: string; detail: string | null; ms: number | null; code: string | null; trace: string | null; data: Record<string, string | null> | null; }

const KINDS: { id: string; label: string }[] = [
  { id: '', label: 'Tudo' }, { id: 'ui', label: 'Tela' }, { id: 'http', label: 'API' }, { id: 'command,query', label: 'Comandos' }, { id: 'step', label: 'Fila' },
  { id: 'form', label: 'Formulários' }, { id: 'state', label: 'Estados' }, { id: 'ai', label: 'IA' }, { id: 'log,error', label: 'Logs e erros' },
];

/** Diagnóstico: the trail of the app, newest first, to see what happened before something went wrong. It stays on this machine. */
@Component({
  selector: 'app-diagnostics',
  imports: [Tabs, DatePipe, Icon, PageHero],
  templateUrl: './diagnostics.page.html',
  styleUrl: './diagnostics.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DiagnosticsPage {
  protected readonly tabs = SETTINGS_TABS;
  protected readonly kinds = KINDS;
  protected readonly kind = signal('');
  protected readonly level = signal('');
  protected readonly text = signal('');
  protected readonly trace = signal('');
  protected readonly paused = signal(false);
  protected readonly open = signal<number | null>(null);
  protected readonly entries = query<TrailEntry[]>(() => this.paused() ? null : `/diagnostics/trail?limit=400${this.kind() ? '&kind=' + encodeURIComponent(this.kind()) : ''}${this.level() ? '&level=' + this.level() : ''}${this.text().trim() ? '&q=' + encodeURIComponent(this.text().trim()) : ''}${this.trace() ? '&trace=' + encodeURIComponent(this.trace()) : ''}`, 3000);
  protected readonly days = query<{ day: string; bytes: number }[]>(() => '/diagnostics/days', 60000);
  protected readonly counts = computed(() => {
    const list = this.entries.value() ?? [];
    return { errors: list.filter((e) => e.level === 'error').length, warnings: list.filter((e) => e.level === 'warn').length, total: list.length };
  });

  protected toggle(id: number) { this.open.update((o) => (o === id ? null : id)); }
  protected follow(trace: string | null) { if (trace) { this.trace.set(trace); this.open.set(null); } }
  protected size(bytes: number) { return bytes > 1048576 ? `${(bytes / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`; }
  protected pairs(data: Record<string, string | null> | null) { return Object.entries(data ?? {}).filter(([, v]) => !!v) as [string, string][]; }
}
