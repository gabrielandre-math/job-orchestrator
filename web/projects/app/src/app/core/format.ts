/** Rótulos e tons dos estados. Mesmo vocabulário em todas as telas. */
export type Tone = 'ok' | 'warn' | 'danger' | 'info' | 'accent' | 'neutral';

const STATES: Record<string, [string, Tone]> = {
  Shortlisted: ['Na fila', 'info'],
  MaterialsReady: ['Currículo pronto', 'info'],
  FormPreparing: ['Preenchendo', 'info'],
  FormValidated: ['Preparada', 'accent'],
  AwaitingApproval: ['Esperando você', 'warn'],
  ReadyToSubmit: ['Aprovada', 'info'],
  Submitting: ['Enviando', 'info'],
  SubmittedConfirmed: ['Confirmada', 'ok'],
  SubmissionUnknown: ['Incerta', 'warn'],
  Failed: ['Falhou', 'danger'],
  NeedsHuman: ['Precisa de você', 'warn'],
  BlockedByPolicy: ['Bloqueada', 'danger'],
  PausedProvider: ['Pausada', 'neutral'],
  RetryScheduled: ['Nova tentativa', 'neutral'],
  DeadLetter: ['Travada', 'danger'],
  Withdrawn: ['Retirada', 'neutral'],
  ManualHandoff: ['Para enviar', 'warn'],
  SubmittedDeclared: ['Enviada por você', 'ok'],
};

const JOBS: Record<string, [string, Tone]> = {
  Shortlisted: ['Na fila', 'accent'],
  Scored: ['Abaixo do corte', 'neutral'],
  Discovered: ['Encontrada', 'info'],
  Rejected: ['Cortada', 'danger'],
  Duplicate: ['Duplicada', 'neutral'],
  Expired: ['Encerrada', 'neutral'],
};

const MODES: Record<string, string> = {
  DiscoveryOnly: 'Só descobrir',
  PrepareOnly: 'Prepara, você envia',
  ReviewRequired: 'Revisão sua',
  AutoSubmit: 'Automático',
};

export const stateLabel = (s: string) => STATES[s]?.[0] ?? s;
export const stateTone = (s: string): Tone => STATES[s]?.[1] ?? 'neutral';
export const jobLabel = (s: string) => JOBS[s]?.[0] ?? s;
export const jobTone = (s: string): Tone => JOBS[s]?.[1] ?? 'neutral';
export const modeLabel = (m: string) => MODES[m] ?? m;

const time = new Intl.DateTimeFormat('pt-BR', { hour: '2-digit', minute: '2-digit' });
const day = new Intl.DateTimeFormat('pt-BR', { day: '2-digit', month: 'short' });

/** "agora", "há 5 min", "há 3 h", ou a data. */
export function ago(iso: string | null | undefined): string {
  if (!iso) return 'nunca';
  const d = new Date(iso);
  const s = Math.max(0, (Date.now() - d.getTime()) / 1000);
  if (s < 45) return 'agora';
  if (s < 3600) return `há ${Math.round(s / 60)} min`;
  if (s < 86400) return `há ${Math.round(s / 3600)} h`;
  return day.format(d);
}

export const clock = (iso: string) => time.format(new Date(iso));
export const evidenceLabel = (level: number) => ['Sem prova', 'Preenchido', 'Clique feito', 'Página de confirmação', 'Recibo', 'E-mail casado'][level] ?? `Nível ${level}`;

/** "1 duplicada", "3 duplicadas". */
export const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`;
