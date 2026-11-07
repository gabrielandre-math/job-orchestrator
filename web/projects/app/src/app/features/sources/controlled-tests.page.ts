import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { get, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';
import { LinkedInSearch } from './linkedin-search';

interface Settings { enabled: boolean; origins: string[]; headless: boolean; lifetimeMinutes: number; }
interface Session { id: string; url: string; state: string; provider: string; payloadHash: string | null; receipt: string | null; detail: string | null; expiresAt: string; ocrAvailable: boolean; }

@Component({
  selector: 'app-controlled-tests', imports: [RouterLink, LinkedInSearch], changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="page narrow">
      <header class="page-head"><div><h1>Busca pessoal e testes</h1><p>Busque vagas na sua conta do LinkedIn e valide CAPTCHA no seu servidor.</p></div><a class="btn" routerLink="/fontes">Voltar às fontes</a></header>
      <jo-linkedin-search />
      <section class="panel box">
        <h2>LinkedIn na sua conta</h2>
        <ol><li>Abra o LinkedIn no seu navegador com a extensão do Job Orchestrator instalada.</li><li>Abra uma vaga ou uma página de resultados. Nos detalhes, expanda a descrição.</li><li>Clique no ícone da extensão, em <strong>Ler esta página</strong>, confira a prévia e salve as vagas selecionadas.</li></ol>
        <p>A captura lê somente a aba escolhida. Abrir os detalhes e capturar novamente completa a mesma vaga. O contato de candidatura fica disponível quando foi publicado na descrição com essa finalidade.</p>
        <a class="btn" routerLink="/vagas">Conferir vagas capturadas</a>
        <p class="muted small">No navegador: chrome://extensions → modo de desenvolvedor → carregar a pasta extension do aplicativo. Recarregue a extensão após atualizar o app.</p>
      </section>
      <section class="panel box">
        <h2>Servidor de CAPTCHA</h2>
        <label class="check"><input type="checkbox" [checked]="enabled()" (change)="enabled.set($any($event.target).checked)"> Ativar testes em servidor próprio</label>
        <label class="field">Origens permitidas, uma por linha<textarea rows="3" [value]="origins()" (input)="origins.set($any($event.target).value)" placeholder="https://captcha.seudominio.com"></textarea></label>
        <label class="check"><input type="checkbox" [checked]="headless()" (change)="headless.set($any($event.target).checked)"> Executar sem janela (somente quando o desafio puder ser concluído automaticamente)</label>
        <p class="muted small">Com janela, você pode resolver o widget na própria sessão. Imagem usa Tesseract local quando instalado; soma usa o resolvedor aritmético. Turnstile, hCaptcha e reCAPTCHA v2 usam os tokens gerados pelo widget oficial.</p>
        <button class="btn" (click)="configure()" [disabled]="saving.running()">{{ saving.running() ? 'Salvando…' : 'Salvar configuração' }}</button>
        @if (saving.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
      </section>
      <section class="panel box">
        <h2>Executar e conferir</h2>
        <p>Seu formulário precisa publicar o contrato de teste v1. O servidor de referência incluído no projeto já oferece esse contrato em <span class="mono">/controlled-test</span>.</p>
        <label class="field">URL do formulário<input [value]="url()" (input)="url.set($any($event.target).value)" placeholder="https://captcha.seudominio.com/controlled-test"></label>
        <label class="check"><input type="checkbox" [checked]="owned()" (change)="owned.set($any($event.target).checked)"> Este formulário está no servidor que controlo e autorizei para o teste.</label>
        @if (!sessionId()) { <button class="btn primary" (click)="start()" [disabled]="!owned() || !enabled() || starting.running()">{{ starting.running() ? 'Abrindo sessão…' : 'Abrir e preparar teste' }}</button> }
        @if (starting.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
        @if (current.problem(); as p) { <p class="problem">{{ p.detail }}</p><button class="btn" (click)="forget()">Limpar sessão encerrada</button> }
        @if (current.value(); as s) {
          <div class="status" aria-live="polite"><strong>{{ label(s.state) }}</strong><span>Provedor: {{ s.provider }}</span><span>{{ s.url }}</span>
            @if (s.detail) { <p>{{ s.detail }}</p> }
            @if (s.state === 'Waiting') { <p>Resolva o desafio na janela aberta e volte aqui. A página e os campos permanecem na mesma sessão.</p> }
            @if (s.state === 'Ready') { <p>A resposta está disponível. O servidor ainda vai validá-la no envio.</p> }
            @if (s.receipt) { <strong>Recibo do servidor: {{ s.receipt }}</strong> }
            @if (s.provider === 'image' && !s.ocrAvailable) { <p>OCR local indisponível: instale Tesseract ou digite a resposta na janela.</p> }
          </div>
          <div class="actions"><button class="btn" (click)="current.reload()" [disabled]="current.loading()">Atualizar</button>
          @if (s.state === 'Ready') { <button class="btn primary" (click)="submit(s)" [disabled]="sending.running()">{{ sending.running() ? 'Aguardando servidor…' : 'Enviar teste ao meu servidor' }}</button> }
          <button class="btn" (click)="close()" [disabled]="closing.running() || sending.running()">Fechar e descartar sessão</button></div>
        }
        @if (sending.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
        <p class="muted small">Um recibo confirma o resultado que seu servidor declarou. Falha depois do envio vira resultado incerto e não dispara nova tentativa. A sessão expira e os cookies e tokens são descartados; o aplicativo não os salva em disco.</p>
      </section>
    </section>
  `,
  styles: `.narrow{max-width:920px}.box{padding:20px;display:grid;gap:14px}.box h2,.box p{margin:0}.box ol{padding-left:22px;margin:0;display:grid;gap:8px}.check{display:flex;gap:10px;align-items:start}.check input{margin-top:4px}.box button{justify-self:start}.small{font-size:12px}.status{display:grid;gap:8px;padding:14px;border:1px solid var(--border);overflow-wrap:anywhere}.actions{display:flex;gap:8px;flex-wrap:wrap}`,
})
export class ControlledTestsPage {
  protected readonly enabled = signal(false);
  protected readonly origins = signal('http://127.0.0.1:5471');
  protected readonly headless = signal(false);
  protected readonly url = signal('http://127.0.0.1:5471/controlled-test');
  protected readonly owned = signal(false);
  protected readonly sessionId = signal<string | null>(sessionStorage.getItem('jo-controlled-session'));
  protected readonly current = query<Session>(() => this.sessionId() ? `/automation/controlled-tests/sessions/${this.sessionId()}` : null, 2500);
  protected readonly saving = command((settings: Settings) => send<Settings>('PUT', '/automation/controlled-tests/settings', settings));
  protected readonly starting = command(() => send<Session>('POST', '/automation/controlled-tests/sessions', { url: this.url(), ownServer: this.owned() }));
  protected readonly sending = command((s: Session) => send<Session>('POST', `/automation/controlled-tests/sessions/${s.id}/submit`, { payloadHash: s.payloadHash }));
  protected readonly closing = command((id: string) => send('DELETE', `/automation/controlled-tests/sessions/${id}`));

  constructor() { void get<Settings>('/automation/controlled-tests/settings').then(s => { this.enabled.set(s.enabled); this.origins.set(s.origins.join('\n') || 'http://127.0.0.1:5471'); this.headless.set(s.headless); }).catch(() => {}); }
  protected async configure() {
    const s = await this.saving.run({ enabled: this.enabled(), origins: this.origins().split('\n').map(s => s.trim()).filter(Boolean), headless: this.headless(), lifetimeMinutes: 10 });
    if (s) this.forget();
  }
  protected async start() { const s = await this.starting.run(undefined); if (s) { sessionStorage.setItem('jo-controlled-session', s.id); this.sessionId.set(s.id); await this.current.reload(); } }
  protected async submit(s: Session) { if (await this.sending.run(s)) await this.current.reload(); }
  protected async close() { const id = this.sessionId(); if (id && await this.closing.run(id) !== null) this.forget(); }
  protected forget() { sessionStorage.removeItem('jo-controlled-session'); this.sessionId.set(null); }
  protected label(state: string) { return ({ Waiting: 'Aguardando resposta', Ready: 'Pronto para validar no servidor', Submitting: 'Enviando', Confirmed: 'Servidor confirmou o teste', Rejected: 'Servidor rejeitou o teste', Unknown: 'Resultado incerto — confira no servidor', Closed: 'Sessão encerrada', Expired: 'Sessão expirada' } as Record<string,string>)[state] || state; }
}
