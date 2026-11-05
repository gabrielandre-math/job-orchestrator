import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Company } from '@jo/ui/company';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { Empty } from '@jo/ui/empty';
import { Score } from '@jo/ui/score';
import { ApplicationRow } from '../../core/api';
import { query } from '../../core/query';
import { ago, evidenceLabel, stateLabel, stateTone } from '../../core/format';

/** Candidaturas: cada vaga que entrou na fila, do currículo à confirmação. */
@Component({
  selector: 'app-applications',
  imports: [RouterLink, Badge, Empty, Score, Company, PageHero],
  templateUrl: './applications.page.html',
  styleUrl: './applications.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApplicationsPage {
  private readonly router = inject(Router);
  readonly state = input<string | undefined>();
  protected readonly all = query<ApplicationRow[]>(() => '/applications', 4000);
  protected readonly label = stateLabel;
  protected readonly tone = stateTone;
  protected readonly ago = ago;
  protected readonly evidence = evidenceLabel;
  protected readonly filters = [
    { key: undefined, label: 'Todas' }, { key: 'AwaitingApproval', label: 'Esperando você' }, { key: 'NeedsHuman', label: 'Precisa de você' },
    { key: 'FormValidated', label: 'Preparadas' }, { key: 'SubmittedConfirmed', label: 'Confirmadas' }, { key: 'SubmissionUnknown', label: 'Incertas' },
  ];
  protected readonly heroStats = computed<HeroStat[]>(() => {
    const all = this.all.value() ?? [];
    const by = (states: string[]) => all.filter((a) => states.includes(a.state)).length;
    return [
      { label: 'No total', value: all.length },
      { label: 'Em andamento', value: by(['Shortlisted', 'MaterialsReady', 'FormPreparing', 'FormValidated', 'ReadyToSubmit', 'Submitting', 'RetryScheduled']) },
      { label: 'Esperando você', value: by(['AwaitingApproval', 'NeedsHuman', 'ManualHandoff']), tone: by(['AwaitingApproval', 'NeedsHuman', 'ManualHandoff']) > 0 ? 'warn' : undefined },
      { label: 'Confirmadas', value: by(['SubmittedConfirmed', 'SubmittedDeclared']), tone: 'ok' },
      { label: 'Incertas', value: by(['SubmissionUnknown']), tone: by(['SubmissionUnknown']) > 0 ? 'warn' : undefined },
    ];
  });
  protected readonly rows = computed(() => (this.all.value() ?? []).filter((a) => !this.state() || a.state === this.state()));
  protected count(key: string | undefined) { return (this.all.value() ?? []).filter((a) => !key || a.state === key).length; }
  protected filter(key: string | undefined) { void this.router.navigate([], { queryParams: { state: key ?? null } }); }
}
