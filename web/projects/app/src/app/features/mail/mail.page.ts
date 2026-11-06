import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { IncomingMail, MailAccountView, MailOverview, MailSettings, MailTarget, MailTemplate, OutgoingMail, send } from '../../core/api';
import { command } from '../../core/command';
import { ago } from '../../core/format';
import { query } from '../../core/query';
import { Help } from '../../shared/help';
import { MailConnect } from './mail-connect';

type Tab = 'targets' | 'review' | 'sent' | 'inbox' | 'template';
const STATUS: Record<string, { label: string; tone: string }> = {
  Draft: { label: 'rascunho', tone: 'muted' }, AwaitingApproval: { label: 'esperando você', tone: 'warn' }, Scheduled: { label: 'agendado', tone: 'accent' },
  Sending: { label: 'enviando', tone: 'accent' }, Sent: { label: 'enviado', tone: 'ok' }, Replied: { label: 'respondido', tone: 'ok' }, Failed: { label: 'não saiu', tone: 'bad' }, Bounced: { label: 'não entregue', tone: 'bad' }, Cancelled: { label: 'cancelado', tone: 'muted' },
};
const CLASS: Record<string, string> = {
  application_received: 'candidatura recebida', rejection: 'recusa', interview_invite: 'convite para entrevista', assessment: 'teste técnico', offer: 'proposta', recruiter_outreach: 'recrutador', bounce: 'não entregue', other: 'outro',
};
const MARKERS = ['{{empresa}}', '{{vaga}}', '{{experiencia}}', '{{competencias}}', '{{link_vaga}}', '{{nome}}', '{{assinatura}}'];

/**
 * E-mail: conectar a conta (senha de app), as vagas que publicam e-mail de candidatura, o rascunho da IA para aprovar (com o anexo e o que o
 * Conferente tirou), o que foi enviado com o lembrete, as respostas lidas da caixa e o esqueleto do e-mail com os marcadores.
 */
