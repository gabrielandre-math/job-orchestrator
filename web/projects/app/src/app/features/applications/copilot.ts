import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { send } from '../../core/api';
import { BrowserCopilot } from '../../core/browser-copilot';
import { command } from '../../core/command';
import { query } from '../../core/query';

interface KitLine { question: string; answer: string; source: 'form' | 'ai' | 'draft' }
interface CopilotView { applyUrl: string; channel: string; hasResume: boolean; answers: { key: string; value: string }[]; firstName: string; lastName: string; email: string; phone: string; linkedIn: string; form: KitLine[] | null }
const SOURCE: Record<KitLine['source'], string> = { form: 'preenchido pelo app', ai: 'IA, pelo seu perfil', draft: 'rascunho da IA: revise' };
interface CoverLetter { ok: boolean; text: string; language: string; model: string; removed: string[]; error: string | null }

const LABEL: Record<string, string> = {
  full_name: 'Nome completo', email: 'E-mail', phone: 'Telefone', location: 'Onde você mora', linkedin_url: 'LinkedIn', github_url: 'GitHub', portfolio_url: 'Portfólio',
  work_authorization: 'Autorizado a trabalhar', needs_sponsorship: 'Precisa de visto', salary_expectation: 'Pretensão salarial', start_date: 'Quando pode começar',
  english_level: 'Inglês', relocation: 'Mudança de cidade', work_modality: 'Modalidade', education: 'Formação', how_heard: 'Como soube da vaga',
  salary_pj: 'Pretensão como PJ', salary_clt: 'Pretensão como CLT', headline: 'Título profissional', summary: 'Resumo (sobre você)',
};

/**
 * Copiloto: para a vaga que vai pelo site original (LinkedIn, Gupy, site da empresa). O app não clica por você: abre a vaga,
 * entrega o currículo da vaga, as respostas aprovadas para copiar e uma carta conferida; no fim você registra que enviou.
 */
