import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, output, signal } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Wizard } from '../../shared/wizard';
import { Account, LlmResult, LoginState, Provider, get, send } from '../../core/api';
import { command } from '../../core/command';

interface Way { kind: string; label: string; badge: string; detail: string; subscription?: boolean; keyUrl?: string; keyHint?: string; baseUrl?: boolean }
interface Option { id: string; name: string; logo: { file: string; mono: boolean } | null; tagline: string; kinds: string[]; ways: Way[] }

const OPTIONS: Option[] = [
  { id: 'claude', name: 'Claude', logo: { file: 'claude', mono: false }, tagline: 'Pro, Max ou chave de API', kinds: ['claude-cli', 'anthropic-api'], ways: [
    { kind: 'claude-cli', label: 'Com a sua assinatura Claude', badge: 'recomendado', subscription: true, detail: 'Sem custo extra: usa a cota do seu Pro ou Max. O navegador abre na página oficial da Anthropic para você entrar.' },
    { kind: 'anthropic-api', label: 'Com chave de API da Anthropic', badge: 'cobra por uso', keyUrl: 'https://console.anthropic.com/settings/keys', keyHint: 'sk-ant-...', detail: 'Crie a chave no Console da Anthropic e cole aqui.' },
  ] },
  { id: 'chatgpt', name: 'ChatGPT', logo: { file: 'openai', mono: true }, tagline: 'Plus, Pro ou chave de API', kinds: ['codex-cli', 'openai-api'], ways: [
    { kind: 'codex-cli', label: 'Com a sua assinatura ChatGPT', badge: 'recomendado', subscription: true, detail: 'Sem custo extra: usa a cota do seu Plus ou Pro pelo Codex. O navegador abre na página oficial da OpenAI.' },
    { kind: 'openai-api', label: 'Com chave de API da OpenAI', badge: 'cobra por uso', keyUrl: 'https://platform.openai.com/api-keys', keyHint: 'sk-...', detail: 'Crie a chave na plataforma da OpenAI e cole aqui.' },
  ] },
  { id: 'gemini', name: 'Gemini', logo: { file: 'gemini', mono: false }, tagline: 'Chave do Google AI Studio', kinds: ['gemini-api'], ways: [
    { kind: 'gemini-api', label: 'Com a chave do AI Studio', badge: 'cota grátis', keyUrl: 'https://aistudio.google.com/apikey', keyHint: 'AIza...', detail: 'A chave sai em um minuto e tem cota grátis. No nível grátis o Google pode usar os dados para melhorar produtos; aqui só vai o perfil minimizado, sem dados pessoais.' },
  ] },
  { id: 'openrouter', name: 'OpenRouter', logo: { file: 'openrouter', mono: false }, tagline: 'Centenas de modelos, uma chave', kinds: ['openrouter-api'], ways: [
    { kind: 'openrouter-api', label: 'Com chave do OpenRouter', badge: 'cobra por uso', keyUrl: 'https://openrouter.ai/keys', keyHint: 'sk-or-...', detail: 'Uma chave para modelos de vários provedores, com crédito pré-pago.' },
  ] },
  { id: 'deepseek', name: 'DeepSeek', logo: { file: 'deepseek', mono: false }, tagline: 'Chave de API', kinds: ['deepseek-api'], ways: [
    { kind: 'deepseek-api', label: 'Com chave da DeepSeek', badge: 'cobra por uso', keyUrl: 'https://platform.deepseek.com/api_keys', keyHint: 'sk-...', detail: 'Crie a chave na plataforma da DeepSeek.' },
  ] },
  { id: 'mistral', name: 'Mistral', logo: { file: 'mistral', mono: false }, tagline: 'Chave de API', kinds: ['mistral-api'], ways: [
    { kind: 'mistral-api', label: 'Com chave da Mistral', badge: 'cobra por uso', keyUrl: 'https://console.mistral.ai/api-keys', keyHint: '', detail: 'Crie a chave no console da Mistral.' },
  ] },
  { id: 'groq', name: 'Groq', logo: { file: 'groq', mono: true }, tagline: 'Modelos abertos, muito rápidos', kinds: ['groq-api'], ways: [
    { kind: 'groq-api', label: 'Com chave da Groq', badge: 'cota grátis', keyUrl: 'https://console.groq.com/keys', keyHint: 'gsk_...', detail: 'Tem cota grátis com limite por minuto.' },
  ] },
  { id: 'grok', name: 'Grok', logo: { file: 'grok', mono: true }, tagline: 'xAI, chave de API', kinds: ['xai-api'], ways: [
    { kind: 'xai-api', label: 'Com chave da xAI', badge: 'cobra por uso', keyUrl: 'https://console.x.ai', keyHint: 'xai-...', detail: 'Crie a chave no console da xAI.' },
  ] },
  { id: 'other', name: 'Outra API', logo: null, tagline: 'Qualquer API no formato da OpenAI', kinds: ['openai-compatible'], ways: [
    { kind: 'openai-compatible', label: 'Com endereço e chave', badge: 'cobra por uso', baseUrl: true, detail: 'Informe o endereço da API (termina em /v1) e a chave.' },
  ] },
];

