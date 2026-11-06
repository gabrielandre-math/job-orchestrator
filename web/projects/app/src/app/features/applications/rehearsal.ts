import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { send } from '../../core/api';
import { command } from '../../core/command';
import { ago } from '../../core/format';
import { query } from '../../core/query';

interface Field { label: string; value: string; source: string }
interface Item { jobId: string; company: string; title: string; url: string; eligibility: string | null; kind: string; reason: string | null; detail: string | null; adapter: string | null; fields: Field[]; seconds: number }
interface View { startedAt: string; finishedAt: string | null; running: boolean; total: number; error: string | null; items: Item[] }
type Verdict = 'ok' | 'you' | 'gap' | 'closed' | 'error';

/** O veredito de cada formulário, em palavras de quem não é técnico. */
const VERDICT: Record<Verdict, { label: string; tone: string }> = {
  ok: { label: 'Pronto para enviar', tone: 'ok' },
  you: { label: 'Precisa de você', tone: 'warn' },
  gap: { label: 'Falta uma resposta', tone: 'warn' },
  closed: { label: 'Vaga fora do ar', tone: 'muted' },
  error: { label: 'O preenchedor falhou', tone: 'bad' },
};

/**
 * Ensaio dos formulários: preenche de verdade o formulário de cada vaga da fila, com o seu perfil e o currículo, e lê de volta,
 * sem clicar em enviar. Mostra o veredito por vaga e, aberto, cada campo com o valor e de onde ele veio.
 */
