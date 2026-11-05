import { ChangeDetectionStrategy, Component, output, signal } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';

type Stage = 'choose' | 'stopping' | 'stopped' | 'dev';

/**
 * Sair do app. Fechar a janela deixa a busca rodando em segundo plano (o normal); "Parar tudo" encerra o servidor, o Worker e o banco
 * próprio, e nada roda até o app ser aberto de novo. A parada vai com um cabeçalho próprio: um site aberto em outra aba não consegue pedir.
 */
@Component({
  selector: 'app-quit',
  imports: [Icon, Iso],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="backdrop" (click)="stage() === 'choose' && close.emit()"></div>
    <section class="dialog" role="dialog" aria-modal="true" aria-labelledby="quit-title">
      @switch (stage()) {
        @case ('choose') {
          <header><h2 id="quit-title">Sair do Job Orchestrator</h2><button type="button" class="x" (click)="close.emit()" aria-label="Fechar"><jo-icon name="x" [size]="16" /></button></header>
          <div class="options">
            <button type="button" class="option" (click)="hide()">
              <jo-iso name="radar" [size]="56" />
              <span><strong>Fechar só a janela</strong><span class="muted small">A busca e o Explorador seguem em segundo plano. Para voltar, abra o app pelo atalho.</span></span>
            </button>
            <button type="button" class="option stop" (click)="stop()">
              <jo-iso name="gate" [size]="56" />
              <span><strong>Parar tudo</strong><span class="muted small">Para a busca, o Worker e o banco. Nada roda até você abrir o app de novo; a etapa em andamento termina antes.</span></span>
            </button>
          </div>
          <footer><button type="button" class="btn ghost" (click)="close.emit()">Cancelar</button></footer>
        }
        @case ('stopping') {
          <div class="state"><span class="spinner" aria-hidden="true"></span><p>Parando os serviços com calma…</p></div>
        }
        @case ('stopped') {
          <div class="state"><jo-iso name="gate" [size]="72" /><h2>Tudo parado</h2><p class="muted">Pode fechar esta janela. Para voltar, abra o Job Orchestrator pelo atalho.</p>
            <button type="button" class="btn" (click)="hide()">Fechar a janela</button></div>
        }
        @case ('dev') {
          <div class="state"><jo-iso name="router" [size]="72" /><h2>Modo de desenvolvimento</h2><p class="muted">Aqui a API e o Worker rodam pelo terminal: pare por lá (Ctrl+C) ou pelo job-orchestrator --stop.</p>
            <button type="button" class="btn" (click)="close.emit()">Entendi</button></div>
        }
      }
    </section>`,
  styles: `
    :host { position: fixed; inset: 0; z-index: 60; display: grid; place-items: center; padding: 16px; }
    .backdrop { position: absolute; inset: 0; background: oklch(0% 0 0 / 0.5); backdrop-filter: blur(6px); animation: fade 0.2s ease-out; }
    .dialog { position: relative; width: min(560px, 100%); border-radius: 8px; border: 1px solid var(--line-strong); background: var(--surface); box-shadow: 0 30px 80px oklch(0% 0 0 / 0.45);
      animation: rise 0.3s cubic-bezier(0.2, 0.8, 0.2, 1); overflow: hidden; }
    header { display: flex; align-items: center; justify-content: space-between; padding: 16px 18px; border-bottom: 1px solid var(--line); }
    h2 { margin: 0; font-size: 16px; font-weight: 620; }
    .x { border: 0; background: none; color: var(--text-2); cursor: pointer; padding: 4px; border-radius: 4px; }
    .x:hover { color: var(--text); background: var(--surface-2); }
    .options { display: grid; gap: 10px; padding: 16px 18px; }
    .option { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 14px; align-items: center; padding: 12px 14px; border-radius: 6px; border: 1px solid var(--line-strong);
      background: var(--surface); color: var(--text); font: inherit; text-align: left; cursor: pointer; transition: border-color 160ms ease, background 160ms ease; }
    .option:hover { border-color: var(--accent); background: var(--accent-weak); }
    .option.stop:hover { border-color: var(--warn); background: var(--warn-weak); }
    .option span { display: grid; gap: 3px; }
    footer { display: flex; justify-content: flex-end; padding: 10px 18px 14px; border-top: 1px solid var(--line); }
    .state { display: grid; justify-items: center; gap: 10px; padding: 30px 24px; text-align: center; }
    .state p { margin: 0; max-width: 40ch; }
    .spinner { width: 26px; height: 26px; border-radius: 50%; border: 2.5px solid var(--surface-3); border-top-color: var(--accent); animation: spin 0.9s linear infinite; }
    @keyframes fade { from { opacity: 0; } }
    @keyframes rise { from { opacity: 0; transform: translateY(10px) scale(0.99); } }
    @keyframes spin { to { transform: rotate(360deg); } }
    @media (prefers-reduced-motion: reduce) { .backdrop, .dialog, .spinner { animation: none; } }
  `,
})
export class Quit {
  readonly close = output<void>();
  protected readonly stage = signal<Stage>('choose');

  protected hide() { window.close(); this.close.emit(); }

  protected async stop() {
    this.stage.set('stopping');
    try {
      const r = await fetch('/api/app/quit', { method: 'POST', headers: { 'X-Jo-App': 'quit' } });
      if (r.status === 404) { this.stage.set('dev'); return; }
    } catch { /* o servidor já saiu */ }
    // Espera a API sumir de fato antes de dizer que parou.
    for (let i = 0; i < 40; i++) {
      await new Promise((r) => setTimeout(r, 500));
      try { await fetch('/api/health', { cache: 'no-store' }); } catch { this.stage.set('stopped'); return; }
    }
    this.stage.set('stopped');
  }
}
