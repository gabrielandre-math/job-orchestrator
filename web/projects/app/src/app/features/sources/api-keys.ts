import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';
import { Wizard } from '../../shared/wizard';

interface KeyView { id: string; name: string; fields: string[]; help: string; adds: string; connected: boolean; }
interface KeyTest { ok: boolean; detail: string; }

const DOMAIN: Record<string, string> = { adzuna: 'adzuna.com.br', jooble: 'jooble.org', brave: 'brave.com', github: 'github.com' };
const LABEL: Record<string, string> = { appId: 'App ID', appKey: 'App Key', key: 'Chave', token: 'Token' };
const HOW: Record<string, string[]> = {
  adzuna: ['Crie a conta de desenvolvedor, que é grátis.', 'No painel, abra "API Access Details".', 'Copie o App ID e a App Key e cole aqui.'],
  jooble: ['Preencha o formulário de API do Jooble com o seu e-mail.', 'A chave chega por e-mail em pouco tempo.', 'Copie a chave e cole aqui.'],
  brave: ['Crie a conta no painel da API do Brave Search.', 'Escolha o plano gratuito.', 'Em "API Keys", copie a chave e cole aqui.'],
  github: ['Abra "Fine-grained tokens" nas configurações do GitHub.', 'Crie um token só com acesso a repositórios públicos, sem permissão extra.', 'Copie o token e cole aqui.'],
};

/**
 * Chaves de API das fontes: Adzuna e Jooble (o Brasil inteiro, todas as áreas), Brave Search (a web inteira pela API oficial, para o
 * Explorador achar quadros e sites de carreira) e o token do GitHub. Cada uma tem um assistente curto que leva ao site, testa e guarda.
 */