@Component({
  selector: 'app-rehearsal',
  imports: [Company, Icon, Iso],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel rh" [class.busy]="running()">
      <header class="rh-head">
        <span class="mark"><jo-iso name="form" [size]="52" /></span>
        <div>
          <h2>Ensaio dos formulários</h2>
          <p class="muted small">Abre o formulário de cada vaga da fila, preenche com o seu perfil e o currículo e confere campo por campo. Não clica em enviar:
            nada sai daqui. Assim você sabe, antes do automático, o que vai sair sozinho e o que vai parar em você.</p>
        </div>
        <button type="button" class="btn primary" (click)="go()" [disabled]="running()">
          @if (running()) { <span class="spinner sm" aria-hidden="true"></span>Ensaiando… } @else { <jo-icon name="play" [size]="14" />{{ last() ? 'Ensaiar de novo' : 'Ensaiar agora' }} }
        </button>
      </header>
      @if (last(); as v) {
        @if (v.error) { <p class="problem rh-pad">{{ v.error }}</p> }
        <div class="tally">
          @for (t of tally(); track t.key) { <span [class]="'t ' + t.tone"><strong class="num">{{ t.n }}</strong>{{ t.label }}</span> }
          <span class="faint small when">{{ v.running ? 'ensaiando ' + v.items.length + ' de ' + v.total : 'último ensaio ' + ago(v.finishedAt ?? v.startedAt) }}</span>
        </div>
        @if (v.running) { <div class="bar" aria-hidden="true"><span [style.width.%]="v.total ? (v.items.length / v.total) * 100 : 0"></span></div> }
        <ul class="list">
          @for (i of v.items; track i.jobId) {
            <li [class]="verdict(i)">
              <button type="button" class="row" [attr.aria-expanded]="open() === i.jobId" (click)="open.set(open() === i.jobId ? null : i.jobId)">
                <jo-company [name]="i.company" [size]="28" />
                <span class="what"><strong>{{ i.title }}</strong><span class="faint small">{{ i.company }}@if (i.adapter) { · {{ adapterName(i.adapter) }} } · {{ i.seconds }} s</span></span>
                <span [class]="'pill ' + verdictOf(i).tone">{{ verdictOf(i).label }}</span>
                <jo-icon name="caret-down" [size]="12" class="caret" />
              </button>
              @if (open() === i.jobId) {
                <div class="more">
                  @if (verdict(i) !== 'ok') { <p class="why">{{ explain(i) }}</p> }
                  @if (verdict(i) === 'gap') {
                    <div class="ask">
                      @for (q of questions(i); track q) {
                        <div class="q">
                          <span class="qt">{{ q }}</span>
                          @if (saved()[q]; as a) { <span class="done"><jo-icon name="check" [size]="13" />Respondido: {{ a }}</span> }
                          @else {
                            @if (yesNo(q)) {
                              <span class="yn"><button type="button" class="btn" (click)="answer(q, 'Sim')" [disabled]="saving.running()">Sim</button><button type="button" class="btn" (click)="answer(q, 'Não')" [disabled]="saving.running()">Não</button></span>
                            } @else {
                              <span class="tx"><input #box type="text" [attr.aria-label]="q" placeholder="Sua resposta" (keydown.enter)="answer(q, box.value)" /><button type="button" class="btn" (click)="answer(q, box.value)" [disabled]="saving.running()">Salvar</button></span>
                            }
                          }
                        </div>
                      }
                      <p class="faint small">A resposta fica guardada no seu PC e vale para todo formulário com a mesma pergunta. Depois, ensaie de novo.</p>
                    </div>
                  }
                  @if (i.fields.length > 0) {
                    <table class="fields">
                      <thead><tr><th>Campo</th><th>O que entrou</th><th>De onde veio</th></tr></thead>
                      <tbody>@for (f of i.fields; track f.label) { <tr><td>{{ f.label }}</td><td class="v">{{ f.value }}</td><td class="faint">{{ f.source }}</td></tr> }</tbody>
                    </table>
                  }
                  <a class="btn ghost" [href]="i.url" target="_blank" rel="noopener"><jo-icon name="arrow-square-out" [size]="13" />Abrir o formulário</a>
                </div>
              }
            </li>
          }
        </ul>
      }
    </section>`,
  styles: `
    :host { display: block; }
    .rh-head { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 14px; align-items: center; padding: 14px 16px; }
    .rh-head h2 { font-size: 15px; margin: 0 0 4px; }
    .rh-head p { margin: 0; line-height: 1.55; max-width: 90ch; }
    .busy .mark jo-iso { animation: rh-bob 1.6s ease-in-out infinite; }
    .rh-pad { margin: 0 16px 10px; }
    .tally { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; padding: 0 16px 12px; }
    .t { display: inline-flex; gap: 6px; align-items: baseline; padding: 4px 10px; border-radius: 4px; background: var(--surface-2); font-size: 12.5px; color: var(--text-2); }
    .t strong { font-size: 14px; color: var(--text); }
    .t.ok strong { color: var(--ok); } .t.warn strong { color: var(--warn); } .t.bad strong { color: var(--danger); }
    .when { margin-left: auto; }
    .bar { height: 3px; margin: 0 16px 10px; background: var(--line); border-radius: 2px; overflow: hidden; }
    .bar span { display: block; height: 100%; background: var(--accent); transition: width 400ms ease; }
    .list { list-style: none; margin: 0; padding: 0; border-top: 1px solid var(--line); }
    .list > li { border-bottom: 1px solid var(--line); }
    .list > li:last-child { border-bottom: 0; }
    .list > li.ok { box-shadow: inset 3px 0 0 var(--ok); } .list > li.you, .list > li.gap { box-shadow: inset 3px 0 0 var(--warn); } .list > li.error { box-shadow: inset 3px 0 0 var(--danger); }
    .row { width: 100%; display: grid; grid-template-columns: auto minmax(0, 1fr) auto auto; gap: 12px; align-items: center; padding: 10px 16px; border: 0; background: transparent; color: inherit; font: inherit; text-align: left; cursor: pointer; }
    .row:hover { background: var(--surface-2); }
    .row[aria-expanded="true"] .caret { transform: rotate(180deg); }
    .caret { transition: transform 160ms ease; color: var(--text-3); }
    .what { display: grid; gap: 1px; min-width: 0; }
    .what strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .pill { padding: 2px 8px; border-radius: 3px; font: 500 11.5px var(--font-ui); white-space: nowrap; background: var(--surface-3); color: var(--text-2); }
    .pill.ok { background: color-mix(in oklch, var(--ok) 15%, transparent); color: var(--ok); }
    .pill.warn { background: color-mix(in oklch, var(--warn) 15%, transparent); color: var(--warn); }
    .pill.bad { background: color-mix(in oklch, var(--danger) 15%, transparent); color: var(--danger); }
    .more { display: grid; gap: 10px; justify-items: start; padding: 0 16px 14px 56px; }
    .why { margin: 0; font-size: 12.5px; line-height: 1.5; color: var(--text-2); max-width: 80ch; }
.ask { display: grid; gap: 8px; width: 100%; max-width: 760px; }
    .q { display: grid; gap: 6px; padding: 10px 12px; border-radius: 6px; border: 1px solid var(--line); background: var(--surface-2); }
    .qt { font-size: 13px; color: var(--text); line-height: 1.45; }
    .yn, .tx { display: flex; gap: 6px; align-items: center; }
    .tx input { flex: 1; height: 30px; }
    .yn .btn, .tx .btn { height: 30px; }
    .done { display: inline-flex; gap: 6px; align-items: center; font-size: 12.5px; color: var(--ok); }
    .fields { width: 100%; border-collapse: collapse; font-size: 12px; }
    .fields th { text-align: left; font: 500 10.5px var(--font-mono); letter-spacing: 0.06em; text-transform: uppercase; color: var(--text-3); padding: 4px 8px; border-bottom: 1px solid var(--line); }
    .fields td { padding: 5px 8px; border-bottom: 1px solid var(--line); vertical-align: top; }
    .fields .v { color: var(--text); word-break: break-word; }
    .spinner.sm { vertical-align: -1px; }
    @keyframes rh-bob { 50% { transform: translateY(-3px); } }
    @media (max-width: 760px) { .rh-head { grid-template-columns: minmax(0, 1fr); } .rh-head .mark { display: none; } .more { padding-left: 16px; } }
    @media (prefers-reduced-motion: reduce) { .busy .mark jo-iso { animation: none; } }
  `,
})
export class Rehearsal {
  protected readonly ago = ago;
  private readonly state = query<{ running: boolean; last: View | null }>(() => '/applications/rehearsal', 2000);
  protected readonly last = computed(() => this.state.value()?.last ?? null);
  private readonly starting = signal(false);
  protected readonly running = computed(() => !!this.state.value()?.running || this.starting());
  protected readonly open = signal<string | null>(null);
  private readonly starter = command((limit: number) => send<void>('POST', '/applications/rehearsal', { limit }), 'O ensaio começou: cada formulário aparece aqui conforme termina.');

  protected readonly tally = computed(() => {
    const items = this.last()?.items ?? [];
    const n = (v: Verdict) => items.filter((i) => this.verdict(i) === v).length;
    return (['ok', 'you', 'gap', 'closed', 'error'] as Verdict[]).filter((v) => n(v) > 0).map((v) => ({ key: v, n: n(v), label: VERDICT[v].label.toLowerCase(), tone: VERDICT[v].tone }));
  });

  protected verdict(i: Item): Verdict {
    if (i.kind === 'Completed') return 'ok';
    if (i.kind === 'NeedsHuman') return 'you';
    if (i.kind === 'PermanentFailure' && i.reason === 'posting-closed') return 'closed';
    if (i.reason === 'unsupported-field' || i.reason === 'unsupported-option') return 'gap';
    return 'error';
  }
  protected verdictOf(i: Item) { return VERDICT[this.verdict(i)]; }
  protected adapterName(id: string) { return ({ 'greenhouse-form': 'Greenhouse', 'lever-form': 'Lever', 'ashby-form': 'Ashby' } as Record<string, string>)[id] ?? id; }

  /** O que o preenchedor disse, com o próximo passo. */
  protected explain(i: Item) {
    const said = i.detail ?? '';
    switch (this.verdict(i)) {
      case 'you': return `${said} No envio de verdade, esta vaga vai parar na sua lista para você terminar.`;
      case 'gap': return 'O formulário faz perguntas que o seu perfil não responde. Responda aqui embaixo uma vez: o app guarda e usa nas próximas vagas.';
      case 'closed': return 'A empresa tirou a vaga do ar: não há o que enviar.';
      default: return `${said || 'O preenchedor não terminou.'} Este é um problema do app, não seu: fica anotado para corrigir.`;
    }
  }

  /** As perguntas sem resposta que o preenchedor listou no detalhe, entre aspas. */
  protected questions(i: Item) { return [...(i.detail ?? '').matchAll(/"([^"]+)"/g)].map((x) => x[1]); }
  /** Pergunta de sim ou não: abre os dois botões em vez do campo de texto. */
  protected yesNo(q: string) { return /^(do|does|are|is|have|has|can|will|would|did|você|voce|possui|tem|já|ja|é|e)\b/i.test(q.trim()); }
  /** Respondidas nesta tela, para o botão virar "Respondido" sem esperar o próximo ensaio. */
  protected readonly saved = signal<Record<string, string>>({});
  protected readonly saving = command((x: { question: string; answer: string }) => send<void>('PUT', '/applications/answers', x), 'Resposta guardada. Vale para todos os formulários com essa pergunta.');
  protected async answer(question: string, value: string) {
    const answer = value.trim();
    if (!answer) return;
    if ((await this.saving.run({ question, answer })) !== null) this.saved.update((s) => ({ ...s, [question]: answer }));
  }

  protected async go() {
    this.starting.set(true);
    await this.starter.run(25);
    await this.state.reload();
    this.starting.set(false);
  }
}
