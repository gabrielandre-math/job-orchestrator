import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { ChartRow, HBars } from '@jo/ui/charts';
import { Count } from '@jo/ui/count';
import { Empty } from '@jo/ui/empty';
import { Iso } from '@jo/ui/iso';
import { RouteHero, RouteStop } from '@jo/ui/route-hero';
import { Overview, Profile, send, TaskRow } from '../../core/api';
import { command } from '../../core/command';
import { ago, clock, modeLabel, plural, stateLabel, stateTone } from '../../core/format';
import { AGENT_ICON, Live, PROVIDER_LOGO } from '../../core/live';
import { query } from '../../core/query';

/**
 * Início: a marca como cenário (a rota com as estações dos agentes), a frase do dia, os números contando, o funil, os agentes
 * ao vivo e o que precisa de você. É a primeira tela, então diz em segundos se está tudo andando.
 */
@Component({
  selector: 'app-overview',
  imports: [RouterLink, Badge, Count, Empty, HBars, Iso, RouteHero],
  templateUrl: './overview.page.html',
  styleUrl: './overview.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OverviewPage {
  protected readonly live = inject(Live);
  protected readonly data = query<Overview>(() => '/overview', 4000);
  protected readonly tasks = query<TaskRow[]>(() => '/tasks', 4000);
  protected readonly profile = query<Profile>(() => '/profile', 0);
  protected readonly run = command((id: string) => send('POST', `/campaigns/${id}/runs`), 'Rodada pedida. As fontes começam a ser lidas.');
  protected readonly label = stateLabel;
  protected readonly tone = stateTone;
  protected readonly ago = ago;
  protected readonly clock = clock;
  protected readonly icons = AGENT_ICON;
  protected readonly logos = PROVIDER_LOGO;
  protected readonly mode = computed(() => modeLabel(this.data.value()?.campaign?.mode ?? ''));

  protected readonly greeting = computed(() => {
    const h = new Date().getHours();
    const part = h < 12 ? 'Bom dia' : h < 18 ? 'Boa tarde' : 'Boa noite';
    const name = (this.profile.value()?.identity.fullName ?? '').split(' ')[0];
    return name ? `${part}, ${name}.` : `${part}.`;
  });

  protected readonly waiting = computed(() => { const a = this.data.value()?.applications; return a ? a.awaitingApproval + a.needsHuman : 0; });
  protected readonly state = computed(() => this.live.busy() ? 'Agentes trabalhando agora' : this.waiting() > 0 ? plural(this.waiting(), 'coisa espera por você', 'coisas esperam por você') : 'Tudo em ordem');

  /** A frase do dia: o que andou e o que espera por você, sem enfeite. */
  protected readonly summary = computed(() => {
    const d = this.data.value();
    if (!d) return '';
    const waiting = d.applications.awaitingApproval + d.applications.needsHuman;
    const queue = plural(d.jobs.shortlisted, 'vaga na fila', 'vagas na fila');
    const prepared = plural(d.applications.prepared, 'candidatura preparada', 'candidaturas preparadas');
    const you = waiting === 0 ? 'nada esperando por você' : plural(waiting, 'coisa esperando por você', 'coisas esperando por você');
    return `${queue}, ${prepared} e ${you}.`;
  });

  /** Estações da rota: acende a que trabalhou por último (ao vivo). */
  protected readonly stops = computed<RouteStop[]>(() => {
    const last = this.live.feed().at(-1)?.message.agent ?? '';
    const on = (names: string[]) => this.live.busy() && names.includes(last);
    return [
      { at: 0.06, icon: 'radar', label: 'Batedor', active: on(['Batedor']) },
      { at: 0.3, icon: 'chart', label: 'Analista', active: on(['Analista']) },
      { at: 0.49, icon: 'writer', label: 'Redator', active: on(['Seletor', 'Redator', 'Conferente', 'Revisor', 'Renderizador', 'Inspetor ATS']) },
      { at: 0.72, icon: 'guardian', label: 'Guardião', active: on(['Guardião']) },
      { at: 0.94, icon: 'send', label: 'Envio', active: on(['Envio', 'Verificador']) },
    ];
  });

  protected readonly cells = computed(() => {
    const d = this.data.value();
    if (!d) return [];
    const a = d.applications;
    return [
      { label: 'Vagas encontradas', value: d.jobs.total, note: `${plural(d.jobs.duplicates, 'duplicada')}, ${plural(d.jobs.rejected, 'cortada')}`, tone: '' },
      { label: 'Na fila', value: d.jobs.shortlisted, note: 'acima do corte', tone: '' },
      { label: 'Preparadas', value: a.prepared, note: 'prontas, sem envio', tone: '' },
      { label: 'Esperando você', value: a.awaitingApproval + a.needsHuman, note: `${plural(a.awaitingApproval, 'aprovação', 'aprovações')}, ${plural(a.needsHuman, 'pendência')}`, tone: a.awaitingApproval + a.needsHuman > 0 ? 'warn' : '' },
      { label: 'Confirmadas', value: a.confirmed, note: `${d.confirmedToday} hoje`, tone: a.confirmed > 0 ? 'ok' : '' },
      { label: 'Incertas', value: a.unknown, note: 'em reconciliação', tone: a.unknown > 0 ? 'warn' : '' },
    ];
  });

  protected readonly funnel = computed<ChartRow[]>(() => {
    const d = this.data.value();
    if (!d) return [];
    const a = d.applications;
    return [
      { label: 'Encontradas', value: d.jobs.total, tone: 'muted' },
      { label: 'Na fila', value: d.jobs.shortlisted },
      { label: 'Preparadas', value: a.prepared },
      { label: 'Esperando você', value: a.awaitingApproval + a.needsHuman, tone: 'warn' },
      { label: 'Confirmadas', value: a.confirmed, tone: 'ok' },
    ];
  });

  /** As últimas falas dos agentes, ao vivo. */
  protected readonly voices = computed(() => this.live.feed().slice(-5).reverse());

  protected async runNow(id: string) {
    await this.run.run(id);
    await this.data.reload();
  }
}
