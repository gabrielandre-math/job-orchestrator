import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Empty } from '@jo/ui/empty';
import { Score } from '@jo/ui/score';
import { JobDetail, JobRow } from '../../core/api';
import { query } from '../../core/query';
import { ago, jobLabel, jobTone } from '../../core/format';

/** Vagas: tudo que as fontes trouxeram, com nota explicada, motivo de corte e duplicadas. */
@Component({
  selector: 'app-jobs',
  imports: [Badge, Empty, Score],
  templateUrl: './jobs.page.html',
  styleUrl: './jobs.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class JobsPage {
  private readonly router = inject(Router);
  readonly status = input<string | undefined>();
  protected readonly search = signal('');
  protected readonly open = signal<string | null>(null);
  protected readonly all = query<JobRow[]>(() => '/jobs', 8000);
  protected readonly detail = query<JobDetail>(() => (this.open() ? `/jobs/${this.open()}` : null));
  protected readonly label = jobLabel;
  protected readonly tone = jobTone;
  protected readonly ago = ago;
  protected readonly filters = [
    { key: undefined, label: 'Todas' }, { key: 'Shortlisted', label: 'Na fila' }, { key: 'Scored', label: 'Abaixo do corte' },
    { key: 'Rejected', label: 'Cortadas' }, { key: 'Duplicate', label: 'Duplicadas' }, { key: 'Expired', label: 'Encerradas' },
  ];

  protected readonly counts = computed(() => {
    const c: Record<string, number> = {};
    for (const j of this.all.value() ?? []) c[j.status] = (c[j.status] ?? 0) + 1;
    return c;
  });

  protected readonly rows = computed(() => {
    const s = this.search().trim().toLowerCase();
    return (this.all.value() ?? []).filter((j) => (!this.status() || j.status === this.status()) && (!s || `${j.company} ${j.title}`.toLowerCase().includes(s)));
  });

  protected filter(key: string | undefined) {
    void this.router.navigate([], { queryParams: { status: key ?? null }, queryParamsHandling: 'merge' });
  }

  protected count(key: string | undefined) { return key ? (this.counts()[key] ?? 0) : (this.all.value()?.length ?? 0); }
  protected source(s: string) { return s.replace(/^greenhouse:/, '').replace(/^https?:\/\//, ''); }
}
