import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Badge } from '@jo/ui/badge';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Account, InstallState, LlmCall, LlmResult, LocalCatalog, LoginState, Provider, TaskUsage, get, remove, send } from '../../core/api';
import { command } from '../../core/command';
import { Connect } from './connect';
import { query } from '../../core/query';

/** Logo oficial de cada tipo de conta. Claude tem cor de marca; OpenAI e Ollama são monocromáticas e seguem o tema. */
const LOGO_BY_KIND: Record<string, { file: string; mono: boolean }> = {
  'claude-cli': { file: 'claude', mono: false }, 'anthropic-api': { file: 'claude', mono: false },
  'codex-cli': { file: 'openai', mono: true }, 'openai-api': { file: 'openai', mono: true },
  'gemini-api': { file: 'gemini', mono: false }, ollama: { file: 'ollama', mono: true },
};

const TASKS: Record<string, string> = {
  'job.extract': 'Ler a vaga', 'question.map': 'Ligar pergunta ao banco', 'email.classify': 'Classificar e-mail',
  'profile.extract': 'Ler o currículo', 'resume.tailor': 'Currículo por vaga', 'resume.factcheck': 'Revisão cruzada',
  'cover.write': 'Carta', 'question.draft': 'Resposta aberta', 'interview.prep': 'Guia de entrevista',
};
const RUNGS: Record<string, string> = { Local: 'local rápido', LocalStrong: 'local forte', Light: 'leve', Medium: 'médio', Strong: 'forte', Top: 'topo' };

/** Tipos de conta que dá para conectar. Assinatura entra pelo navegador; chave de API cobra por uso e exige teto. */
const KINDS = [
  { kind: 'claude-cli', label: 'Claude, pela assinatura', hint: 'Abre o navegador para você entrar na sua conta Claude (Pro ou Max). Cada conta tem a sua pasta de login.', key: false },
  { kind: 'codex-cli', label: 'ChatGPT, pela assinatura', hint: 'Abre o navegador para você entrar na sua conta ChatGPT (Plus ou Pro), pelo Codex.', key: false },
  { kind: 'anthropic-api', label: 'Anthropic, chave de API', hint: 'Cobra por uso. A chave fica no gnome-keyring.', key: true },
  { kind: 'openai-api', label: 'OpenAI, chave de API', hint: 'Cobra por uso. A chave fica no gnome-keyring.', key: true },
  { kind: 'gemini-api', label: 'Gemini, chave do AI Studio', hint: 'Tem cota grátis. No nível grátis o Google pode usar os dados para melhorar produtos; o perfil vai minimizado, sem dados pessoais.', key: true },
  { kind: 'openai-compatible', label: 'Outra API compatível (OpenRouter, Groq, DeepSeek)', hint: 'Qualquer API no formato da OpenAI. Informe o endereço e a chave.', key: true },
];

/**
 * Provedores de IA: as suas contas (várias por provedor), a ordem de gasto, a cota de cada uma, o modelo local com instalador
 * que recomenda o melhor para a sua máquina, e o uso de cada tarefa.
 */
