import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Empty } from '@jo/ui/empty';
import { Overview, send, TaskRow } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';
import { ago, clock, modeLabel, plural, stateLabel, stateTone } from '../../core/format';

/** Visão geral: a última atividade com contagens do banco, o que precisa de você e a saúde dos adapters. */
@Component({
  selector: 'app-overview',
  imports: [RouterLink, Badge, Empty],
  templateUrl: './overview.page.html',
  styleUrl: './overview.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OverviewPage {
  protected readonly data = query<Overview>(() => '/overview', 4000);
  protected readonly tasks = query<TaskRow[]>(() => '/tasks', 4000);
  protected readonly run = command((id: string) => send('POST', `/campaigns/${id}/runs`));
  protected readonly label = stateLabel;
  protected readonly tone = stateTone;
  protected readonly ago = ago;
  protected readonly clock = clock;
  protected readonly mode = computed(() => modeLabel(this.data.value()?.campaign?.mode ?? ''));

  protected readonly cells = computed(() => {
    const d = this.data.value();
    if (!d) return [];
    const a = d.applications;
    return [
      { label: 'Vagas encontradas', value: d.jobs.total, note: `${plural(d.jobs.duplicates, 'duplicada')}, ${plural(d.jobs.rejected, 'cortada')}` },
      { label: 'Na fila', value: d.jobs.shortlisted, note: 'acima do corte' },
      { label: 'Preparadas', value: a.prepared, note: 'prontas, sem envio' },
      { label: 'Esperando você', value: a.awaitingApproval + a.needsHuman, note: `${plural(a.awaitingApproval, 'aprovação', 'aprovações')}, ${plural(a.needsHuman, 'pendência')}`, tone: a.awaitingApproval + a.needsHuman > 0 ? 'warn' : '' },
      { label: 'Confirmadas', value: a.confirmed, note: `${d.confirmedToday} hoje`, tone: a.confirmed > 0 ? 'ok' : '' },
      { label: 'Incertas', value: a.unknown, note: 'em reconciliação', tone: a.unknown > 0 ? 'warn' : '' },
      { label: 'Bloqueadas', value: a.blocked + a.failed, note: 'Guardião ou ATS', tone: a.blocked + a.failed > 0 ? 'danger' : '' },
    ];
  });

  protected async runNow(id: string) {
    await this.run.run(id);
    await this.data.reload();
  }
}
