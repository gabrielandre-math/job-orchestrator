import { ChangeDetectionStrategy, Component, ViewEncapsulation, input } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';

let next = 0;

/**
 * Ícone "?" ao lado de um campo: abre uma janela que explica o campo em português simples, com exemplo.
 * É o <dialog> nativo: fica por cima de tudo, até de um assistente aberto, prende o foco e fecha no Esc sem fechar o assistente.
 * Vai fora do <label> do campo, para o clique dentro da janela não cair no campo.
 * O conteúdo vem por projeção: parágrafos, uma lista (dl) e o exemplo (.ex) já têm o desenho aqui.
 */
@Component({
  selector: 'jo-help',
  imports: [Icon, Iso],
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (label(); as text) {
      <button type="button" class="btn ghost jh-text" (click)="box.showModal()"><jo-icon name="question" [size]="14" />{{ text }}</button>
    } @else {
      <button type="button" class="jh-q" [attr.aria-label]="'O que é ' + term() + '?'" [attr.title]="'O que é ' + term() + '?'" (click)="box.showModal()">
        <jo-icon name="question" [size]="13" />
      </button>
    }
    <dialog #box class="jh-box" [attr.aria-labelledby]="id" (click)="$event.target === box && box.close()" (keydown.escape)="$event.stopPropagation()">
      <div class="jh-in">
        <header class="jh-head" [class.bare]="!iso()">
          @if (iso(); as name) { <jo-iso [name]="name" [size]="56" /> }
          <h2 [id]="id">{{ term() }}</h2>
          <button type="button" class="btn ghost" (click)="box.close()" aria-label="Fechar"><jo-icon name="x" [size]="16" /></button>
        </header>
        <div class="jh-body"><ng-content /></div>
        <footer class="jh-foot"><button type="button" class="btn primary" (click)="box.close()">Entendi</button></footer>
      </div>
    </dialog>`,
  styles: `
    jo-help { display: inline-flex; vertical-align: middle; }
    .jh-q { display: inline-grid; place-items: center; width: 18px; height: 18px; padding: 0; border-radius: 50%; border: 1px solid var(--line-strong); background: transparent;
      color: var(--text-3); cursor: pointer; transition: color 140ms ease, border-color 140ms ease, background-color 140ms ease; }
    .jh-q:hover { color: var(--accent); border-color: var(--accent); background: var(--accent-weak); }
    .jh-q:focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }
    .jh-text { gap: 6px; }
    .jh-box { width: min(540px, calc(100% - 32px)); max-height: min(680px, calc(100% - 32px)); padding: 0; border: 1px solid var(--line-strong); border-radius: 8px;
      background: var(--surface); color: var(--text); box-shadow: 0 30px 80px oklch(0% 0 0 / 0.45); overflow: auto; overscroll-behavior: contain;
      font: 400 13px var(--font-ui); text-align: left; text-transform: none; letter-spacing: normal; white-space: normal; }
    .jh-box[open] { animation: jh-rise 0.26s cubic-bezier(0.2, 0.8, 0.2, 1); }
    .jh-box::backdrop { background: oklch(0% 0 0 / 0.5); backdrop-filter: blur(4px); }
    .jh-box[open]::backdrop { animation: jh-fade 0.2s ease-out; }
    .jh-in { display: grid; gap: 14px; padding: 16px 18px; }
    .jh-head { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 14px; align-items: center; }
    .jh-head.bare { grid-template-columns: minmax(0, 1fr) auto; }
    .jh-box .jh-head h2 { margin: 0; font-size: 16px; line-height: 1.3; }
    .jh-body { display: grid; gap: 10px; line-height: 1.6; color: var(--text-2); }
    .jh-body p { margin: 0; max-width: 62ch; }
    .jh-body strong { color: var(--text); font-weight: 600; }
    .jh-body dl { display: grid; grid-template-columns: max-content minmax(0, 1fr); gap: 6px 14px; margin: 0; }
    .jh-body dt { color: var(--text); font-weight: 600; }
    .jh-body dd { margin: 0; }
    .jh-body dl.stack { grid-template-columns: minmax(0, 1fr); row-gap: 2px; }
    .jh-body dl.stack dd + dt { margin-top: 8px; }
    .jh-body .ex { display: grid; gap: 4px; padding: 10px 12px; border-radius: 6px; background: var(--surface-2); border: 1px solid var(--line); }
    .jh-body .ex > span:first-child { font: 500 11px var(--font-ui); text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-3); }
    .jh-foot { display: flex; justify-content: flex-end; }
    @keyframes jh-rise { from { opacity: 0; transform: translateY(8px) scale(0.99); } }
    @keyframes jh-fade { from { opacity: 0; } }
    @media (prefers-reduced-motion: reduce) { .jh-box[open], .jh-box[open]::backdrop { animation: none; } }
    @media (max-width: 640px) {
      .jh-head jo-iso { display: none; } .jh-head { grid-template-columns: minmax(0, 1fr) auto; }
      .jh-body dl { grid-template-columns: minmax(0, 1fr); row-gap: 2px; } .jh-body dd + dt { margin-top: 6px; }
    }
  `,
})
export class Help {
  /** O nome do campo, como aparece na tela: vira o título da janela e o nome do botão para leitor de tela. */
  readonly term = input.required<string>();
  /** Figura isométrica do cabeçalho, como nos assistentes. */
  readonly iso = input<string | null>(null);
  /** Com texto, o gatilho vira um botão discreto ("Quem é quem") em vez do ícone sozinho. */
  readonly label = input<string | null>(null);
  protected readonly id = `jh-${++next}`;
}
