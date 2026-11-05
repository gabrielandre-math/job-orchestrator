import { ChangeDetectionStrategy, Component, computed, inject, output, signal } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Profile, get, upload } from '../../core/api';
import { command } from '../../core/command';
import { AGENT_ICON, Live } from '../../core/live';
import { Wizard } from '../../shared/wizard';

/**
 * Importação guiada do export do LinkedIn: pedir o arquivo (o que marcar), esperar o e-mail, enviar o ZIP e ver os agentes lendo.
 * O app nunca entra no LinkedIn por você: o arquivo é o caminho oficial para os seus dados.
 */
@Component({
  selector: 'app-linkedin-import',
  imports: [Icon, Iso, Wizard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <jo-wizard [steps]="['Pedir o arquivo', 'Esperar o e-mail', 'Enviar', 'Pronto']" [step]="step()" label="Importar do LinkedIn" (close)="close.emit()">
      @switch (step()) {
        @case (1) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="identity" [size]="96" />
              <div>
                <h2>Peça a cópia dos seus dados ao LinkedIn</h2>
                <p class="muted">É o caminho oficial: o LinkedIn gera um ZIP com o seu perfil. O app não entra na sua conta nem usa robô na rede social.</p>
              </div>
            </div>
            <a class="btn primary" href="https://www.linkedin.com/mypreferences/d/download-my-data" target="_blank" rel="noopener"><jo-icon name="arrow-square-out" [size]="14" />Abrir a página do LinkedIn</a>
            <ol class="how">
              <li>Escolha <strong>"Quer algo específico?"</strong> e marque Perfil, Cargos, Formação, Competências, Certificações, Idiomas, Projetos e Recomendações.</li>
              <li>Clique em <strong>Solicitar arquivo</strong> e confirme a senha. A opção do arquivo maior também serve, só demora mais.</li>
            </ol>
          </div>
        }
        @case (2) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="inbox" [size]="96" />
              <div>
                <h2>O LinkedIn avisa por e-mail</h2>
                <p class="muted">A parte específica costuma ficar pronta em cerca de 10 minutos; o arquivo completo, em até 24 horas. O link do e-mail baixa um arquivo chamado Basic_LinkedInDataExport.zip.</p>
              </div>
            </div>
            <p class="notice">Pode fechar esta janela e voltar depois: o botão Importar do LinkedIn continua no Perfil.</p>
          </div>
        }
        @case (3) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="reader" [size]="96" />
              <div><h2>Envie o ZIP como veio</h2><p class="muted">O Leitor tira os seus dados de contato antes de qualquer modelo ler, e os fatos se somam aos do currículo, cada um com a origem.</p></div>
            </div>
            <label class="drop" [class.busy]="busy()">
              <input type="file" accept=".zip" (change)="choose($event)" [disabled]="busy()" />
              @if (busy()) { <span class="spinner" aria-hidden="true"></span><strong>Lendo o export</strong> }
              @else { <jo-icon name="upload-simple" [size]="28" /><strong>Solte o ZIP aqui ou clique para escolher</strong> }
            </label>
            @if (feed().length > 0 && busy()) {
              <ol class="feed">@for (l of feed(); track l.message.id) { <li><jo-iso [name]="icons[l.message.agent] ?? 'router'" [size]="26" /><span><strong>{{ l.message.agent }}</strong> {{ l.message.title }}</span></li> }</ol>
            }
            @if (sending.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
          </div>
        }
        @case (4) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="checker" [size]="96" />
              <div><h2>{{ added() }} fatos novos no perfil</h2><p class="muted">Confira na lista do Perfil. Fato que veio só do LinkedIn mostra a origem e entra no currículo depois de confirmado.</p></div>
            </div>
            <div class="done-actions"><button type="button" class="btn primary" (click)="close.emit()">Concluir</button></div>
          </div>
        }
      }
      @if (step() < 3) {
        <footer class="wz-foot" wz-foot>
          @if (step() > 1) { <button type="button" class="btn ghost" (click)="step.set(step() - 1)"><jo-icon name="arrow-left" [size]="14" />Voltar</button> } @else { <span></span> }
          <span class="end"><button type="button" class="btn primary" (click)="step.set(step() + 1)">{{ step() === 1 ? 'Já pedi' : 'Já tenho o ZIP' }}<jo-icon name="arrow-right" [size]="14" /></button></span>
        </footer>
      }
    </jo-wizard>`,
  styles: `
    :host { display: contents; }
    .how { margin: 0; padding-left: 20px; display: grid; gap: 8px; line-height: 1.55; color: var(--text-2); }
    .drop { position: relative; display: grid; justify-items: center; gap: 6px; padding: 28px 18px; border-radius: 8px; border: 1.5px dashed var(--line-strong); background: var(--surface-2); cursor: pointer; transition: border-color 200ms ease, background-color 200ms ease; }
    .drop:hover { border-color: var(--accent); background: color-mix(in oklch, var(--accent) 6%, var(--surface-2)); }
    .drop.busy { cursor: progress; }
    .drop input { position: absolute; inset: 0; opacity: 0; cursor: pointer; }
    .drop jo-icon { color: var(--accent); }
    .feed { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .feed li { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 8px; align-items: center; font-size: 12.5px; }
  `,
})
export class LinkedInImport {
  private readonly live = inject(Live);
  readonly close = output<void>();
  readonly changed = output<void>();
  protected readonly step = signal(1);
  protected readonly busy = signal(false);
  protected readonly added = signal(0);
  protected readonly icons = AGENT_ICON;
  protected readonly sending = command((f: File) => upload<unknown>('/profile/documents', f));
  protected readonly feed = computed(() => this.live.feed().filter((l) => l.kind === 'profile').slice(-5).reverse());

  protected async choose(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const before = (await get<Profile>('/profile')).facts.length;
    this.busy.set(true);
    if ((await this.sending.run(file)) === null) { this.busy.set(false); return; }
    for (let i = 0; i < 120; i++) {
      await new Promise((r) => setTimeout(r, 1500));
      const p = await get<Profile>('/profile');
      const d = p.documents.at(-1);
      if (d && (d.status === 'Ready' || d.status === 'Failed')) { this.added.set(Math.max(0, p.facts.length - before)); break; }
    }
    this.busy.set(false);
    this.changed.emit();
    this.step.set(4);
  }
}
