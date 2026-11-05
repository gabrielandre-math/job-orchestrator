import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { get, send } from '../../core/api';

interface Gpu { vendor: string; name: string; totalMb: number; }
interface Hardware { gpus: Gpu[]; cpu: string; threads: number; ramTotalMb: number; }
interface Terms { version: string; accepted: boolean; acceptedAt: string | null; }

const ITEMS = [
  { iso: 'model', title: 'Processador e placa de vídeo', text: 'O modelo local (Ollama) roda na placa de vídeo quando ela existe, senão no processador. Um modelo carregado por vez, liberado depois de 5 minutos parado.' },
  { iso: 'radar', title: 'Em segundo plano', text: 'A busca roda a cada hora e o Explorador procura fontes novas a cada 10 minutos, mesmo com a janela fechada. O botão Sair para tudo.' },
  { iso: 'reader', title: 'Internet', text: 'Só fontes públicas de vagas e APIs oficiais, com o robots.txt e o limite de cada site respeitados. Nada de robô no LinkedIn, nem de contornar CAPTCHA ou bloqueio.' },
  { iso: 'identity', title: 'Seus dados', text: 'Perfil, currículos e banco ficam no seu computador. Para a sua IA (Claude ou ChatGPT) vão só os trechos de cada tarefa; senhas ficam no chaveiro do sistema.' },
  { iso: 'guardian', title: 'Candidaturas', text: 'Nada é enviado sem o seu ok ou fora dos limites que você escolher. O currículo usa só fatos que estão no seu perfil.' },
  { iso: 'receipt', title: 'Disco', text: 'Banco, coletor e modelos ocupam alguns GB na sua pasta de dados. O desinstalador tira o app, e os dados só saem se você pedir.' },
];

/**
 * Os termos de uso da primeira vez, antes de o app usar a máquina sozinho: o que ele usa (processador, placa de vídeo, memória, disco e
 * internet), quando, o que fica e o que sai, e as regras. Mostra o computador de verdade da pessoa. No painel abre como janela por cima;
 * no instalador entra como um passo (embedded).
 */
