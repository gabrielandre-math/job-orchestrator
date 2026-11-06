import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Adapter, Campaign, get, send } from '../../core/api';
import { command } from '../../core/command';
import { Help } from '../../shared/help';
import { Wizard } from '../../shared/wizard';

/**
 * Autorizar o envio automático em quatro passos: o que muda, os limites (validade, teto por dia, nota mínima, quais formulários),
 * o texto do consentimento e pronto. Só nasce por aqui: o arquivo de configuração não liga o automático.
 */
@Component({
  selector: 'app-authorize',
  imports: [Icon, Iso, Wizard, Help],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <jo-wizard [steps]="['O que muda', 'Limites', 'Consentimento', 'Pronto']" [step]="step()" label="Autorizar o envio automático" (close)="close.emit()">
      @switch (step()) {
        @case (1) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="gate" [size]="96" />
              <div>
                <h2>Envio automático, dentro de limites</h2>
                <p class="muted">Com a autorização, o Guardião envia sozinho o formulário que o adapter preparou, sem esperar o seu ok a cada vaga. Fora dos limites, a vaga volta para a sua revisão. Você revoga quando quiser.</p>
              </div>
            </div>
            <ul class="rules">
              <li><jo-icon name="lock-simple" [size]="15" />Só nesta campanha, só com nota a partir do mínimo, só pelos formulários escolhidos.</li>
              <li><jo-icon name="lock-simple" [size]="15" />Teto por dia, uma empresa por dia e nada de reenviar sem reconciliar.</li>
              <li><jo-icon name="lock-simple" [size]="15" />CAPTCHA, login, MFA, declaração legal e pergunta nova sempre param para você.</li>
              <li><jo-icon name="lock-simple" [size]="15" />Cada envio fica registrado com prova (página de confirmação ou recibo).</li>
            </ul>
          </div>
        }
        @case (2) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="guardian" [size]="96" />
              <div><h2>Os limites</h2><p class="muted">Comece pequeno: dá para ampliar depois de ver os primeiros envios.</p></div>
            </div>
            <div class="limits">
              <div class="field">
                <div class="lh"><label for="auth-days">Validade</label>
                  <jo-help term="Validade" iso="receipt">
                    <p>Por quantos dias o sistema pode enviar candidaturas sozinho, contando a partir de agora.</p>
                    <p>Quando o prazo acaba, o envio automático desliga. As vagas continuam sendo preparadas, mas cada uma espera você aprovar. Para voltar ao automático, é só autorizar de novo.</p>
                    <p>Você pode desligar antes, a qualquer hora, no botão <strong>Revogar</strong> da Campanha. As vagas que estavam prontas voltam para a sua aprovação.</p>
                    <p class="ex"><span>Com o valor escolhido</span>{{ days() }} {{ days() === 1 ? 'dia' : 'dias' }}: o automático vale até {{ until() }}.</p>
                  </jo-help>
                  <span class="num accent">{{ days() }} {{ days() === 1 ? 'dia' : 'dias' }}</span></div>
                <input id="auth-days" type="range" min="1" max="30" [value]="days()" (input)="days.set(num($event))" />
              </div>
              <div class="field">
                <div class="lh"><label for="auth-cap">Teto por dia</label>
                  <jo-help term="Teto por dia" iso="queue">
                    <p>O máximo de candidaturas que podem sair num mesmo dia com o automático ligado. A conta do dia inclui as que você aprovou à mão.</p>
                    <p>Quando o teto enche, as vagas prontas que sobraram não se perdem: ficam na fila e saem a partir da meia-noite, no fuso da campanha ({{ campaign().timeZone }}), dentro do teto do dia novo.</p>
                    <p>No automático, cada empresa recebe no máximo uma candidatura a cada 24 horas.</p>
                    <p>Fica dentro do limite geral da campanha ({{ campaign().maxSubmissionsPerDay }} por dia) e nunca passa de 15.</p>
                    <p class="ex"><span>Com o valor escolhido</span>Teto {{ perDay() }}: se 20 vagas ficarem prontas hoje, {{ perDay() }} saem hoje e as outras saem nos próximos dias, {{ perDay() }} por dia.</p>
                  </jo-help>
                  <span class="num accent">{{ perDay() }}</span></div>
                <input id="auth-cap" type="range" min="1" [max]="maxCap()" [value]="perDay()" (input)="perDay.set(num($event))" />
              </div>
              <div class="field">
                <div class="lh"><label for="auth-score">Nota mínima</label>
                  <jo-help term="Nota mínima" iso="chart">
                    <p>Toda vaga ganha uma nota de 0 a 100. Quanto maior, mais ela combina com os cargos, as palavras-chave e os locais da campanha.</p>
                    <p>No automático, só sai sozinha a vaga com nota igual ou maior que esta. A vaga com nota menor também é preparada, mas espera você aprovar.</p>
                    <p>Não pode ficar abaixo da nota de corte da campanha ({{ campaign().minFitScore }}), que decide quais vagas entram na fila.</p>
                    <p class="ex"><span>Exemplo</span>Corte da campanha 80 e nota mínima 90. A vaga com nota 95 sai sozinha. A vaga com 85 é preparada e espera você. A vaga com 70 nem entra na fila.</p>
                  </jo-help>
                  <span class="num accent">{{ minScore() }}</span></div>
                <input id="auth-score" type="range" [min]="campaign().minFitScore" max="100" [value]="minScore()" (input)="minScore.set(num($event))" />
              </div>
            </div>
            <div class="field">
              <div class="lh">Formulários que podem enviar
                <jo-help term="Formulários que podem enviar" iso="form">
                  <p>Cada sistema de vagas que as empresas usam tem um formulário diferente, e o sistema tem um preenchedor para cada um.</p>
                  <p>Aqui você escolhe por quais desses formulários o envio pode sair sozinho. O número embaixo de cada nome mostra quantos envios por ele já foram confirmados, para você escolher os que já provaram que funcionam.</p>
                  <p>A vaga de um formulário que não está marcado também é preparada, mas espera você aprovar.</p>
                  <p class="ex"><span>Com todos marcados</span>A autorização vale também para formulários novos que entrarem depois.</p>
                </jo-help></div>
              <div class="grid">
                @for (a of forms(); track a.id) {
                  <button type="button" class="tile" [attr.aria-pressed]="picked().has(a.id)" (click)="toggle(a.id)">
                    <span class="tick"><jo-icon name="check" [size]="12" /></span>
                    <strong>{{ a.name }}</strong><span class="faint small">{{ a.confirmed }} de {{ a.attempts }} confirmados</span>
                  </button>
                }
              </div>
            </div>
          </div>
        }
        @case (3) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="you" [size]="96" />
              <div><h2>Consentimento</h2><p class="muted">Leia com calma. A autorização vale {{ days() }} {{ days() === 1 ? 'dia' : 'dias' }}, até {{ perDay() }} por dia, nota a partir de {{ minScore() }}.</p></div>
            </div>
            <label class="consent" [class.on]="accepted()">
              <input type="checkbox" [checked]="accepted()" (change)="accepted.set($any($event.target).checked)" />
              <span><strong>Autorizo</strong><p>{{ text() }}</p></span>
            </label>
            @if (granting.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
          </div>
        }
        @case (4) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="send" [size]="96" />
              <div><h2>Envio automático ligado</h2><p class="muted">As próximas vagas preparadas dentro dos limites saem sozinhas. Acompanhe em Candidaturas e na Busca ao vivo.</p></div>
            </div>
            <div class="done-actions"><button type="button" class="btn primary" (click)="close.emit()">Concluir</button></div>
          </div>
        }
      }
      @if (step() < 4) {
        <footer class="wz-foot" wz-foot>
          @if (step() > 1) { <button type="button" class="btn ghost" (click)="step.set(step() - 1)"><jo-icon name="arrow-left" [size]="14" />Voltar</button> } @else { <span></span> }
          <span class="end">
            @if (step() < 3) { <button type="button" class="btn primary" (click)="step.set(step() + 1)" [disabled]="step() === 2 && picked().size === 0">Continuar<jo-icon name="arrow-right" [size]="14" /></button> }
            @else { <button type="button" class="btn primary" (click)="grant()" [disabled]="!accepted() || granting.running()">Autorizar</button> }
          </span>
        </footer>
      }
    </jo-wizard>`,
  styles: `
    :host { display: contents; }
    .rules { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .rules li { display: flex; gap: 10px; align-items: flex-start; padding: 10px 12px; border-radius: 6px; background: var(--surface-2); font-size: 13px; line-height: 1.5; }
    .rules jo-icon { color: var(--ok); margin-top: 2px; }
    .limits { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; }
    .limits .field { display: grid; gap: 6px; font-size: 12px; color: var(--text-2); }
    .lh { display: flex; align-items: center; gap: 6px; margin-bottom: 6px; }
    .limits .lh { margin-bottom: 0; }
    .accent { color: var(--accent); margin-left: auto; }
    input[type="range"] { padding: 0; height: 20px; accent-color: var(--accent); border: 0; background: transparent; box-shadow: none !important; }
    .consent { display: flex; gap: 12px; align-items: flex-start; padding: 14px 16px; border-radius: 6px; border: 1px solid color-mix(in oklch, var(--warn) 45%, var(--line)); background: color-mix(in oklch, var(--warn) 6%, var(--surface-2)); cursor: pointer; transition: border-color 200ms ease, background-color 200ms ease; }
    .consent.on { border-color: color-mix(in oklch, var(--ok) 50%, var(--line)); background: color-mix(in oklch, var(--ok) 6%, var(--surface-2)); }
    .consent p { margin: 4px 0 0; color: var(--text-2); font-size: 12.5px; line-height: 1.55; }
    .small { font-size: 12px; }
    @media (max-width: 640px) { .limits { grid-template-columns: 1fr; } }
  `,
})
export class Authorize {
  readonly campaign = input.required<Campaign>();
  readonly close = output<void>();
  readonly changed = output<void>();
  protected readonly step = signal(1);
  protected readonly days = signal(7);
  protected readonly perDay = signal(3);
  protected readonly minScore = signal(85);
  protected readonly accepted = signal(false);
  protected readonly text = signal('');
  protected readonly adapters = signal<Adapter[]>([]);
  protected readonly picked = signal<Set<string>>(new Set());
  protected readonly forms = computed(() => this.adapters().filter((a) => a.capabilities.includes('SubmitApplication')));
  protected readonly maxCap = computed(() => Math.min(15, this.campaign().maxSubmissionsPerDay));
  protected readonly until = computed(() => new Date(Date.now() + this.days() * 86_400_000).toLocaleDateString('pt-BR', { day: 'numeric', month: 'long' }));
  protected readonly granting = command((body: object) => send('POST', `/campaigns/${this.campaign().id}/authorizations`, body), 'Envio automático autorizado.');

  constructor() {
    void get<{ text: string }>('/authorizations/text').then((t) => this.text.set(t.text)).catch(() => undefined);
    void get<Adapter[]>('/adapters').then((a) => { this.adapters.set(a); this.picked.set(new Set(a.filter((x) => x.capabilities.includes('SubmitApplication')).map((x) => x.id))); }).catch(() => undefined);
    queueMicrotask(() => this.minScore.set(Math.max(this.campaign().minFitScore, 85)));
  }

  protected toggle(id: string) { this.picked.update((p) => { const n = new Set(p); if (n.has(id)) n.delete(id); else n.add(id); return n; }); }
  protected num(e: Event) { return +(e.target as HTMLInputElement).value; }
  protected async grant() {
    const all = this.forms().length === this.picked().size;
    if ((await this.granting.run({ days: this.days(), maxPerDay: this.perDay(), minScore: this.minScore(), adapters: all ? [] : [...this.picked()], accepted: this.accepted() })) === null) return;
    this.changed.emit();
    this.step.set(4);
  }
}