@Component({
  selector: 'jo-api-keys',
  imports: [Company, Icon, Iso, Wizard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel">
      <header><h2>Mais vagas com chave gratuita</h2><span class="faint small">cada chave fica no chaveiro do sistema, nunca em arquivo</span></header>
      <ul class="keys">
        @for (k of list(); track k.id) {
          <li>
            <jo-company [name]="k.name" [domain]="domain(k.id)" [size]="30" />
            <span class="what"><strong>{{ k.name }}</strong><span class="faint small">{{ k.adds }}</span></span>
            <span class="tag" [class.ok]="k.connected">{{ k.connected ? 'Conectada' : 'Sem chave' }}</span>
            <span class="acts">
              <button type="button" class="btn" (click)="open(k)">{{ k.connected ? 'Trocar' : 'Conectar' }}</button>
              @if (k.connected) { <button type="button" class="btn ghost" (click)="remove(k)" [disabled]="removing.running()">Remover</button> }
            </span>
          </li>
        }
      </ul>
    </section>
    @if (editing(); as k) {
      <jo-wizard [steps]="['O que é', 'A chave', 'Pronto']" [step]="step()" [label]="'Conectar ' + k.name" (close)="close()">
        @switch (step()) {
          @case (1) {
            <div class="wz-step">
              <div class="intro"><jo-iso name="router" [size]="96" /><div><h2>{{ k.name }}</h2><p class="muted">{{ k.adds }}</p></div></div>
              <ol class="how">@for (h of how(k.id); track $index) { <li>{{ h }}</li> }</ol>
              <a class="btn big" [href]="k.help" target="_blank" rel="noopener"><jo-icon name="arrow-square-out" [size]="15" />Abrir o site para criar a chave</a>
            </div>
          }
          @case (2) {
            <div class="wz-step">
              <div class="intro"><jo-iso name="guardian" [size]="96" /><div><h2>Cole a chave</h2><p class="muted">Ela vai para o chaveiro do sistema (gnome-keyring), testada na hora. Se não funcionar, sai de lá e a tela diz por quê.</p></div></div>
              <div class="fields">
                @for (f of k.fields; track f) {
                  <label class="field">{{ label(f) }}<input type="password" autocomplete="off" spellcheck="false" [value]="values()[f] ?? ''" (input)="set(f, $any($event.target).value)" /></label>
                }
              </div>
              @if (result(); as r) { @if (!r.ok) { <p class="problem">{{ r.detail }}</p> } }
            </div>
          }
          @case (3) {
            <div class="wz-step">
              <div class="intro"><jo-iso name="send" [size]="96" /><div><h2>{{ k.name }} conectada</h2><p class="muted">{{ result()?.detail }} O Explorador já usa a partir do próximo ciclo, com as palavras e os cargos do currículo.</p></div></div>
              <div class="done-actions"><button type="button" class="btn primary" (click)="close()">Concluir</button></div>
            </div>
          }
        }
        @if (step() < 3) {
          <footer class="wz-foot" wz-foot>
            @if (step() > 1) { <button type="button" class="btn ghost" (click)="step.set(1)"><jo-icon name="arrow-left" [size]="14" />Voltar</button> } @else { <span></span> }
            <span class="end">
              @if (step() === 1) { <button type="button" class="btn primary" (click)="step.set(2)">Já tenho a chave<jo-icon name="arrow-right" [size]="14" /></button> }
              @else { <button type="button" class="btn primary" (click)="save(k)" [disabled]="!filled() || saving.running()">@if (saving.running()) { <span class="spin" aria-hidden="true"></span>Testando } @else { Testar e salvar }</button> }
            </span>
          </footer>
        }
      </jo-wizard>
    }
  `,
  styles: `
    :host { display: block; }
    .small { font-size: 12px; }
    .keys { list-style: none; margin: 0; padding: 4px 0; display: grid; }
    .keys li { display: grid; grid-template-columns: auto minmax(0, 1fr) auto auto; gap: 14px; align-items: center; padding: 10px 16px; transition: background-color 180ms ease; }
    .keys li:hover { background: var(--surface-2); }
    .what { display: grid; min-width: 0; }
    .acts { display: flex; gap: 6px; }
    .tag { font-size: 11.5px; padding: 2px 8px; border-radius: 999px; background: var(--surface-2); color: var(--text-3); white-space: nowrap; }
    .tag.ok { background: var(--ok-weak); color: var(--ok); }
    .how { margin: 0; padding-left: 20px; display: grid; gap: 6px; font-size: 13px; line-height: 1.5; }
    .fields { display: grid; gap: 12px; max-width: 520px; }
    .problem { margin: 0; color: var(--danger); font-size: 12.5px; }
    .spin { width: 12px; height: 12px; border-radius: 50%; border: 2px solid var(--line-strong); border-top-color: var(--accent-text); animation: turn 0.8s linear infinite; }
    @keyframes turn { to { transform: rotate(360deg); } }
    @media (max-width: 760px) { .keys li { grid-template-columns: auto minmax(0, 1fr); } .tag, .acts { grid-column: 2; justify-self: start; } }
  `,
})
export class ApiKeys {
  protected readonly keys = query<KeyView[]>(() => '/sources/keys', 0);
  protected readonly list = computed(() => this.keys.value() ?? []);
  protected readonly editing = signal<KeyView | null>(null);
  protected readonly step = signal(1);
  protected readonly values = signal<Record<string, string>>({});
  protected readonly result = signal<KeyTest | null>(null);
  protected readonly filled = computed(() => (this.editing()?.fields ?? []).every((f) => (this.values()[f] ?? '').trim().length > 3));
  protected readonly saving = command((x: { id: string; values: Record<string, string> }) => send<KeyTest>('PUT', `/sources/keys/${x.id}`, { values: x.values }));
  protected readonly removing = command((id: string) => send('DELETE', `/sources/keys/${id}`), 'Chave removida.');

  protected domain(id: string) { return DOMAIN[id] ?? ''; }
  protected label(f: string) { return LABEL[f] ?? f; }
  protected how(id: string) { return HOW[id] ?? []; }
  protected set(f: string, v: string) { this.values.update((x) => ({ ...x, [f]: v })); this.result.set(null); }
  protected open(k: KeyView) { this.values.set({}); this.result.set(null); this.step.set(1); this.editing.set(k); }
  protected close() { this.editing.set(null); this.values.set({}); }

  protected async save(k: KeyView) {
    const r = await this.saving.run({ id: k.id, values: this.values() });
    if (!r) return;
    this.result.set(r);
    if (r.ok) { this.values.set({}); this.step.set(3); await this.keys.reload(); }
  }

  protected async remove(k: KeyView) { if ((await this.removing.run(k.id)) !== null) await this.keys.reload(); }
}
