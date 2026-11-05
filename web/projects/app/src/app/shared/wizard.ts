import { ChangeDetectionStrategy, Component, ViewEncapsulation, computed, input, output } from '@angular/core';
import { Icon } from '@jo/ui/icon';

/**
 * Moldura dos assistentes em passos (conectar conta, varredura, fontes, primeiro contato): fundo desfocado, passos no topo
 * com barra de progresso, corpo que desliza na direção do passo e rodapé por projeção ([wz-foot]).
 * As classes internas (.intro, .tile, .way, .cards...) ficam aqui, sem encapsulamento, para todos terem o mesmo desenho.
 */
@Component({
  selector: 'jo-wizard',
  imports: [Icon],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'close.emit()', '[class.back]': 'dir() < 0', '[class.wide]': 'wide()' },
  template: `
    <div class="wz-backdrop" (click)="close.emit()" aria-hidden="true"></div>
    <section class="wz-modal" role="dialog" aria-modal="true" [attr.aria-label]="label()">
      <header class="wz-head">
        <ol class="wz-stepper" aria-label="Passos">
          @for (s of steps(); track s; let i = $index) {
            <li [class.on]="step() === i + 1" [class.done]="step() > i + 1" [attr.aria-current]="step() === i + 1 ? 'step' : null">
              <span class="n num">@if (step() > i + 1) { <jo-icon name="check" [size]="12" /> } @else { {{ i + 1 }} }</span><span class="t">{{ s }}</span>
            </li>
          }
        </ol>
        <button type="button" class="btn ghost" (click)="close.emit()" aria-label="Fechar"><jo-icon name="x" [size]="16" /></button>
      </header>
      <div class="wz-bar" aria-hidden="true"><span [style.width.%]="progress()"></span></div>
      <div class="wz-body"><ng-content /></div>
      <ng-content select="[wz-foot]" />
    </section>`,
  styles: `
    jo-wizard { position: fixed; inset: 0; z-index: 60; display: grid; place-items: center; padding: 24px 16px; --wz-dx: 22px; }
    jo-wizard.back { --wz-dx: -22px; }
    .wz-backdrop { position: absolute; inset: 0; background: oklch(0% 0 0 / 0.5); backdrop-filter: blur(6px); animation: wz-fade 0.22s ease-out; }
    .wz-modal { position: relative; display: grid; grid-template-rows: auto auto minmax(0, 1fr) auto; width: min(920px, 100%); max-height: min(820px, 100%); background: var(--surface);
      border: 1px solid var(--line-strong); border-radius: 8px; overflow: hidden; box-shadow: 0 30px 80px oklch(0% 0 0 / 0.45); animation: wz-rise 0.34s cubic-bezier(0.2, 0.8, 0.2, 1); }
    jo-wizard.wide .wz-modal { width: min(1080px, 100%); }
    .wz-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 16px; }
    .wz-stepper { list-style: none; display: flex; gap: 6px; margin: 0; padding: 0; flex-wrap: wrap; }
    .wz-stepper li { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px 4px 4px; border-radius: 4px; color: var(--text-3); font-size: 12.5px; transition: color 200ms ease, background-color 200ms ease; }
    .wz-stepper .n { display: grid; place-items: center; width: 22px; height: 22px; border-radius: 4px; border: 1px solid var(--line-strong); font-size: 11.5px; transition: background-color 220ms ease, border-color 220ms ease, color 220ms ease; }
    .wz-stepper li.on { color: var(--text); background: var(--surface-2); }
    .wz-stepper li.on .n { border-color: var(--accent); color: var(--accent); box-shadow: 0 0 0 3px var(--accent-weak); }
    .wz-stepper li.done { color: var(--text-2); }
    .wz-stepper li.done .n { background: var(--accent); border-color: var(--accent); color: var(--accent-text); animation: wz-pop 0.3s cubic-bezier(0.2, 0.8, 0.2, 1); }
    .wz-bar { height: 2px; background: var(--line); }
    .wz-bar span { display: block; height: 100%; background: var(--accent); transition: width 480ms cubic-bezier(0.2, 0.8, 0.2, 1); }
    .wz-body { overflow-y: auto; overflow-x: hidden; padding: 20px; display: grid; gap: 18px; align-content: start; }
    .wz-step { display: grid; gap: 18px; align-content: start; min-width: 0; animation: wz-step 0.36s cubic-bezier(0.2, 0.8, 0.2, 1) both; }
    .wz-foot { display: flex; align-items: center; justify-content: space-between; gap: 8px; padding: 10px 16px; border-top: 1px solid var(--line); }
    .wz-foot .end { display: flex; gap: 8px; margin-left: auto; }

    jo-wizard .intro { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 18px; align-items: center; }
    /* A figura de cada passo flutua com um brilho leve da cor do app: o assistente parece vivo, sem pular na cara. */
    jo-wizard .intro > jo-iso { animation: wz-bob 2.8s ease-in-out infinite, wz-glow 2.8s ease-in-out infinite; }
    jo-wizard .promise jo-iso { animation: wz-bob 2.6s ease-in-out infinite; }
    jo-wizard .promise li:nth-child(2) jo-iso { animation-delay: -0.8s; } jo-wizard .promise li:nth-child(3) jo-iso { animation-delay: -1.6s; }
    jo-wizard .intro h2 { font-size: 17px; margin-bottom: 4px; }
    jo-wizard .intro p { margin: 0; line-height: 1.5; max-width: 64ch; }
    jo-wizard .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(190px, 100%), 1fr)); gap: 10px; }
    jo-wizard .tile { position: relative; display: grid; gap: 4px; justify-items: start; align-content: start; text-align: left; padding: 14px; border-radius: 6px; border: 1px solid var(--line-strong);
      background: var(--surface-2); color: var(--text); font: inherit; cursor: pointer; transition: border-color 160ms ease, transform 160ms cubic-bezier(0.2, 0.8, 0.2, 1), box-shadow 180ms ease, background-color 180ms ease;
      animation: wz-rise 0.32s cubic-bezier(0.2, 0.8, 0.2, 1) both; }
    jo-wizard .tile:nth-child(2) { animation-delay: 20ms; } jo-wizard .tile:nth-child(3) { animation-delay: 40ms; } jo-wizard .tile:nth-child(4) { animation-delay: 60ms; }
    jo-wizard .tile:nth-child(5) { animation-delay: 80ms; } jo-wizard .tile:nth-child(n + 6) { animation-delay: 100ms; }
    jo-wizard .tile:hover { border-color: color-mix(in oklch, var(--accent) 55%, var(--line-strong)); transform: translateY(-2px); box-shadow: 0 10px 22px -14px oklch(0% 0 0 / 0.6); }
    jo-wizard .tile:active { transform: translateY(0) scale(0.985); }
    jo-wizard .tile[aria-pressed="true"], jo-wizard .tile[aria-checked="true"] { border-color: var(--accent); background: color-mix(in oklch, var(--accent) 7%, var(--surface-2)); box-shadow: 0 0 0 3px var(--accent-weak); }
    jo-wizard .tile .tick { position: absolute; top: 10px; right: 10px; width: 18px; height: 18px; border-radius: 4px; display: grid; place-items: center; background: var(--accent); color: var(--accent-text);
      transform: scale(0); transition: transform 220ms cubic-bezier(0.2, 0.8, 0.2, 1); }
    jo-wizard .tile[aria-pressed="true"] .tick, jo-wizard .tile[aria-checked="true"] .tick { transform: scale(1); }
    jo-wizard .logo-box { display: grid; place-items: center; width: 44px; height: 44px; margin-bottom: 6px; }
    jo-wizard .logo { display: block; width: 34px; height: 34px; }
    jo-wizard .logo.mono { background: var(--text); mask: var(--logo) center / contain no-repeat; -webkit-mask: var(--logo) center / contain no-repeat; }
    jo-wizard .small { font-size: 12px; }
    jo-wizard .ways, jo-wizard .cards { display: grid; gap: 10px; }
    jo-wizard .cards.row { grid-template-columns: repeat(auto-fit, minmax(min(200px, 100%), 1fr)); }
    jo-wizard .way, jo-wizard .card { position: relative; display: grid; gap: 6px; justify-items: start; align-content: start; text-align: left; padding: 14px 16px; border-radius: 6px; border: 1px solid var(--line-strong);
      background: transparent; color: var(--text); font: inherit; cursor: pointer; transition: border-color 160ms ease, background-color 180ms ease, box-shadow 180ms ease, transform 140ms ease; }
    jo-wizard .way:hover, jo-wizard .card:hover { border-color: var(--accent); background: var(--accent-weak); }
    jo-wizard .way:active, jo-wizard .card:active { transform: scale(0.99); }
    jo-wizard .card[aria-checked="true"] { border-color: var(--accent); background: color-mix(in oklch, var(--accent) 8%, transparent); box-shadow: 0 0 0 3px var(--accent-weak); }
    jo-wizard .card .dot { position: absolute; top: 14px; right: 14px; width: 16px; height: 16px; border-radius: 50%; border: 1.5px solid var(--line-strong); display: grid; place-items: center; transition: border-color 160ms ease; }
    jo-wizard .card .dot::after { content: ""; width: 8px; height: 8px; border-radius: 50%; background: var(--accent); transform: scale(0); transition: transform 200ms cubic-bezier(0.2, 0.8, 0.2, 1); }
    jo-wizard .card[aria-checked="true"] .dot { border-color: var(--accent); }
    jo-wizard .card[aria-checked="true"] .dot::after { transform: scale(1); }
    jo-wizard .card strong { padding-right: 22px; }
    jo-wizard .badge { padding: 1px 7px; border-radius: 3px; font: 500 11px var(--font-ui); background: var(--surface-3); color: var(--text-2); }
    jo-wizard .badge.rec { background: var(--accent-weak); color: var(--accent); }
    jo-wizard .badge.free { background: color-mix(in oklch, var(--ok) 16%, transparent); color: var(--ok); }
    jo-wizard .big { height: 40px; padding: 0 20px; font-size: 14px; justify-self: start; }
    jo-wizard .waiting { display: flex; gap: 14px; align-items: flex-start; padding: 14px 16px; border-radius: 6px; border: 1px solid var(--line-strong); background: var(--surface-2); }
    jo-wizard .waiting p { margin: 0 0 6px; }
    jo-wizard .waiting a { color: var(--accent); }
    jo-wizard .waiting.bad { border-color: color-mix(in oklch, var(--warn) 45%, var(--line)); }
    jo-wizard .out { margin: 8px 0; padding: 8px 10px; border-radius: 4px; background: var(--bg); border: 1px solid var(--line); font-size: 11px; white-space: pre-wrap; max-height: 160px; overflow: auto; }
    jo-wizard .done-actions { display: flex; flex-wrap: wrap; gap: 8px; }
    jo-wizard .ok { color: var(--ok); }
    jo-wizard .warn { color: var(--warn); }
    jo-wizard .locked { display: flex; gap: 10px; align-items: flex-start; padding: 12px 14px; border-radius: 6px; background: var(--surface-2); border: 1px dashed var(--line-strong); color: var(--text-2); font-size: 12.5px; line-height: 1.5; }
    jo-wizard .locked jo-icon { color: var(--ok); margin-top: 2px; }
    jo-wizard .line { display: flex; gap: 12px; align-items: flex-start; padding: 14px 16px; border-radius: 6px; border: 1px solid var(--line-strong); cursor: pointer; transition: border-color 160ms ease, background-color 180ms ease; }
    jo-wizard .line:hover { border-color: color-mix(in oklch, var(--accent) 45%, var(--line-strong)); }
    jo-wizard .line p { margin: 2px 0 0; color: var(--text-2); font-size: 12.5px; line-height: 1.5; }
    jo-wizard .pipe { display: grid; grid-template-columns: repeat(auto-fit, minmax(110px, 1fr)); gap: 8px; }
    jo-wizard .station { display: grid; justify-items: center; gap: 4px; padding: 10px 6px 8px; border-radius: 6px; border: 1px solid var(--line); background: var(--surface-2); text-align: center; transition: border-color 200ms ease, opacity 200ms ease; }
    jo-wizard .station.idle { opacity: 0.55; }
    jo-wizard .station.working { border-color: var(--accent); box-shadow: 0 0 0 3px var(--accent-weak); }
    jo-wizard .station.done { border-color: color-mix(in oklch, var(--ok) 45%, var(--line)); }
    jo-wizard .station.bad { border-color: color-mix(in oklch, var(--warn) 55%, var(--line)); }
    jo-wizard .station strong { font-size: 12px; }
    jo-wizard .station span { font-size: 11px; color: var(--text-3); line-height: 1.35; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
    jo-wizard .spinner, .spinner { width: 22px; height: 22px; border-radius: 50%; border: 2.5px solid var(--surface-3); border-top-color: var(--accent); animation: wz-spin 0.9s linear infinite; flex: none; display: inline-block; }
    jo-wizard .spinner.sm, .spinner.sm { width: 12px; height: 12px; border-width: 2px; vertical-align: -1px; margin-right: 6px; }

    @keyframes wz-fade { from { opacity: 0; } }
    @keyframes wz-bob { 50% { transform: translateY(-5px); } }
    @media (prefers-reduced-motion: reduce) { jo-wizard .intro > jo-iso, jo-wizard .promise jo-iso { animation: none; } }
    @keyframes wz-glow { 50% { filter: drop-shadow(0 12px 14px color-mix(in oklch, var(--accent) 22%, transparent)); } }
    @keyframes wz-rise { from { opacity: 0; transform: translateY(10px) scale(0.99); } }
    @keyframes wz-step { from { opacity: 0; transform: translateX(var(--wz-dx)); } }
    @keyframes wz-pop { 40% { transform: scale(1.18); } }
    @keyframes wz-spin { to { transform: rotate(360deg); } }
    @media (max-width: 640px) {
      jo-wizard { padding: 0; }
      .wz-modal, jo-wizard.wide .wz-modal { width: 100%; height: 100%; max-height: none; border-radius: 0; border: 0; }
      jo-wizard .intro { grid-template-columns: 1fr; }
      jo-wizard .intro jo-iso { display: none; }
      .wz-stepper li { padding-right: 6px; gap: 0; }
      .wz-stepper li .t { display: none; }
      .wz-stepper li.on .t { display: inline; margin-left: 6px; font-size: 12px; }
    }
  `,
})
export class Wizard {
  readonly steps = input.required<string[]>();
  readonly step = input.required<number>();
  readonly label = input('Assistente');
  readonly wide = input(false);
  readonly close = output<void>();
  protected readonly progress = computed(() => ((this.step() - 1) / Math.max(1, this.steps().length - 1)) * 100);
  private previous = 1;
  /** Direção do último passo, para o corpo deslizar para a frente ou para trás. */
  protected readonly dir = computed(() => {
    const s = this.step();
    const d = s >= this.previous ? 1 : -1;
    this.previous = s;
    return d;
  });
}