@Component({
  selector: 'app-terms',
  imports: [Icon, Iso],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class.embedded]': 'embedded()' },
  template: `
    @if (!embedded()) { <div class="backdrop"></div> }
    <section class="terms" [class.embedded]="embedded()" role="dialog" aria-modal="true" aria-labelledby="terms-title">
      @if (declined()) {
        <div class="declined">
          <span class="bob"><jo-iso name="gate" [size]="88" /></span>
          <h2>Sem o aceite, nada roda sozinho</h2>
          <p class="muted">A busca, o Explorador e o modelo local ficam parados. Dá para ver o que já existe no app e aceitar depois em Configurações.</p>
          <div class="actions"><button type="button" class="btn" (click)="declined.set(false)">Ler de novo</button><button type="button" class="btn primary" (click)="done.emit(false)">Entendi</button></div>
        </div>
      } @else {
        <div class="hero">
          <span class="art" aria-hidden="true"><span class="halo"></span><span class="bob"><jo-iso name="model" [size]="104" /></span></span>
          <div>
            <p class="eyebrow">Termos de uso, versão {{ version() }}</p>
            <h2 id="terms-title">Antes de o app usar a sua máquina</h2>
            <p class="muted">O Job Orchestrator roda no seu computador. Para buscar vagas e escrever currículos sozinho, ele usa o processador, a placa de vídeo, a memória, o disco e a internet. Confira e aceite.</p>
            @if (machine(); as m) { <p class="machine mono">{{ m }}</p> }
          </div>
        </div>
        <ul class="items">
          @for (i of items; track i.title; let n = $index) {
            <li [style.animation-delay.ms]="120 + n * 70"><span class="ico"><jo-iso [name]="i.iso" [size]="40" /></span><span><strong>{{ i.title }}</strong><span class="muted small">{{ i.text }}</span></span></li>
          }
        </ul>
        <label class="agree"><input type="checkbox" [checked]="agree()" (change)="agree.set($any($event.target).checked)" /><span>Li e aceito os termos de uso. Posso parar tudo pelo botão Sair e tirar o app pelo desinstalador.</span></label>
        <footer>
          <button type="button" class="btn ghost" (click)="declined.set(true)">Não aceito</button>
          <button type="button" class="btn primary" [disabled]="!agree() || saving()" (click)="accept()">
            @if (saving()) { <span class="spinner sm" aria-hidden="true"></span>Gravando } @else { <jo-icon name="check" [size]="14" />Aceitar e começar }
          </button>
        </footer>
      }
    </section>`,
  styles: `
    :host { position: fixed; inset: 0; z-index: 70; display: grid; place-items: center; padding: 16px; overflow-y: auto; }
    :host(.embedded) { position: static; display: block; padding: 0; overflow: visible; }
    .backdrop { position: fixed; inset: 0; background: oklch(0% 0 0 / 0.55); backdrop-filter: blur(8px); animation: fade 0.25s ease-out; }
    .terms { position: relative; width: min(820px, 100%); border-radius: 10px; border: 1px solid var(--line-strong); background: var(--surface); padding: 26px 28px 20px;
      box-shadow: 0 30px 90px oklch(0% 0 0 / 0.5); animation: rise 0.36s cubic-bezier(0.2, 0.8, 0.2, 1); display: grid; gap: 18px; }
    .terms.embedded { width: 100%; border: 0; box-shadow: none; padding: 0; background: transparent; animation: none; }
    .hero { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 22px; align-items: center; }
    .art { position: relative; display: grid; place-items: center; width: 132px; height: 132px; }
    .halo { position: absolute; inset: 14px; border-radius: 50%; background: radial-gradient(closest-side, color-mix(in oklch, var(--accent) 26%, transparent), transparent); animation: pulse 3.2s ease-in-out infinite; }
    .bob { display: inline-grid; animation: bob 2.8s ease-in-out infinite; will-change: transform; }
    .eyebrow { margin: 0 0 4px; font: 500 10.5px var(--font-mono); letter-spacing: 0.06em; text-transform: uppercase; color: var(--text-3); }
    h2 { margin: 0 0 6px; font-size: 21px; font-weight: 650; letter-spacing: -0.01em; }
    .hero p { margin: 0; }
    .machine { margin-top: 10px !important; padding: 8px 10px; border-radius: 6px; background: var(--surface-2); border: 1px solid var(--line); font-size: 12px; color: var(--text-2); }
    .items { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); border-top: 1px solid var(--line); }
    .items li { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 12px; align-items: start; padding: 12px 4px; border-bottom: 1px solid var(--line); animation: rise 0.32s cubic-bezier(0.2, 0.8, 0.2, 1) both; }
    .items li:nth-child(odd) { padding-right: 16px; border-right: 1px solid var(--line); }
    .items li:nth-child(even) { padding-left: 16px; }
    .items li > span:last-child { display: grid; gap: 3px; }
    .agree { display: flex; gap: 10px; align-items: flex-start; font-size: 13.5px; cursor: pointer; }
    .agree input { margin-top: 2px; accent-color: var(--accent); width: 16px; height: 16px; }
    footer { display: flex; justify-content: flex-end; gap: 8px; }
    .declined { display: grid; justify-items: center; text-align: center; gap: 10px; padding: 18px 8px; }
    .declined p { max-width: 52ch; margin: 0; }
    .actions { display: flex; gap: 8px; margin-top: 6px; }
    @keyframes bob { 50% { transform: translate3d(0, -7px, 0); } }
    @keyframes pulse { 50% { transform: scale(1.12); opacity: 0.7; } }
    @keyframes fade { from { opacity: 0; } }
    @keyframes rise { from { opacity: 0; transform: translateY(8px); } }
    @media (max-width: 720px) { .hero { grid-template-columns: 1fr; justify-items: start; } .items { grid-template-columns: 1fr; } .items li:nth-child(odd) { border-right: 0; padding-right: 4px; } .items li:nth-child(even) { padding-left: 4px; } }
    @media (prefers-reduced-motion: reduce) { .bob, .halo, .items li, .terms, .backdrop { animation: none; } }
  `,
})
export class TermsDialog {
  readonly embedded = input(false);
  /** true quando aceitou; false quando não aceitou. */
  readonly done = output<boolean>();
  protected readonly items = ITEMS;
  protected readonly agree = signal(false);
  protected readonly saving = signal(false);
  protected readonly declined = signal(false);
  protected readonly version = signal('');
  private readonly hw = signal<Hardware | null>(null);

  /** O computador de verdade da pessoa, para os termos falarem da máquina dela. */
  protected readonly machine = computed(() => {
    const h = this.hw();
    if (!h) return null;
    const gpu = h.gpus.slice().sort((a, b) => b.totalMb - a.totalMb)[0];
    const parts = [gpu ? `${gpu.name}${gpu.vendor === 'apple' ? '' : ` (${Math.round(gpu.totalMb / 1024)} GB)`}` : 'sem placa de vídeo dedicada', `${h.cpu} (${h.threads} threads)`, `${Math.round(h.ramTotalMb / 1024)} GB de RAM`];
    return `Seu computador: ${parts.join(', ')}.`;
  });

  constructor() {
    void get<Terms>('/terms').then((t) => this.version.set(t.version)).catch(() => undefined);
    void get<{ hardware: Hardware }>('/local/catalog').then((c) => this.hw.set(c.hardware)).catch(() => undefined);
  }

  protected async accept() {
    this.saving.set(true);
    try { await send('POST', '/terms/accept'); this.done.emit(true); } catch { /* o aviso de erro vem do toast */ }
    this.saving.set(false);
  }
}
