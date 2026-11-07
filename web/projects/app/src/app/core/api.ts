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

export async function send<T = void>(method: 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown): Promise<T> {
  const r = await fetch(API + path, { method, headers: { 'content-type': 'application/json', accept: 'application/json' }, body: method === 'DELETE' ? undefined : JSON.stringify(body ?? {}) });
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
export interface JobRow { id: string; sentBy: string | null; company: string; title: string; location: string; status: string; score: number | null; confidence: number | null; reason: string | null; applyUrl: string; source: string; firstSeenAt: string; lastVerifiedAt: string | null; open: boolean; duplicateOf: string | null; seniority: string | null; modality: string | null; salaryText: string | null; contract: string | null; english: string | null; sponsorship: string | null; eligibility: string | null; eligibilityEvidence: string | null; applyChannel: string; applyEmail: string | null; stack: string[]; companyDomain: string | null; }
export interface CaptureResult { kind: 'watch' | 'saved' | 'known'; jobId: string | null; source: string | null; message: string; }
export interface Factor { name: string; weight: number; value: number; points: number; note: string; unknown: boolean; }
export interface JobDetail { job: JobRow; factors: Factor[]; description: string; snapshots: number; integrityWarnings: string[]; }
export interface ApplicationRow { id: string; jobId: string; company: string; title: string; location: string; state: string; score: number; createdAt: string; updatedAt: string; preparedBy: string | null; evidenceLevel: number; receiptId: string | null; lastReason: string | null; outcome?: string | null; outcomeAt?: string | null; }
export interface Field { label: string; value: string; source: string; }
export interface HistoryRow { at: string; from: string | null; to: string; reason: string; actor: string; detail: string | null; }
export interface EvidenceRow { level: number; kind: string; detail: string; at: string; }
export interface TaskRow { id: string; applicationId: string; kind: string; title: string; detail: string; createdAt: string; resolvedAt: string | null; resolution: string | null; }
export interface ApplyCapability { channel: string; platform: string | null; prepare: boolean; submit: boolean; confirm: string; recover: string; }
export interface ApplicationDetail { application: ApplicationRow; applyUrl: string; payloadHash: string | null; fields: Field[]; hasResume: boolean; history: HistoryRow[]; evidence: EvidenceRow[]; tasks: TaskRow[]; capability: ApplyCapability; }
export interface Campaign { id: string; name: string; enabled: boolean; mode: string; minFitScore: number; maxSubmissionsPerDay: number; technologies: string[]; titles: string[]; locations: string[]; sources: string[]; timeZone: string; window: string; lastRunAt: string | null; }
export interface UsageWindow { name: string; usedPercent: number | null; resetsAt: string | null; }
export interface LlmResult { ok: boolean; json: string | null; providerId: string; model: string; effort: string; error: string | null; seconds: number; degraded: boolean; cached: boolean; }
export interface LocalModel { name: string; sizeGb: number; slot: string | null; loaded: boolean; gpuPercent: number | null; }
export interface BenchRow { model: string; valid: number; total: number; jobExtract: number; emailClassify: number; questionMap: number; overall: number; p50: number; p95: number; cold: number; gpuPercent: number | null; vramMb: number | null; at: string; }
export interface TaskUsage { task: string; rung: string; privacy: string; cloud: number; budget: number; local: number; cached: number; failed: number; }
export interface LlmCall { at: string; task: string; providerId: string; model: string; effort: string; ok: boolean; error: string | null; seconds: number; degraded: boolean; cached: boolean; }
export interface Provider { id: string; name: string; family: string; order: number; installed: boolean; version: string | null; login: string; plan: string | null; usage: UsageWindow[]; available: boolean; status: string; detail: string; lastTest: LlmResult | null; }

export interface Identity { fullName: string; email: string | null; phone: string | null; location: string | null; linkedIn: string | null; gitHub: string | null; website: string | null; }
export interface Confirmations {
  authorizedCountries: string[]; needsSponsorship: boolean; floorCltBrl: number | null; floorPjBrl: number | null; floorUsdMonth: number | null;
  modalities: string[]; cities: string[]; relocate: boolean; startAvailability: string | null; englishLevel: string | null; selfIdDefault: string; openAnswersPolicy: string; confirmedAt: string | null;
}
export interface ProfileDocument { id: string; fileName: string; kind: string; pages: number; status: 'Stored' | 'Extracting' | 'Ready' | 'Failed'; error: string | null; extractedBy: string | null; importedAt: string; extractedAt: string | null; }
export interface ProfileFact { id: string; documentId: string | null; alsoIn?: string[]; kind: string; statement: string; parentId: string | null; organization: string | null; role: string | null; start: string | null; end: string | null; technologies: string[]; level: string | null; quote: string | null; page: number | null; note: string | null; status: 'Verified' | 'Inferred' | 'Confirmed' | 'Rejected'; }
export interface Profile { id: string | null; version: number; status: 'Empty' | 'Extracting' | 'NeedsConfirmation' | 'Ready'; synthetic: boolean; identity: Identity; confirmations: Confirmations; confirmedAt: string | null; updatedAt: string | null; documents: ProfileDocument[]; facts: ProfileFact[]; pending: number; }

/** Envio de arquivo (multipart). */
export async function upload<T>(path: string, file: File): Promise<T> {
  const body = new FormData();
  body.append('file', file);
  const r = await fetch(API + path, { method: 'POST', body, headers: { accept: 'application/json' } });
  if (!r.ok) throw await toProblem(r);
  return r.json() as Promise<T>;
}

export interface Account { id: string; kind: string; name: string; position: number; enabled: boolean; ownFolder: boolean; baseUrl: string | null; models: Record<string, string>; monthlyTokens: number | null; tokensThisMonth: number; }
export interface LoginState { running: boolean; url: string | null; ok: boolean | null; output: string | null; }
export interface Gpu { vendor: string; name: string; totalMb: number; usedMb: number; freeMb: number; }
export interface Hardware { gpus: Gpu[]; cpu: string; threads: number; avx2: boolean; avx512: boolean; ramTotalMb: number; ramFreeMb: number; diskFreeGb: number; ollamaInstalled: boolean; ollamaVersion: string | null; ollamaRunning: boolean; }
export interface CatalogModel { name: string; family: string; diskGb: number; vramGb: number; score: number | null; speed: string; note: string; installed: boolean; benchScore: number | null; }
export interface LocalCatalog { hardware: Hardware; models: CatalogModel[]; recommendation: { fast: string; strong: string; why: string; budgetGb: number }; fast: string | null; strong: string | null; }
export interface InstallState { running: boolean; done: boolean; failed: boolean; steps: { name: string; status: string; detail: string; percent: number }[]; }

export async function remove(path: string): Promise<void> {
  const r = await fetch(API + path, { method: 'DELETE', headers: { accept: 'application/json' } });
  if (!r.ok) throw await toProblem(r);
}

export interface ScraperStatus { installed: boolean; version: string | null; venv: string; project: string; cacheMb: number; installing: boolean; installLog: string | null; maxPages: number; maxSeconds: number; maxAiPages: number; assist: string; collectEmails: boolean; }
export interface CrawlFound { title: string; company: string; location: string; applyUrl: string; applyEmail: string | null; source: string; }

// Assistente e primeiro contato.
export interface OnboardingView { done: boolean; aiReady: boolean; accounts: string[]; localReady: boolean; facts: number; profileConfirmed: boolean; firstName: string; campaign: boolean; keywords: number; sources: number; }
export interface ProposedSource { source: string; why: string; }
export interface SetupProposal { ok: boolean; error: string | null; area: string; summary: string; titles: string[]; keywords: string[]; locations: string[]; remote: boolean; seniority: string; minFitScore: number; sources: ProposedSource[]; providerId: string; model: string; fromAi: boolean; }
export interface AssistantAction { type: string; title: string; values: string[]; number: number | null; }
export interface AssistantAnswer { ok: boolean; reply: string; actions: AssistantAction[]; providerId: string; model: string; rung: string; seconds: number; escalatedFrom: string | null; error: string | null; }
export interface AskResponse { conversationId: string; rung: string; why: string; answers: AssistantAnswer[]; }
export interface ChatTurn { id: string; at: string; role: 'user' | 'assistant' | 'route'; text: string; data: string | null; }
export interface ConversationView { id: string; subject: string; startedAt: string; lastAt: string; messages: number; }
export interface SourceTestResult { ok: boolean; count: number; seconds: number; sample: { title: string; company: string; location: string; applyUrl: string; applyEmail: string | null }[]; error: string | null; }

// E-mail.
export interface MailPreset { id: string; name: string; smtp: string; smtpPort: number; smtpSsl: boolean; imap: string; imapPort: number; appPasswordUrl: string; hint: string; }
export interface MailOverview { sentToday: number; dailyCap: number; awaiting: number; scheduled: number; sent: number; replied: number; interviews: number; mode: 'Draft' | 'Review' | 'Auto'; consent: boolean; connected: boolean; }
export interface MailAccountView { id: string; provider: string; address: string; displayName: string; smtpHost: string; smtpPort: number; imapHost: string; checkedAt: string | null; lastError: string | null; sends: boolean; pausedUntil: string | null; pauseReason: string | null; sentToday: number; }
export interface MailSettings { mode: 'Draft' | 'Review' | 'Auto'; dailyCap: number; followUpDays: number; windowStart: number; windowEnd: number; attachResume: boolean; autoConsentAt: string | null; autoConsentText: string | null; }
export interface MailTemplate { id: string; kind: 'first' | 'followup'; language: 'pt' | 'en'; subject: string; body: string; updatedAt: string; }
export interface MailTarget { jobId: string; company: string; title: string; location: string; applyEmail: string; applyUrl: string; companyDomain: string | null; score: number; mailStatus: string | null; mailId: string | null; }
export interface OutgoingMail { id: string; jobId: string; company: string; jobTitle: string; to: string; subject: string; body: string; kind: 'first' | 'followup'; followsId: string | null; status: string; createdAt: string; approvedAt: string | null; sentAt: string | null; messageId: string | null; attachmentPath: string | null; attachmentName: string | null; error: string | null; draftedBy: string; checkNotes: string | null; repliedAt: string | null; replyClass: string | null; threadId: string | null; }
export interface IncomingMail { id: string; messageId: string; from: string; subject: string; date: string; snippet: string; class: string | null; actionNeeded: boolean; outgoingId: string | null; ambiguous: boolean; receivedAt: string; }
export interface AuthorizationItem { id: string; grantedAt: string; expiresAt: string; maxPerDay: number; minScore: number; adapters: string[]; revokedAt: string | null; status: 'ativa' | 'vencida' | 'revogada'; }

export interface ExploreVerdict { source: string; origin: string; total: number; relevant: number; sample: string[]; fit: boolean; reason: string; at: string; }
export interface ExploreLast { at: string; tested: number; added: string[]; frontier: number; swept: number; seconds: number; skipped: string | null; origins: Record<string, number> | null; }
export interface ExploreStatus {
  enabled: boolean; intervalMinutes: number; searchIntervalMinutes: number; running: boolean; last: ExploreLast | null; next: string | null;
  frontier: number; queue: Record<string, number>; explored: number; fit: number; recent: ExploreVerdict[];
}