@Component({
  selector: 'app-mail',
  imports: [Company, Icon, Iso, MailConnect, PageHero, Help, RouterLink],
  templateUrl: './mail.page.html',
  styleUrl: './mail.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailPage {
  protected readonly overview = query<MailOverview>(() => '/mail/overview', 8000);
  protected readonly accounts = query<MailAccountView[]>(() => '/mail/accounts', 0);
  protected readonly settings = query<MailSettings>(() => '/mail/settings', 0);
  protected readonly targets = query<MailTarget[]>(() => '/mail/targets', 15000);
  protected readonly outgoing = query<OutgoingMail[]>(() => '/mail/outgoing', 6000);
  protected readonly inbox = query<IncomingMail[]>(() => '/mail/inbox', 30000);
  protected readonly templates = query<MailTemplate[]>(() => '/mail/templates', 0);
  protected readonly tab = signal<Tab>('targets');
  protected readonly wizard = signal<number | null>(null);
  protected readonly edits = signal<Record<string, { subject: string; body: string }>>({});
  protected readonly confirming = signal<string | null>(null);
  protected readonly tplEdits = signal<Record<string, { subject: string; body: string }>>({});
  protected readonly ago = ago;
  protected readonly markers = MARKERS;
  protected readonly classLabel = CLASS;
  protected readonly drafting = command((jobId: string) => send<OutgoingMail>('POST', '/mail/drafts', { jobId }), 'Rascunho pronto em Para aprovar.');
  protected readonly approving = command((id: string) => send<OutgoingMail>('POST', `/mail/outgoing/${id}/approve`), (m) => m.status === 'Sent' ? 'E-mail enviado.' : `Agendado: ${m.error ?? 'sai na próxima janela.'}`);
  protected readonly cancelling = command((id: string) => send('POST', `/mail/outgoing/${id}/cancel`), 'E-mail cancelado.');
  protected readonly editing = command((x: { id: string; subject: string; body: string }) => send('PUT', `/mail/outgoing/${x.id}`, { subject: x.subject, body: x.body }), 'Edição salva.');
  protected readonly checking = command((_: void) => send<{ stored: number }>('POST', '/mail/inbox/check'), (r) => r.stored ? `${r.stored} respostas novas.` : 'Nada novo na caixa.');
  protected readonly savingTpl = command((x: { id: string; subject: string; body: string }) => send('PUT', `/mail/templates/${x.id}`, { subject: x.subject, body: x.body }), 'Modelo salvo.');
  protected readonly removing = command((id: string) => send('DELETE', `/mail/accounts/${id}`), 'Conta desconectada e senha apagada do chaveiro.');

  protected readonly connected = computed(() => (this.accounts.value() ?? []).length > 0);
  protected readonly toWrite = computed(() => (this.targets.value() ?? []).filter((t) => !t.mailStatus));
  protected readonly toReview = computed(() => (this.outgoing.value() ?? []).filter((m) => m.status === 'AwaitingApproval' || m.status === 'Draft' || m.status === 'Scheduled'));
  protected readonly done = computed(() => (this.outgoing.value() ?? []).filter((m) => ['Sent', 'Replied', 'Failed', 'Bounced', 'Cancelled', 'Sending'].includes(m.status)));
  /** Avisos de falha na entrega ficam no e-mail enviado (como não entregue), não entre as respostas. */
  protected readonly replies = computed(() => (this.inbox.value() ?? []).filter((i) => i.class !== 'bounce'));
  /** Não entregues e os que não saíram: o número que pede atenção no topo e na aba. */
  protected readonly lost = computed(() => (this.outgoing.value() ?? []).filter((m) => m.status === 'Bounced' || m.status === 'Failed').length);
  protected readonly tabs = computed(() => [
    { key: 'targets' as Tab, label: 'Para escrever', n: this.toWrite().length, alert: false }, { key: 'review' as Tab, label: 'Para aprovar', n: this.toReview().length, alert: this.toReview().some((m) => m.status !== 'Scheduled') },
    { key: 'sent' as Tab, label: 'Enviados', n: this.done().length, alert: this.lost() > 0 }, { key: 'inbox' as Tab, label: 'Respostas', n: this.replies().length, alert: this.replies().some((i) => i.actionNeeded) },
    { key: 'template' as Tab, label: 'Modelo', n: null, alert: false },
  ]);
  protected readonly stats = computed<HeroStat[]>(() => {
    const o = this.overview.value();
    return [
      { label: 'Enviados hoje', value: o?.sentToday ?? 0, note: o ? `de ${o.dailyCap} por dia` : undefined, tone: 'accent' },
      { label: 'Esperando você', value: o?.awaiting ?? 0, tone: (o?.awaiting ?? 0) > 0 ? 'warn' : undefined },
      { label: 'Não entregues', value: this.lost(), tone: this.lost() > 0 ? 'danger' : undefined, note: this.lost() > 0 ? 'veja em Enviados' : undefined },
      { label: 'Respostas', value: o?.replied ?? 0, tone: 'ok' },
      { label: 'Entrevistas e testes', value: o?.interviews ?? 0, tone: (o?.interviews ?? 0) > 0 ? 'ok' : undefined },
    ];
  });
  protected readonly modeText = computed(() => {
    const s = this.settings.value();
    if (!s) return '';
    return s.mode === 'Auto' ? `Automático desde ${s.autoConsentAt ? new Date(s.autoConsentAt).toLocaleDateString('pt-BR') : ''}: até ${s.dailyCap} por dia, ${hours(s)}, lembrete em ${s.followUpDays} dias.`
      : s.mode === 'Review' ? `Revisão sua: cada e-mail espera o seu ok. Até ${s.dailyCap} por dia, ${hours(s)}.` : 'Só rascunho: nada sai daqui.';
  });

  protected status(s: string) { return STATUS[s] ?? { label: s, tone: 'muted' }; }

  /** As etapas de um e-mail: escrito, enviado e a resposta (ou onde parou). */
  protected track(m: OutgoingMail): { label: string; state: Step; at: string | null }[] {
    const steps: { label: string; state: Step; at: string | null }[] = [{ label: m.kind === 'followup' ? 'Lembrete escrito' : 'Escrito', state: 'done', at: short(m.createdAt) }];
    if (m.status === 'Cancelled') return [...steps, { label: 'Cancelado', state: 'off', at: null }];
    if (m.status === 'Failed') return [...steps, { label: 'Não saiu', state: 'bad', at: null }];
    if (m.status === 'Sending') return [...steps, { label: 'Enviando', state: 'now', at: null }];
    steps.push({ label: 'Enviado', state: 'done', at: short(m.sentAt) });
    if (m.status === 'Bounced') return [...steps, { label: 'Não chegou', state: 'bad', at: null }];
    if (m.status === 'Replied') return [...steps, { label: m.replyClass && CLASS[m.replyClass] ? upper(CLASS[m.replyClass]) : 'Respondido', state: 'done', at: short(m.repliedAt) }];
    return [...steps, { label: 'Resposta', state: 'wait', at: null }];
  }

  /** O que deu errado, em português, e o que fazer. O aviso original do servidor fica em "ver detalhes". */
  protected problem(m: OutgoingMail) {
    if (m.status !== 'Bounced' && m.status !== 'Failed') return null;
    const raw = m.error ?? '';
    if (m.status === 'Failed') return { title: 'Não saiu.', why: raw || 'O envio foi barrado antes de sair.', next: 'Nada foi enviado para a empresa.', raw: null };
    const why = /block|bloque|policy|spam/i.test(raw) ? 'O servidor da empresa bloqueou a mensagem.'
      : /not found|does not exist|no such user|user unknown|invalid recipient|inexist|5\.1\.1/i.test(raw) ? 'Esse endereço não existe mais.'
      : /mailbox full|quota|cheia/i.test(raw) ? 'A caixa de entrada da empresa está cheia.' : 'O servidor da empresa recusou a mensagem.';
    return { title: 'Não chegou na empresa.', why, next: 'Por e-mail não vai dar para esta vaga: se ela tiver outro caminho, candidate-se por lá.', raw: raw.replace(/^Não chegou na empresa:\s*/, '') };
  }

  /** O próximo passo de um e-mail que saiu: quando vem o lembrete, ou que a resposta chegou. */
  protected next(m: OutgoingMail): string | null {
    if (m.status === 'Replied') return 'A empresa respondeu: a mensagem está na aba Respostas.';
    if (m.status !== 'Sent' || !m.sentAt) return null;
    if (m.kind === 'followup') return 'Lembrete enviado. Não sai mais nada para esta vaga.';
    const f = (this.outgoing.value() ?? []).find((o) => o.followsId === m.id);
    if (f) return f.status === 'Sent' ? 'Lembrete enviado. Não sai mais nada para esta vaga.' : f.status === 'Cancelled' ? 'Lembrete cancelado.' : 'Lembrete pronto, na fila.';
    const at = new Date(Date.parse(m.sentAt) + (this.settings.value()?.followUpDays ?? 5) * 86_400_000);
    return `Sem resposta ainda. Se continuar assim, o lembrete sai em ${at.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })}.`;
  }
  protected edit(m: OutgoingMail) { return this.edits()[m.id] ?? { subject: m.subject, body: m.body }; }
  protected setEdit(m: OutgoingMail, k: 'subject' | 'body', v: string) { this.edits.update((e) => ({ ...e, [m.id]: { ...this.edit(m), [k]: v } })); }
  protected changed(m: OutgoingMail) { const e = this.edits()[m.id]; return !!e && (e.subject !== m.subject || e.body !== m.body); }
  protected notes(m: OutgoingMail) { return (m.checkNotes ?? '').split('\n').filter((x) => x.trim()); }

  protected async draft(t: MailTarget) { if ((await this.drafting.run(t.jobId)) !== null) { await Promise.all([this.outgoing.reload(), this.targets.reload(), this.overview.reload()]); this.tab.set('review'); } }
  protected async approve(m: OutgoingMail) {
    if (this.confirming() !== m.id) { this.confirming.set(m.id); setTimeout(() => this.confirming.update((c) => (c === m.id ? null : c)), 5000); return; }
    this.confirming.set(null);
    if (this.changed(m)) await this.saveEdit(m);
    if ((await this.approving.run(m.id)) !== null) await Promise.all([this.outgoing.reload(), this.overview.reload(), this.targets.reload()]);
  }
  protected async cancel(m: OutgoingMail) { if ((await this.cancelling.run(m.id)) !== null) await Promise.all([this.outgoing.reload(), this.targets.reload()]); }
  protected async saveEdit(m: OutgoingMail) { const e = this.edit(m); if ((await this.editing.run({ id: m.id, ...e })) !== null) { this.edits.update((x) => { const n = { ...x }; delete n[m.id]; return n; }); await this.outgoing.reload(); } }
  protected async check() { if ((await this.checking.run()) !== null) await Promise.all([this.inbox.reload(), this.outgoing.reload(), this.overview.reload()]); }
  protected async refresh() { await Promise.all([this.accounts.reload(), this.settings.reload(), this.overview.reload()]); }

  protected tpl(t: MailTemplate) { return this.tplEdits()[t.id] ?? { subject: t.subject, body: t.body }; }
  protected setTpl(t: MailTemplate, k: 'subject' | 'body', v: string) { this.tplEdits.update((e) => ({ ...e, [t.id]: { ...this.tpl(t), [k]: v } })); }
  protected insert(t: MailTemplate, marker: string, area: HTMLTextAreaElement) {
    const v = this.tpl(t).body, at = area.selectionStart ?? v.length;
    this.setTpl(t, 'body', v.slice(0, at) + marker + v.slice(area.selectionEnd ?? at));
    queueMicrotask(() => { area.focus(); area.selectionStart = area.selectionEnd = at + marker.length; });
  }
  protected preview(t: MailTemplate) {
    const sample: Record<string, string> = { '{{empresa}}': 'Empresa Exemplo', '{{vaga}}': 'Cargo da vaga', '{{experiencia}}': 'Hoje atuo como (seu cargo atual) na (sua empresa).', '{{competencias}}': 'Trabalho com (as competências do seu perfil que a vaga cita).', '{{link_vaga}}': 'https://…', '{{nome}}': 'Seu Nome', '{{assinatura}}': 'Seu Nome\n(seu telefone)\n(seu LinkedIn)' };
    return Object.entries(sample).reduce((s, [k, v]) => s.split(k).join(v), this.tpl(t).body);
  }
  protected async saveTpl(t: MailTemplate) { if ((await this.savingTpl.run({ id: t.id, ...this.tpl(t) })) !== null) { this.tplEdits.update((x) => { const n = { ...x }; delete n[t.id]; return n; }); await this.templates.reload(); } }
  protected tplLabel(t: MailTemplate) { return `${t.kind === 'first' ? 'Candidatura' : 'Lembrete'} · ${t.language === 'pt' ? 'português' : 'inglês'}`; }
}

/** A janela de envio em palavras: das 0h às 24h é o 24/7. */
function hours(s: { windowStart: number; windowEnd: number }) {
  return s.windowStart === 0 && s.windowEnd === 24 ? '24 horas, todos os dias' : `das ${s.windowStart}h às ${s.windowEnd}h`;
}

type Step = 'done' | 'now' | 'bad' | 'wait' | 'off';
/** 06/11, 00:53 */
function short(iso: string | null) {
  return iso ? new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' }) : null;
}
function upper(s: string) { return s.charAt(0).toUpperCase() + s.slice(1); }