@Component({
  selector: 'app-copilot',
  imports: [Icon, Iso, NgTemplateOutlet, RouterLink],
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
            <div><strong>Abra a vaga já preenchida</strong><p class="muted small">A vaga abre numa aba nova e o copiloto preenche o formulário, com o currículo desta vaga anexado. Confira e clique em enviar no site: a página de confirmação marca a candidatura como enviada.@if (!browser.status.value()?.connected && !browser.here()) { O copiloto ainda não respondeu: instale em <a routerLink="/configuracoes" fragment="copiloto">Configurações</a>. }</p></div>
            <button type="button" class="btn primary" (click)="browser.open({ id: applicationId(), applyUrl: v.applyUrl })"><jo-icon name="arrow-square-out" [size]="14" />Abrir preenchida</button>
          </li>
          <li class="wide">
            <jo-iso name="form" [size]="44" />
            <div><strong>O que escrever em cada campo</strong><p class="muted small">Para o que o copiloto não preencher: copie, ou ajuste antes de colar. As respostas vêm do seu perfil; nada aqui é inventado.</p></div>
            <ng-template #lines let-list>
              <ul class="answers">
                @for (a of list; track a.key) {
                  <li [class.draft]="a.source === 'draft'"><span class="k">{{ a.label }}<span class="src">{{ a.tag }}</span></span><span class="v">{{ a.value }}</span>
                    <button type="button" class="btn ghost" (click)="copy(a.key, a.value)" [attr.aria-label]="'Copiar ' + a.label"><jo-icon [name]="copied() === a.key ? 'check' : 'copy'" [size]="14" /></button></li>
                }
              </ul>
            </ng-template>
            @if (kit().form.length) {
              <ng-container *ngTemplateOutlet="lines; context: { $implicit: kit().form }" />
              @if (kit().profile.length) {
                <details class="others"><summary>Outras respostas do seu perfil ({{ kit().profile.length }})</summary>
                  <ng-container *ngTemplateOutlet="lines; context: { $implicit: kit().profile }" />
                </details>
              }
            } @else if (kit().profile.length) {
              <ng-container *ngTemplateOutlet="lines; context: { $implicit: kit().profile }" />
            } @else { <p class="faint small none">O formulário desta vaga ainda não foi lido. Abra preenchida: o copiloto lê as perguntas na página e a IA responde as que ficarem vazias.</p> }
          </li>
          <li class="wide">
            <jo-iso name="writer" [size]="44" />
            <div><strong>Carta de apresentação</strong><p class="muted small">Escrita só com fatos do seu currículo; o Conferente tira o que não tiver fonte. Use se a vaga pedir.</p>
              <div class="tone" role="group" aria-label="Tom e idioma da carta">
                @for (o of tones; track o.key) { <button type="button" class="chip" [attr.aria-pressed]="tone() === o.key" (click)="tone.set(o.key)">{{ o.label }}</button> }
                <span class="sep" aria-hidden="true"></span>
                @for (o of langs; track o.key) { <button type="button" class="chip" [attr.aria-pressed]="lang() === o.key" (click)="lang.set(o.key)">{{ o.label }}</button> }
              </div>
            </div>
            <button type="button" class="btn" (click)="write()" [disabled]="writing.running()">
              @if (writing.running()) { <span class="spinner sm" aria-hidden="true"></span>Escrevendo } @else { <jo-icon name="pen-nib" [size]="14" />{{ letter() ? 'Escrever de novo' : 'Escrever com IA' }} }
            </button>
            @if (letter(); as l) {
              @if (l.ok) {
                <textarea rows="9" [value]="text()" (input)="edit($any($event.target).value)"></textarea>
                <div class="row"><span class="faint small">por {{ l.model }}@if (l.removed.length) { · {{ l.removed.length }} frase(s) sem fonte saíram }</span>
                  <button type="button" class="btn ghost" (click)="copy('letter', text())"><jo-icon [name]="copied() === 'letter' ? 'check' : 'copy'" [size]="14" />Copiar a carta</button></div>
              } @else { <p class="warn small">{{ l.error }}</p> }
            }
          </li>
          <li>
            <jo-iso name="renderer" [size]="44" />
            <div><strong>Currículo desta vaga</strong><p class="muted small">PDF sob medida, conferido pelo Inspetor ATS. O copiloto já anexa; aqui, para anexar à mão.</p></div>
            @if (v.hasResume) { <a class="btn" [href]="'/api/applications/' + applicationId() + '/resume'" target="_blank" rel="noopener"><jo-icon name="download-simple" [size]="14" />Baixar o PDF</a> }
            @else { <span class="faint small">ainda sendo gerado</span> }
          </li>
          <li>
            <jo-iso name="send" [size]="44" />
            <div><strong>Enviou? Registre</strong><p class="muted small">Com o copiloto, a confirmação do site registra sozinha. Enviou por fora? Marque aqui e ela vai para Enviadas.</p></div>
            <button type="button" class="btn primary" (click)="declare()" [disabled]="declaring.running()"><jo-icon name="check" [size]="14" />Já enviei</button>
          </li>
        </ol>
      }
    </section>`,
  styles: `
    .tone { display: flex; flex-wrap: wrap; gap: 5px; align-items: center; margin-top: 8px; }
    .tone .sep { width: 1px; height: 16px; background: var(--line-strong); margin: 0 4px; }
    .copilot { overflow: hidden; }
    .small { font-size: 12px; }
    .steps { list-style: none; margin: 0; padding: 6px 0; display: grid; }
    .steps > li { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 14px; align-items: center; padding: 12px 16px; border-bottom: 1px solid var(--line); }
    .steps > li:last-child { border-bottom: 0; }
    .steps > li.wide { grid-template-columns: auto minmax(0, 1fr) auto; }
    .steps > li.wide > :is(ul, textarea, .row, p.warn, p.none, details) { grid-column: 2 / -1; }
    .others { margin-top: 4px; }
    .others > summary { cursor: pointer; width: fit-content; padding: 4px 0; color: var(--text-2); font-size: 12.5px; }
    .others > summary:hover, .others[open] > summary { color: var(--text); }
    .others > ul { margin-top: 6px; }
    .steps p { margin: 2px 0 0; }
    .answers { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .answers li { display: grid; grid-template-columns: 170px minmax(0, 1fr) auto; gap: 10px; align-items: center; padding: 4px 6px 4px 10px; border-radius: 4px; background: var(--surface-2); font-size: 12.5px; }
    .answers .k { color: var(--text-3); }
    .answers .v { overflow-wrap: anywhere; white-space: pre-line; }
    .answers .k { display: grid; gap: 2px; }
    .answers .src { font-size: 10.5px; color: var(--text-3); }
    .answers li.draft { background: color-mix(in oklch, var(--warn) 8%, var(--surface-2)); }
    .none { grid-column: 2 / -1; margin: 0; }
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
  protected readonly browser = inject(BrowserCopilot);
  protected readonly letter = signal<CoverLetter | null>(null);
  protected readonly text = signal('');
  protected readonly copied = signal<string | null>(null);
  /** Tom e idioma da carta (o idioma automático segue a descrição da vaga). */
  protected readonly tones = [{ key: 'direto', label: 'Direto' }, { key: 'formal', label: 'Formal' }, { key: 'caloroso', label: 'Caloroso' }];
  protected readonly langs = [{ key: '', label: 'Idioma da vaga' }, { key: 'pt', label: 'Português' }, { key: 'en', label: 'Inglês' }, { key: 'es', label: 'Espanhol' }];
  protected readonly tone = signal('direto');
  protected readonly lang = signal('');
  protected readonly writing = command((jobId: string) => send<CoverLetter>('POST', `/assistant/cover/${jobId}`, { tone: this.tone(), language: this.lang() || null }));
  protected readonly declaring = command((id: string) => send('POST', `/applications/${id}/declare`, {}), 'Registrada como enviada por você.');
  /**
   * The kit: the form's own questions (what the app filled, the AI's answers, its drafts to review), and your profile's answers the
   * form did not ask by name, folded away once the form has been read.
   */
  protected readonly kit = computed(() => {
    const v = this.view.value();
    if (!v) return { form: [], profile: [] };
    const form = (v.form ?? []).map((l, i) => ({ key: `f${i}`, label: l.question, value: l.answer, source: l.source, tag: SOURCE[l.source] }));
    // Your profile's answers by their readable name only, and never one the form lines already show (same question or same value).
    const asked = new Set(form.map((l) => l.label.trim().toLowerCase()));
    const shown = new Set(form.map((l) => l.value.trim().toLowerCase()));
    const profile = [];
    for (const a of v.answers) {
      const label = a.key.startsWith('years_experience:') ? `Anos com ${a.key.slice(17)}` : LABEL[a.key];
      const value = a.value.trim().toLowerCase();
      if (!label || asked.has(label.trim().toLowerCase()) || (value.length >= 6 && shown.has(value))) continue;
      shown.add(value);
      profile.push({ key: a.key, label, value: a.value, source: 'form' as const, tag: 'do seu perfil' });
    }
    return { form, profile };
  });
  protected async write() {
    const l = await this.writing.run(this.jobId());
    if (!l) return;
    this.letter.set(l);
    this.text.set(l.text);
    try { if (l.ok) sessionStorage.setItem(`jo.letter.${this.jobId()}`, JSON.stringify(l)); } catch { /* sem armazenamento */ }
  }
  /** The letter is ready when you open the kit: written once per job and kept for this browser session. */
  private readonly autoLetter = effect(() => {
    if (!this.view.value()) return;
    untracked(() => {
      if (this.letter() || this.writing.running()) return;
      try {
        const kept = sessionStorage.getItem(`jo.letter.${this.jobId()}`);
        if (kept) { const l = JSON.parse(kept) as CoverLetter; this.letter.set(l); this.text.set(l.text); return; }
      } catch { /* sem armazenamento */ }
      void this.write();
    });
  });
  protected edit(v: string) { this.text.set(v); }
  protected async copy(key: string, value: string) {
    try { await navigator.clipboard.writeText(value); this.copied.set(key); setTimeout(() => this.copied.update((c) => (c === key ? null : c)), 1500); } catch { /* sem área de transferência */ }
  }
  protected async declare() { if ((await this.declaring.run(this.applicationId())) !== null) this.done.emit(); }
}
