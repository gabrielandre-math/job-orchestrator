import { inject, ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Copilot } from './copilot';
import { InterviewPanel } from './interview-panel';
import { Badge } from '@jo/ui/badge';
import { ApplicationDetail, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';
import { clock, evidenceLabel, stateLabel, stateTone } from '../../core/format';

/** Uma candidatura: linha do tempo, formulário exato, provas, pendências e as ações que cabem no estado atual. */
@Component({
  selector: 'app-application',
  imports: [RouterLink, Badge, Copilot, InterviewPanel],
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
  /** Arquivar: a candidatura encerrada some das telas e das contagens; o histórico fica, com a linha do arquivamento. */
  protected readonly archiving = command(() => send('POST', `/applications/${this.id()}/archive`), 'Arquivada: saiu das telas, e o histórico fica.');
  private readonly router = inject(Router);
  protected closed(state: string) { return ['Withdrawn', 'SubmittedConfirmed', 'SubmittedDeclared', 'Failed', 'DeadLetter', 'BlockedByPolicy'].includes(state); }
  protected async archive() { if ((await this.archiving.run(undefined)) !== null) void this.router.navigate(['/candidaturas']); }
  protected readonly confirming = signal(false);
  protected readonly declaring = command(() => send('POST', `/applications/${this.id()}/declare`, { note: 'Você confirmou que o envio chegou.' }), 'Registrada: o envio chegou.');
  protected async arrived2() { await this.data.reload(); }
  protected async arrived() { if ((await this.declaring.run(undefined)) !== null) await this.data.reload(); }
  /** Envio incerto que a reconciliação não achou: só você sabe se chegou (e-mail de confirmação, portal do candidato). */
  /** Uma parede que o robô não passa (CAPTCHA, login): o caminho é o copiloto no seu navegador, não refazer o preparo. */
  protected readonly wall = computed(() => (this.data.value()?.tasks ?? []).find((t) => !t.resolvedAt && (t.kind === 'Captcha' || t.kind === 'Login'))?.kind ?? null);
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
