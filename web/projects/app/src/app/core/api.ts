/** Chamadas à API local. Mesma origem em produção (a API serve o painel); proxy no ng serve. */
export const API = '/api';

/** Problem Details (RFC 9457) como a API devolve. */
export interface Problem { status: number; code: string; detail: string; fields?: Record<string, string[]>; }

async function toProblem(r: Response): Promise<Problem> {
  try {
    const p = await r.json();
    return { status: r.status, code: p.code ?? p.title ?? 'error', detail: p.detail ?? 'Não foi possível concluir.', fields: p.errors };
  } catch {
    return { status: r.status, code: 'error', detail: 'Não foi possível concluir.' };
  }
}

export async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const r = await fetch(API + path, { signal, headers: { accept: 'application/json' } });
  if (!r.ok) throw await toProblem(r);
  return r.json() as Promise<T>;
}

export async function send<T = void>(method: 'POST' | 'PUT', path: string, body?: unknown): Promise<T> {
  const r = await fetch(API + path, { method, headers: { 'content-type': 'application/json', accept: 'application/json' }, body: JSON.stringify(body ?? {}) });
  if (!r.ok) throw await toProblem(r);
  return (r.status === 204 ? undefined : await r.json()) as T;
}

export function isProblem(e: unknown): e is Problem {
  return typeof e === 'object' && e !== null && 'detail' in e && 'status' in e;
}

// Contratos de leitura (espelham as views do backend).
export interface JobCounts { total: number; discovered: number; shortlisted: number; rejected: number; duplicates: number; expired: number; }
export interface StateCounts { total: number; inProgress: number; prepared: number; awaitingApproval: number; sending: number; confirmed: number; unknown: number; needsHuman: number; blocked: number; failed: number; withdrawn: number; }
export interface Activity { at: string; applicationId: string; company: string; title: string; from: string | null; to: string; reason: string; actor: string; }
export interface CampaignCard { id: string; name: string; mode: string; minFitScore: number; maxSubmissionsPerDay: number; sources: number; }
export interface Adapter { id: string; version: string; name: string; kind: string; capabilities: string[]; domains: string[]; attempts: number; confirmed: number; failures: number; circuitOpen: boolean; circuitOpenUntil: string | null; }
export interface Overview { campaign: CampaignCard | null; jobs: JobCounts; applications: StateCounts; pendingTasks: number; confirmedToday: number; activity: Activity[]; adapters: Adapter[]; }
export interface FlowNode { id: string; label: string; role: string; status: 'active' | 'idle' | 'attention' | 'planned'; count: number; detail: string; phase: string | null; }
export interface FlowEdge { from: string; to: string; count: number; kind: 'flow' | 'fallback' | 'planned'; }
export interface Flow { nodes: FlowNode[]; edges: FlowEdge[]; at: string; }
export interface JobRow { id: string; company: string; title: string; location: string; status: string; score: number | null; confidence: number | null; reason: string | null; applyUrl: string; source: string; firstSeenAt: string; lastVerifiedAt: string | null; open: boolean; duplicateOf: string | null; }
export interface Factor { name: string; weight: number; value: number; points: number; note: string; unknown: boolean; }
export interface JobDetail { job: JobRow; factors: Factor[]; description: string; snapshots: number; }
export interface ApplicationRow { id: string; jobId: string; company: string; title: string; location: string; state: string; score: number; createdAt: string; updatedAt: string; preparedBy: string | null; evidenceLevel: number; receiptId: string | null; lastReason: string | null; }
export interface Field { label: string; value: string; source: string; }
export interface HistoryRow { at: string; from: string | null; to: string; reason: string; actor: string; detail: string | null; }
export interface EvidenceRow { level: number; kind: string; detail: string; at: string; }
export interface TaskRow { id: string; applicationId: string; kind: string; title: string; detail: string; createdAt: string; resolvedAt: string | null; resolution: string | null; }
export interface ApplicationDetail { application: ApplicationRow; applyUrl: string; payloadHash: string | null; fields: Field[]; hasResume: boolean; history: HistoryRow[]; evidence: EvidenceRow[]; tasks: TaskRow[]; }
export interface Campaign { id: string; name: string; enabled: boolean; mode: string; minFitScore: number; maxSubmissionsPerDay: number; technologies: string[]; locations: string[]; sources: string[]; timeZone: string; window: string; lastRunAt: string | null; }
