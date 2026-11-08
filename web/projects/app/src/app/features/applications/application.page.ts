import { inject, ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormQuestions } from '../../core/form-questions';
import { Copilot } from './copilot';
import { InterviewPanel } from './interview-panel';
import { Badge } from '@jo/ui/badge';
import { ApplicationDetail, ApplyCapability, send } from '../../core/api';
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
  protected readonly questions = inject(FormQuestions);
  protected readonly hasQuestions = computed(() => (this.questions.data.value() ?? []).some(q => q.applications.includes(this.id())));
  protected readonly data = query<ApplicationDetail>(() => `/applications/${this.id()}`, 3000);
  protected readonly withdraw = command(() => send('POST', `/applications/${this.id()}/withdraw`));
  protected readonly retry = command(() => send('POST', `/applications/${this.id()}/retry`));
  /** Arquivar: a candidatura encerrada some das telas e das contagens; o histórico fica, com a linha do arquivamento. */
  protected readonly archiving = command(() => send('POST', `/applications/${this.id()}/archive`), 'Arquivada: saiu das telas, e o histórico fica.');
  private readonly router = inject(Router);
  protected closed(state: string) { return ['Withdrawn', 'SubmittedConfirmed', 'SubmittedDeclared', 'Failed', 'DeadLetter', 'BlockedByPolicy'].includes(state); }
  protected async archive() { if ((await this.archiving.run(undefined)) !== null) void this.router.navigate(['/candidaturas']); }
  protected async arrived2() { await this.data.reload(); }
  /** Envio incerto que a reconciliação não achou: só você sabe se chegou (e-mail de confirmação, portal do candidato). */
  /** O que o app faz nesta vaga, antes de você confiar a noite a ele. */
  /** How this job is sent: always by you; the app prepares, and the copilot fills when the site has a form. */
  protected capability(c: ApplyCapability) {
    if (c.channel === 'email') return 'Esta vaga pede o currículo por e-mail: a IA escreve o e-mail a partir do seu currículo e você aprova o envio pela sua conta.';
    if (c.channel === 'ats') return `O app lê o ${(c.platform ?? 'formulário').replace('Formulário', 'formulário')} e prepara as respostas; o copiloto preenche no seu navegador e você clica em enviar.`;
    return 'O envio é pelo site da vaga: o copiloto preenche o que achar, o kit abaixo tem o resto para copiar, e você clica em enviar.';
  }
  /** Uma parede que o robô não passa (CAPTCHA, login): o caminho é o copiloto no seu navegador, não refazer o preparo. */
  protected readonly wall = computed(() => (this.data.value()?.tasks ?? []).find((t) => !t.resolvedAt && (t.kind === 'Captcha' || t.kind === 'Login'))?.kind ?? null);
  protected readonly label = stateLabel;
  protected readonly tone = stateTone;
  protected readonly clock = clock;
  protected readonly evidence = evidenceLabel;
  protected readonly history = computed(() => [...(this.data.value()?.history ?? [])].reverse());
  protected readonly pending = computed(() => (this.data.value()?.tasks ?? []).filter((t) => !t.resolvedAt));


  protected async doWithdraw() { await this.withdraw.run(undefined); await this.data.reload(); }
  protected async doRetry() { await this.retry.run(undefined); await this.data.reload(); }
  protected day(iso: string) { return new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' }); }
}
