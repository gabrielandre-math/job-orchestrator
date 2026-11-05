import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';

interface CopilotView { applyUrl: string; channel: string; hasResume: boolean; answers: { key: string; value: string }[]; firstName: string; lastName: string; email: string; phone: string; linkedIn: string }
interface CoverLetter { ok: boolean; text: string; language: string; model: string; removed: string[]; error: string | null }

const LABEL: Record<string, string> = {
  full_name: 'Nome completo', email: 'E-mail', phone: 'Telefone', location: 'Onde você mora', linkedin_url: 'LinkedIn', github_url: 'GitHub', portfolio_url: 'Portfólio',
  work_authorization: 'Autorizado a trabalhar', needs_sponsorship: 'Precisa de visto', salary_expectation: 'Pretensão salarial', start_date: 'Quando pode começar',
  english_level: 'Inglês', relocation: 'Mudança de cidade', work_modality: 'Modalidade', education: 'Formação', how_heard: 'Como soube da vaga',
};

/**
 * Copiloto: para a vaga que vai pelo site original (LinkedIn, Gupy, site da empresa). O app não clica por você: abre a vaga,
 * entrega o currículo da vaga, as respostas aprovadas para copiar e uma carta conferida; no fim você registra que enviou.
 */
@Component({
  selector: 'app-copilot',
  imports: [Icon, Iso],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel copilot">
      <header>
        <h2>Copiloto</h2>
        <span class="faint small">a vaga vai pelo site original; o app prepara, você envia</span>
      </header>
      @if (view.value(); as v) {
        <ol class="steps">
          <li>
            <jo-iso name="selector" [size]="44" />
            <div><strong>Abra a vaga</strong><p class="muted small">O site da vaga abre numa aba nova. Entre com a sua conta, se ele pedir.</p></div>
            <a class="btn" [href]="v.applyUrl" target="_blank" rel="noopener"><jo-icon name="arrow-square-out" [size]="14" />Abrir a vaga</a>
          </li>
          <li>
            <jo-iso name="renderer" [size]="44" />
            <div><strong>Anexe o currículo desta vaga</strong><p class="muted small">PDF sob medida, conferido pelo Inspetor ATS.</p></div>
            @if (v.hasResume) { <a class="btn" [href]="'/api/applications/' + applicationId() + '/resume'" target="_blank" rel="noopener"><jo-icon name="download-simple" [size]="14" />Baixar o PDF</a> }
            @else { <span class="faint small">ainda sendo gerado</span> }
          </li>
          <li class="wide">
            <jo-iso name="form" [size]="44" />
            <div><strong>Copie as respostas</strong><p class="muted small">Só respostas que você já aprovou no perfil. Pergunta nova fica com você.</p></div>
            <ul class="answers">
              @for (a of rows(); track a.key) {
                <li><span class="k">{{ a.label }}</span><span class="v">{{ a.value }}</span>
                  <button type="button" class="btn ghost" (click)="copy(a.key, a.value)" [attr.aria-label]="'Copiar ' + a.label"><jo-icon [name]="copied() === a.key ? 'check' : 'copy'" [size]="14" /></button></li>
              }
            </ul>
          </li>
          <li class="wide">
            <jo-iso name="writer" [size]="44" />
            <div><strong>Carta de apresentação, se a vaga pedir</strong><p class="muted small">Escrita só com fatos do seu currículo; o Conferente tira o que não tiver fonte.</p></div>
            <button type="button" class="btn" (click)="write()" [disabled]="writing.running()">
              @if (writing.running()) { <span class="spinner sm" aria-hidden="true"></span>Escrevendo } @else { <jo-icon name="pen-nib" [size]="14" />{{ letter() ? 'Escrever de novo' : 'Escrever com IA' }} }
            </button>
            @if (letter(); as l) {
              @if (l.ok) {
                <textarea rows="9" [value]="l.text" (input)="edit($any($event.target).value)"></textarea>
                <div class="row"><span class="faint small">por {{ l.model }}@if (l.removed.length) { · {{ l.removed.length }} frase(s) sem fonte saíram }</span>
                  <button type="button" class="btn ghost" (click)="copy('letter', text())"><jo-icon [name]="copied() === 'letter' ? 'check' : 'copy'" [size]="14" />Copiar a carta</button></div>
              } @else { <p class="warn small">{{ l.error }}</p> }
            }
          </li>
          <li class="wide">
            <jo-iso name="form" [size]="44" />
            <div><strong>Ou deixe a extensão preencher</strong><p class="muted small">Com a extensão do copiloto no Chromium, um clique na página da vaga preenche os campos vazios com estas respostas e anexa o currículo; você confere e envia. Ela não roda no LinkedIn. Para ligar: chrome://extensions, modo de desenvolvedor, "Carregar sem compactação" e a pasta <span class="mono">~/.local/opt/job-orchestrator/extension</span>.</p></div>
            <button type="button" class="btn ghost" (click)="copy('ext', 'chrome://extensions')"><jo-icon [name]="copied() === 'ext' ? 'check' : 'copy'" [size]="14" />Copiar endereço</button>
          </li>
          <li>
            <jo-iso name="send" [size]="44" />
            <div><strong>Enviou? Registre</strong><p class="muted small">Fica como enviada por você (evidência nível 2), separada das confirmadas pelo ATS.</p></div>
            <button type="button" class="btn primary" (click)="declare()" [disabled]="declaring.running()"><jo-icon name="check" [size]="14" />Já enviei</button>
          </li>
        </ol>
      }
    </section>`,
  styles: `
    .copilot { overflow: hidden; }
    .small { font-size: 12px; }
    .steps { list-style: none; margin: 0; padding: 6px 0; display: grid; }
    .steps > li { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 14px; align-items: center; padding: 12px 16px; border-bottom: 1px solid var(--line); }
    .steps > li:last-child { border-bottom: 0; }
    .steps > li.wide { grid-template-columns: auto minmax(0, 1fr) auto; }
    .steps > li.wide > :is(ul, textarea, .row, p.warn) { grid-column: 2 / -1; }
    .steps p { margin: 2px 0 0; }
    .answers { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .answers li { display: grid; grid-template-columns: 170px minmax(0, 1fr) auto; gap: 10px; align-items: center; padding: 4px 6px 4px 10px; border-radius: 4px; background: var(--surface-2); font-size: 12.5px; }
    .answers .k { color: var(--text-3); }
    .answers .v { overflow-wrap: anywhere; }
    textarea { font-family: var(--font-ui); line-height: 1.6; }
    .row { display: flex; justify-content: space-between; align-items: center; gap: 8px; }
    .warn { color: var(--warn); margin: 0; }
    @media (max-width: 760px) { .steps > li { grid-template-columns: auto minmax(0, 1fr); } .steps > li > .btn, .steps > li > a.btn { grid-column: 2; justify-self: start; } .answers li { grid-template-columns: 1fr auto; } .answers .k { grid-column: 1 / -1; } }
  `,
})
export class Copilot {
  readonly applicationId = input.required<string>();
  readonly jobId = input.required<string>();
  readonly done = output<void>();
  protected readonly view = query<CopilotView>(() => `/applications/${this.applicationId()}/copilot`, 0);
  protected readonly letter = signal<CoverLetter | null>(null);
  protected readonly text = signal('');
  protected readonly copied = signal<string | null>(null);
  protected readonly writing = command((jobId: string) => send<CoverLetter>('POST', `/assistant/cover/${jobId}`));
  protected readonly declaring = command((id: string) => send('POST', `/applications/${id}/declare`, {}), 'Registrada como enviada por você.');
  protected readonly rows = computed(() => (this.view.value()?.answers ?? []).map((a) => ({ ...a, label: a.key.startsWith('years_experience:') ? `Anos com ${a.key.slice(17)}` : LABEL[a.key] ?? a.key })));

  protected async write() { const l = await this.writing.run(this.jobId()); if (l) { this.letter.set(l); this.text.set(l.text); } }
  protected edit(v: string) { this.text.set(v); }
  protected async copy(key: string, value: string) {
    try { await navigator.clipboard.writeText(value); this.copied.set(key); setTimeout(() => this.copied.update((c) => (c === key ? null : c)), 1500); } catch { /* sem área de transferência */ }
  }
  protected async declare() { if ((await this.declaring.run(this.applicationId())) !== null) this.done.emit(); }
}