const CAPS = [{ label: 'Pouco', value: 200_000, hint: 'testes' }, { label: 'Médio', value: 1_000_000, hint: 'uso de um mês' }, { label: 'Bastante', value: 5_000_000, hint: 'muitas vagas' }];

/**
 * Conectar conta em quatro passos: provedor (logos oficiais), jeito de conectar (assinatura recomendada ou chave),
 * conectar (o navegador abre, ou a chave é conferida na hora) e pronto (teste e posição na ordem de gasto).
 */
@Component({
  selector: 'app-connect',
  imports: [Icon, Iso, Wizard],
  templateUrl: './connect.html',
  styleUrl: './connect.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Connect {
  private readonly destroy = inject(DestroyRef);
  readonly accounts = input<Account[]>([]);
  readonly providers = input<Provider[]>([]);
  readonly close = output<void>();
  readonly changed = output<void>();
  readonly local = output<void>();

  protected readonly options = OPTIONS;
  protected readonly caps = CAPS;
  protected readonly step = signal<1 | 2 | 3 | 4>(1);
  protected readonly option = signal<Option | null>(null);
  protected readonly way = signal<Way | null>(null);
  protected readonly key = signal('');
  protected readonly showKey = signal(false);
  protected readonly baseUrl = signal('');
  protected readonly cap = signal(1_000_000);
  protected readonly account = signal<Account | null>(null);
  protected readonly login = signal<LoginState | null>(null);
  protected readonly result = signal<LlmResult | null>(null);
  protected readonly creating = command((body: object) => send<Account>('POST', '/accounts', body));
  protected readonly signing = command((id: string) => send<LoginState>('POST', `/accounts/${id}/login`));
  protected readonly testing = command((id: string) => send<LlmResult>('POST', `/providers/${id}/test`));
  protected readonly ordering = command((ids: string[]) => send('PUT', '/accounts/order', ids), 'Esta conta agora é a primeira da ordem de gasto.');
  private timer?: ReturnType<typeof setInterval>;

  /** O que já está conectado de cada provedor, para mostrar no bloco. */
  protected readonly existing = computed(() => {
    const views = new Map(this.providers().map((p) => [p.id, p]));
    const map = new Map<string, { name: string; ready: boolean }[]>();
    for (const o of OPTIONS) map.set(o.id, this.accounts().filter((a) => o.kinds.includes(a.kind)).map((a) => ({ name: a.name, ready: views.get(a.id)?.available ?? false })));
    return map;
  });

  constructor() { this.destroy.onDestroy(() => clearInterval(this.timer)); }

  protected pick(o: Option) {
    this.option.set(o);
    this.way.set(o.ways.length === 1 ? o.ways[0] : null);
    this.step.set(o.ways.length === 1 ? 3 : 2);
  }
  protected choose(w: Way) { this.way.set(w); this.step.set(3); }
  protected back() {
    const s = this.step();
    if (s === 3 && (this.option()?.ways.length ?? 0) > 1) this.step.set(2);
    else if (s > 1) { this.step.set(1); this.way.set(null); }
    clearInterval(this.timer);
    this.login.set(null);
  }

  private name() {
    const o = this.option()!;
    const w = this.way()!;
    const base = w.subscription ? o.name : `${o.name} (chave)`;
    const same = this.accounts().filter((a) => a.kind === w.kind).length;
    return same > 0 ? `${base} ${same + 1}` : base;
  }

  /** Assinatura: cria a conta com pasta própria e abre o login oficial no navegador; acompanha até terminar. */
  protected async signIn() {
    const created = this.account() ?? (await this.creating.run({ kind: this.way()!.kind, name: this.name() }));
    if (!created) return;
    this.account.set(created);
    const state = await this.signing.run(created.id);
    if (!state) return;
    this.login.set(state);
    clearInterval(this.timer);
    this.timer = setInterval(async () => {
      const s = await get<LoginState>(`/accounts/${created.id}/login`);
      this.login.set(s);
      if (!s.running) {
        clearInterval(this.timer);
        if (s.ok) await this.finish(created);
      }
    }, 2000);
  }

  /** Chave: guarda no keyring; o servidor confere listando os modelos e já escolhe o leve e o principal. */
  protected async saveKey() {
    const w = this.way()!;
    const created = await this.creating.run({ kind: w.kind, name: this.name(), apiKey: this.key().trim(), monthlyTokens: this.cap(), baseUrl: w.baseUrl ? this.baseUrl().trim() : null });
    if (!created) return;
    this.key.set('');
    await this.finish(created);
  }

  private async finish(a: Account) {
    this.account.set(a);
    this.step.set(4);
    this.changed.emit();
    this.result.set(await this.testing.run(a.id));
    this.changed.emit();
  }

  protected async first() {
    const a = this.account();
    if (!a) return;
    const ids = [a.id, ...this.accounts().map((x) => x.id).filter((id) => id !== a.id)];
    if ((await this.ordering.run(ids)) !== null) this.changed.emit();
  }

  protected restart() {
    this.step.set(1); this.option.set(null); this.way.set(null); this.account.set(null); this.login.set(null); this.result.set(null);
  }

  protected tokens(n: number) { return n >= 1_000_000 ? `${n / 1_000_000} mi` : `${n / 1000} mil`; }
}