@Component({
  selector: 'app-providers',
  imports: [Badge, Icon, Iso, Connect],
  templateUrl: './providers.page.html',
  styleUrl: './providers.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProvidersPage {
  private readonly destroy = inject(DestroyRef);
  protected readonly list = query<Provider[]>(() => '/providers', 15000);
  protected readonly accounts = query<Account[]>(() => '/accounts', 15000);
  protected readonly local = query<LocalCatalog>(() => '/local/catalog', 20000);
  protected readonly usage = query<TaskUsage[]>(() => '/providers/usage', 15000);
  protected readonly calls = query<LlmCall[]>(() => '/providers/calls?limit=12', 15000);
  protected readonly testing = signal<string | null>(null);
  protected readonly test = command((id: string) => send<LlmResult>('POST', `/providers/${id}/test`));
  protected readonly saving = command((x: { id: string; body: object }) => send('PUT', `/accounts/${x.id}`, x.body));
  protected readonly ordering = command((ids: string[]) => send('PUT', '/accounts/order', ids));
  protected readonly deleting = command((id: string) => remove(`/accounts/${id}`));
  protected readonly creating = command((body: object) => send<Account>('POST', '/accounts', body));
  protected readonly logging = command((id: string) => send<LoginState>('POST', `/accounts/${id}/login`));
  protected readonly installing = command((body: object) => send<InstallState>('POST', '/local/install', body));
  protected readonly slotting = command((body: object) => send('PUT', '/local/slots', body));
  protected readonly kinds = KINDS;

  protected readonly adding = signal(false);
  protected readonly wizard = signal(false);
  protected readonly draft = signal({ kind: 'claude-cli', name: 'Claude 2', apiKey: '', baseUrl: '', monthlyTokens: 1_000_000 });
  protected readonly confirmDelete = signal<string | null>(null);
  protected readonly login = signal<{ id: string; state: LoginState } | null>(null);
  protected readonly install = signal<InstallState | null>(null);
  protected readonly picks = signal<Set<string>>(new Set());
  protected readonly slots = signal<{ fast: string | null; strong: string | null }>({ fast: null, strong: null });
  private poll?: ReturnType<typeof setInterval>;

  /** Cada conta com o status vivo do provedor (login, cota, último teste). */
  protected readonly rows = computed(() => {
    const views = new Map((this.list.value() ?? []).map((p) => [p.id, p]));
    return (this.accounts.value() ?? []).map((a) => ({ account: a, view: views.get(a.id) ?? null, logo: LOGO_BY_KIND[a.kind] ?? null }));
  });
  protected readonly ollama = computed(() => (this.list.value() ?? []).find((p) => p.id === 'ollama') ?? null);
  protected readonly kind = computed(() => KINDS.find((k) => k.kind === this.draft().kind)!);
  protected readonly gpu = computed(() => this.local.value()?.hardware.gpus[0] ?? null);

  constructor() {
    this.destroy.onDestroy(() => clearInterval(this.poll));
    void this.watchInstall();
  }

  protected async run(id: string) {
    this.testing.set(id);
    await this.test.run(id);
    this.testing.set(null);
    await Promise.all([this.list.reload(), this.calls.reload(), this.usage.reload()]);
  }

  protected async toggle(a: Account) {
    if ((await this.saving.run({ id: a.id, body: { enabled: !a.enabled } })) !== null) await this.refresh();
  }

  protected async move(a: Account, delta: number) {
    const ids = (this.accounts.value() ?? []).map((x) => x.id);
    const i = ids.indexOf(a.id);
    const j = i + delta;
    if (j < 0 || j >= ids.length) return;
    [ids[i], ids[j]] = [ids[j], ids[i]];
    if ((await this.ordering.run(ids)) !== null) await this.refresh();
  }

  protected async drop(a: Account) {
    if (this.confirmDelete() !== a.id) { this.confirmDelete.set(a.id); return; }
    this.confirmDelete.set(null);
    if ((await this.deleting.run(a.id)) !== null) await this.refresh();
  }

  protected setDraft(key: string, value: string | number) {
    this.draft.update((d) => {
      const next = { ...d, [key]: value };
      if (key === 'kind') next.name = defaultName(String(value), this.accounts.value() ?? []);
      return next;
    });
  }

  protected async connect() {
    const d = this.draft();
    const k = this.kind();
    const created = await this.creating.run({
      kind: d.kind, name: d.name, apiKey: k.key ? d.apiKey : null, baseUrl: d.kind === 'openai-compatible' ? d.baseUrl : null,
      monthlyTokens: k.key ? Number(d.monthlyTokens) : null,
    });
    if (!created) return;
    this.adding.set(false);
    this.draft.update((x) => ({ ...x, apiKey: '' }));
    await this.refresh();
    if (!k.key) await this.signIn(created.id);
  }

  /** Login pela assinatura: o CLI abre o navegador; a tela acompanha até você terminar. */
  protected async signIn(id: string) {
    const state = await this.logging.run(id);
    if (!state) return;
    this.login.set({ id, state });
    clearInterval(this.poll);
    this.poll = setInterval(async () => {
      const s = await get<LoginState>(`/accounts/${id}/login`);
      this.login.set({ id, state: s });
      if (!s.running) { clearInterval(this.poll); await this.refresh(); }
    }, 2000);
  }

  protected pick(name: string) { this.picks.update((s) => { const n = new Set(s); if (n.has(name)) n.delete(name); else n.add(name); return n; }); }
  protected setSlot(slot: 'fast' | 'strong', name: string) { this.slots.update((s) => ({ ...s, [slot]: name })); }
  protected useRecommendation() {
    const r = this.local.value()?.recommendation;
    if (!r) return;
    this.slots.set({ fast: r.fast, strong: r.strong });
    this.picks.set(new Set([r.fast, r.strong]));
  }

  /** Instala o que falta (Ollama e modelos) e grava rápido e forte. Modelo já baixado só troca a vaga. */
  protected async installPicked() {
    const c = this.local.value();
    if (!c) return;
    const s = this.slots();
    const wanted = new Set([...this.picks(), ...[s.fast, s.strong].filter((x): x is string => !!x)]);
    const missing = [...wanted].filter((m) => !c.models.find((x) => x.name === m)?.installed);
    if (missing.length === 0 && c.hardware.ollamaRunning) {
      if ((await this.slotting.run({ models: [], fast: s.fast, strong: s.strong })) !== null) await this.refresh();
      return;
    }
    const st = await this.installing.run({ models: missing, fast: s.fast, strong: s.strong });
    if (st) { this.install.set(st); void this.watchInstall(); }
  }

  private async watchInstall() {
    const tick = async () => {
      const st = await get<InstallState>('/local/install');
      this.install.set(st.steps.length > 0 ? st : null);
      if (!st.running) { clearInterval(timer); if (st.done) await this.refresh(); }
    };
    const timer = setInterval(tick, 1200);
    this.destroy.onDestroy(() => clearInterval(timer));
    await tick();
  }

  protected goLocal() {
    this.wizard.set(false);
    queueMicrotask(() => document.getElementById('modelo-local')?.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  protected async refresh() { await Promise.all([this.list.reload(), this.accounts.reload(), this.local.reload()]); }

  protected task(name: string) { return TASKS[name] ?? name; }
  protected rung(name: string) { return RUNGS[name] ?? name; }
  protected pct(x: number) { return `${Math.round(x)}%`; }
  protected gb(mb: number) { return (mb / 1024).toFixed(1); }
  protected tokens(n: number) { return n >= 1_000_000 ? `${(n / 1_000_000).toFixed(1)} mi` : n >= 1000 ? `${Math.round(n / 1000)} mil` : String(n); }
  protected time(iso: string) { return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }); }
  protected date(iso: string) { return new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' }); }
  protected kindLabel(k: string) { return ({ 'claude-cli': 'assinatura Claude', 'codex-cli': 'assinatura ChatGPT', 'anthropic-api': 'chave Anthropic', 'openai-api': 'chave OpenAI', 'gemini-api': 'chave Gemini', 'openai-compatible': 'chave compatível' } as Record<string, string>)[k] ?? k; }
  protected statusLabel(s: string) {
    return ({ ready: 'Pronto', partial: 'Falta modelo', 'no-login': 'Sem login', 'no-key': 'Sem chave', missing: 'Não instalado', blocked: 'Bloqueado', reserve: 'Na reserva', paused: 'Pausado', stopped: 'Parado', off: 'Desligada' } as Record<string, string>)[s] ?? s;
  }
  protected statusTone(s: string) {
    return ({ ready: 'ok', partial: 'warn', 'no-login': 'warn', 'no-key': 'warn', missing: 'neutral', blocked: 'danger', reserve: 'warn', paused: 'warn', stopped: 'neutral', off: 'neutral' } as Record<string, string>)[s] ?? 'neutral';
  }
  protected reset(iso: string | null) {
    if (!iso) return '';
    const d = new Date(iso);
    return `volta ${d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })} às ${d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}`;
  }
}

function defaultName(kind: string, accounts: Account[]) {
  const base = ({ 'claude-cli': 'Claude', 'codex-cli': 'ChatGPT', 'anthropic-api': 'Anthropic API', 'openai-api': 'OpenAI API', 'gemini-api': 'Gemini', 'openai-compatible': 'OpenRouter' } as Record<string, string>)[kind] ?? 'Conta';
  const same = accounts.filter((a) => a.kind === kind).length;
  return same > 0 ? `${base} ${same + 1}` : base;
}
