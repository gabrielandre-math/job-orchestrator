import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Copilot } from './copilot';
import { Badge } from '@jo/ui/badge';
import { ApplicationDetail, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';
import { clock, evidenceLabel, stateLabel, stateTone } from '../../core/format';

/** Uma candidatura: linha do tempo, formulário exato, provas, pendências e as ações que cabem no estado atual. */
@Component({
  selector: 'app-application',
  imports: [RouterLink, Badge, Copilot],
  templateUrl: './application.page.html',
  styleUrl: './application.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ApplicationPage {
  readonly id = input.required<string>();
  protected readonly data = query<ApplicationDetail>(() => `/applications/${this.id()}`, 3000);
  protected readonly approve = command((hash: string) => send('POST', `/applications/${this.id()}/approve`, { payloadHash: hash }));
  protected readonly withdraw = command(() => send('POST', `/applications/${this.id()}/withdraw`));
  protected readonly retry = command(() => send('POST', `/applications/${this.id()}/retry`));
  protected readonly confirming = signal(false);
  protected readonly declaring = command(() => send('POST', `/applications/${this.id()}/declare`, { note: 'Você confirmou que o envio chegou.' }), 'Registrada: o envio chegou.');
  protected async arrived2() { await this.data.reload(); }
  protected async arrived() { if ((await this.declaring.run(undefined)) !== null) await this.data.reload(); }
  /** Envio incerto que a reconciliação não achou: só você sabe se chegou (e-mail de confirmação, portal do candidato). */
  protected readonly uncertain = computed(() => (this.data.value()?.tasks ?? []).some((t) => !t.resolvedAt && t.kind === 'Reconcile'));
  protected readonly label = stateLabel;
  protected readonly tone = stateTone;
  protected readonly clock = clock;
  protected readonly evidence = evidenceLabel;
  protected readonly history = computed(() => [...(this.data.value()?.history ?? [])].reverse());
  protected readonly pending = computed(() => (this.data.value()?.tasks ?? []).filter((t) => !t.resolvedAt));

  protected async doApprove(hash: string) {
    if (!this.confirming()) { this.confirming.set(true); setTimeout(() => this.confirming.set(false), 5000); return; }
    this.confirming.set(false);
    await this.approve.run(hash);
    await this.data.reload();
  }

  protected async doWithdraw() { await this.withdraw.run(undefined); await this.data.reload(); }
  protected async doRetry() { await this.retry.run(undefined); await this.data.reload(); }
  protected day(iso: string) { return new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' }); }
}
