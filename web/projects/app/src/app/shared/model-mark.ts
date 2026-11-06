import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

interface Logo { file: string; mono: boolean; family: string; name: string }

/** Família do modelo pelo nome (os do Ollama) e, sem nome conhecido, pelo provedor. */
const FAMILIES: { test: RegExp; logo: Logo }[] = [
  { test: /claude|sonnet|opus|haiku|fable/i, logo: { file: 'claude', mono: false, family: 'claude', name: 'Claude' } },
  { test: /gpt-oss|gpt|codex|^o\d/i, logo: { file: 'openai', mono: true, family: 'openai', name: 'OpenAI' } },
  { test: /gemma|gemini/i, logo: { file: 'gemini', mono: false, family: 'gemini', name: 'Google' } },
  { test: /llama/i, logo: { file: 'svgl/meta', mono: false, family: 'meta', name: 'Meta' } },
  { test: /qwen|qwq/i, logo: { file: 'svgl/qwen', mono: true, family: 'pulse', name: 'Qwen' } },
  { test: /mistral|mixtral|codestral|ministral|devstral|magistral/i, logo: { file: 'mistral', mono: false, family: 'blocks', name: 'Mistral' } },
  { test: /deepseek/i, logo: { file: 'deepseek', mono: false, family: 'pulse', name: 'DeepSeek' } },
  { test: /\bphi/i, logo: { file: 'svgl/microsoft', mono: false, family: 'blocks', name: 'Microsoft' } },
  { test: /granite/i, logo: { file: 'svgl/ibm', mono: false, family: 'pulse', name: 'IBM' } },
  { test: /command-r|aya/i, logo: { file: 'svgl/cohere', mono: false, family: 'pulse', name: 'Cohere' } },
];
const PROVIDERS: Record<string, Logo> = {
  'claude-pro': FAMILIES[0].logo, 'claude-max': FAMILIES[0].logo, claude: FAMILIES[0].logo, anthropic: FAMILIES[0].logo,
  codex: FAMILIES[1].logo, openai: FAMILIES[1].logo,
  ollama: { file: 'ollama', mono: true, family: 'ollama', name: 'Ollama' },
  gemini: FAMILIES[2].logo, groq: { file: 'groq', mono: false, family: 'pulse', name: 'Groq' },
  openrouter: { file: 'openrouter', mono: true, family: 'pulse', name: 'OpenRouter' }, grok: { file: 'grok', mono: true, family: 'pulse', name: 'xAI' },
};

/** O logo de quem responde (Claude, Codex, Ollama, Gemma, Llama, Qwen...), para as conversas, as chamadas e a lista de modelos. */
export function modelLogo(provider: string | null | undefined, model: string | null | undefined): Logo | null {
  const byModel = model ? FAMILIES.find((f) => f.test.test(model))?.logo : undefined;
  return byModel ?? (provider ? PROVIDERS[provider.toLowerCase()] ?? null : null);
}

/**
 * Logo do modelo, com a animação de "pensando" de cada marca enquanto escreve: a estrela do Claude gira e respira, a flor da
 * OpenAI gira, a lhama do Ollama balança, a faísca do Google cintila, o infinito da Meta corre e os outros pulsam. Parado, é só o logo.
 */
@Component({
  selector: 'jo-model-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[style.--mm]': "size() + 'px'", '[class.busy]': 'busy()', '[class]': "'jo-mm f-' + (logo()?.family ?? 'none')" },
  template: `
    @if (logo(); as l) {
      <span class="halo" aria-hidden="true"></span>
      @if (l.mono) { <span class="ic mono" [style.--logo]="'url(/logos/' + l.file + '.svg)'" role="img" [attr.aria-label]="l.name"></span> }
      @else { <img class="ic" [src]="'/logos/' + l.file + '.svg'" [alt]="l.name" /> }
    }`,
  styles: `
    :host { position: relative; display: inline-grid; place-items: center; width: var(--mm); height: var(--mm); flex: none; vertical-align: middle; }
    .ic { position: relative; width: 100%; height: 100%; object-fit: contain; display: block; }
    .ic.mono { background: var(--text); mask: var(--logo) center / contain no-repeat; -webkit-mask: var(--logo) center / contain no-repeat; }
    .halo { position: absolute; inset: -22%; border-radius: 50%; opacity: 0; pointer-events: none;
      background: radial-gradient(circle, color-mix(in oklch, var(--mm-glow, var(--accent)) 30%, transparent) 0%, transparent 70%); }
    :host(.f-claude) { --mm-glow: #d97757; } :host(.f-gemini) { --mm-glow: #4f8df7; } :host(.f-meta) { --mm-glow: #0a7cff; }
    :host(.busy) .halo { animation: mm-halo 1.8s ease-in-out infinite; }
    /* Claude: a estrela gira devagar e respira, como na marca quando está pensando. */
    :host(.busy.f-claude) .ic { animation: mm-claude 2.6s cubic-bezier(0.45, 0, 0.55, 1) infinite; }
    /* OpenAI (Codex): a flor gira sem parar. */
    :host(.busy.f-openai) .ic { animation: mm-spin 1.8s linear infinite; }
    /* Ollama: a lhama balança a cabeça. */
    :host(.busy.f-ollama) .ic { animation: mm-nod 1.1s ease-in-out infinite alternate; transform-origin: 50% 90%; }
    /* Google (Gemma, Gemini): a faísca cintila e gira um pouco. */
    :host(.busy.f-gemini) .ic { animation: mm-spark 1.6s ease-in-out infinite; }
    /* Meta (Llama): o infinito corre de um lado para o outro. */
    :host(.busy.f-meta) .ic { animation: mm-loop 1.4s ease-in-out infinite; }
    /* Blocos (Mistral, Microsoft): piscam em degraus. */
    :host(.busy.f-blocks) .ic { animation: mm-steps 1.2s steps(4) infinite; }
    :host(.busy.f-pulse) .ic, :host(.busy.f-none) .ic { animation: mm-pulse 1.4s ease-in-out infinite; }
    @keyframes mm-halo { 50% { opacity: 1; transform: scale(1.08); } }
    @keyframes mm-claude { 0% { transform: rotate(0deg) scale(1); } 50% { transform: rotate(180deg) scale(0.84); } 100% { transform: rotate(360deg) scale(1); } }
    @keyframes mm-spin { to { transform: rotate(360deg); } }
    @keyframes mm-nod { from { transform: rotate(-7deg) translateY(0); } to { transform: rotate(5deg) translateY(-1.5px); } }
    @keyframes mm-spark { 0%, 100% { transform: rotate(0deg) scale(1); filter: brightness(1); } 50% { transform: rotate(45deg) scale(0.86); filter: brightness(1.35); } }
    @keyframes mm-loop { 0%, 100% { transform: translateX(-8%) scaleX(1); } 50% { transform: translateX(8%) scaleX(0.9); } }
    @keyframes mm-steps { 0% { opacity: 1; } 50% { opacity: 0.55; } 100% { opacity: 1; } }
    @keyframes mm-pulse { 50% { transform: scale(0.86); opacity: 0.75; } }
    @media (prefers-reduced-motion: reduce) { :host(.busy) .ic, :host(.busy) .halo { animation: none; } }
  `,
})
export class ModelMark {
  readonly provider = input<string | null | undefined>(null);
  readonly model = input<string | null | undefined>(null);
  readonly size = input(18);
  /** Escrevendo agora: liga a animação da marca. */
  readonly busy = input(false);
  protected readonly logo = computed(() => modelLogo(this.provider(), this.model()));
}
