import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Campaign, get, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';

interface Settings { enabled: boolean; location: string; remoteOnly: boolean; maxJobs: number; maxDetails: number; intervalMinutes: number; dailySearches: number; }
interface View { settings: Settings; state: string; detail: string | null; lastSearch: string | null; nextSearch: string | null; lastJobs: number; browserOpen: boolean; }

@Component({
  selector: 'jo-linkedin-search', imports: [RouterLink], changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="panel box">
      <h2>Buscar vagas pela minha sessão do LinkedIn</h2>
      <p>Conecte sua conta na janela separada do LinkedIn. A busca lê vagas e descrições e as leva para a campanha, que aplica seus critérios de perfil e prepara os materiais conforme o modo escolhido.</p>
      <label class="check"><input type="checkbox" [checked]="enabled()" (change)="enabled.set($any($event.target).checked)"> Ativar busca com a conta que vou conectar</label>
      <div class="fields"><label class="field">Termo da busca<input [value]="term()" (input)="term.set($any($event.target).value)" maxlength="160"></label>
      <label class="field">Localidade<input [value]="location()" (input)="location.set($any($event.target).value)" maxlength="120"></label></div>
      <label class="check"><input type="checkbox" [checked]="remote()" (change)="remote.set($any($event.target).checked)"> Filtrar vagas remotas no LinkedIn</label>
      <div class="fields"><label class="field">Vagas por busca<input type="number" min="1" max="20" [value]="maxJobs()" (input)="maxJobs.set(+$any($event.target).value)"></label>
      <label class="field">Descrições completas por busca<input type="number" min="0" [max]="maxJobs()" [value]="maxDetails()" (input)="maxDetails.set(+$any($event.target).value)"></label>
      <label class="field">Intervalo por termo (minutos)<input type="number" min="15" max="1440" [value]="interval()" (input)="interval.set(+$any($event.target).value)"></label></div>
      <div class="actions"><button class="btn" (click)="save()" [disabled]="saving.running()">Salvar configuração</button>
      <button class="btn" (click)="connect()" [disabled]="opening.running()">{{ opening.running() ? 'Abrindo…' : 'Abrir LinkedIn para login' }}</button>
      <button class="btn" (click)="view.reload()">Conferir sessão</button></div>
      @if (view.value(); as v) {
        <div class="status" aria-live="polite"><strong>{{ stateLabel(v.state) }}</strong>@if (v.detail) { <p>{{ v.detail }}</p> }
          @if (v.lastSearch) { <p>Última busca: {{ v.lastJobs }} vagas lidas. <a routerLink="/vagas">Conferir vagas na campanha</a></p> }
        </div>
      }
      @if (saving.problem() || opening.problem() || attaching.problem() || searching.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
      <label class="check"><input type="checkbox" [checked]="recurring()" (change)="recurring.set($any($event.target).checked)"> Ligar também a busca recorrente da campanha, mantendo o modo de candidatura atual</label>
      <div class="actions"><button class="btn primary" (click)="add()" [disabled]="attaching.running() || !enabled() || !term().trim()">Adicionar esta busca à campanha</button>
      <button class="btn primary" (click)="run()" [disabled]="searching.running() || attaching.running() || !enabled()">Buscar agora pela campanha</button></div>
      <p class="muted small">A janela guarda uma sessão local separada do seu navegador pessoal. Login e verificações são feitos por você; a busca pausa quando o site pede nova autenticação. Os limites de leitura e o estado da sessão ficam visíveis aqui. A integração depende do layout do LinkedIn e pode precisar de atualização.</p>
      <p class="muted small">Quando a publicação traz um link externo de ATS suportado, a campanha usa esse formulário. Candidaturas dentro do LinkedIn continuam pelo site com o copiloto. Contato publicado pode seguir pelo módulo de e-mail quando esse for o canal escolhido e o envio estiver configurado e autorizado.</p>
      <button class="btn" (click)="disconnect()" [disabled]="disconnecting.running()">Desconectar e apagar a sessão local</button>
    </section>
  `,
  styles: `.box{padding:20px;display:grid;gap:14px}.box h2,.box p{margin:0}.check{display:flex;gap:10px;align-items:start}.check input{margin-top:4px}.fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:14px}.actions{display:flex;flex-wrap:wrap;gap:8px}.small{font-size:12px}.status{padding:12px;border:1px solid var(--border);display:grid;gap:6px}.box>button{justify-self:start}`,
})
export class LinkedInSearch {
  protected readonly enabled = signal(false);
  protected readonly term = signal('.NET');
  protected readonly location = signal('Brasil');
  protected readonly remote = signal(true);
  protected readonly maxJobs = signal(10);
  protected readonly maxDetails = signal(5);
  protected readonly interval = signal(60);
  protected readonly recurring = signal(true);
  protected readonly view = query<View>(() => '/automation/linkedin-session', 10000);
  protected readonly saving = command((s: Settings) => send<View>('PUT', '/automation/linkedin-session', s), 'Configuração do LinkedIn salva.');
  protected readonly opening = command(() => send<View>('POST', '/automation/linkedin-session/open'));
  protected readonly disconnecting = command(() => send('DELETE', '/automation/linkedin-session'));
  protected readonly attaching = command(() => this.attach(), 'Busca adicionada à campanha.');
  protected readonly searching = command(async () => { const id = await this.attach(); return send('POST', `/campaigns/${id}/runs`); }, 'Rodada solicitada. Acompanhe em Busca ao vivo.');
  constructor() {
    void get<View>('/automation/linkedin-session').then(v => { const s=v.settings; this.enabled.set(s.enabled); this.location.set(s.location); this.remote.set(s.remoteOnly); this.maxJobs.set(s.maxJobs); this.maxDetails.set(s.maxDetails); this.interval.set(s.intervalMinutes); }).catch(() => {});
  }
  private settings(): Settings { return { enabled: this.enabled(), location: this.location(), remoteOnly: this.remote(), maxJobs: this.maxJobs(), maxDetails: this.maxDetails(), intervalMinutes: this.interval(), dailySearches: 12 }; }
  protected async save() { if (await this.saving.run(this.settings())) await this.view.reload(); }
  protected async connect() { if (await this.saving.run(this.settings()) && await this.opening.run(undefined)) await this.view.reload(); }
  protected async add() { await this.attaching.run(undefined); }
  protected async run() { await this.searching.run(undefined); await this.view.reload(); }
  protected async disconnect() { if (await this.disconnecting.run(undefined) !== null) { this.enabled.set(false); await this.view.reload(); } }
  private async attach() {
    await send('PUT', '/automation/linkedin-session', this.settings());
    const term = this.term().trim();
    if (!term || term.length > 160) throw { status: 400, detail: 'Informe um termo de busca de até 160 caracteres.' };
    const c = (await get<Campaign[]>('/campaigns'))[0];
    if (!c) throw { status: 404, detail: 'Crie uma campanha primeiro.' };
    await send('PUT', `/campaigns/${c.id}`, { ...c, sources: [...new Set([...c.sources, 'linkedin-session:' + term])], night: this.recurring() || c.enabled });
    return c.id;
  }
  protected stateLabel(state: string) { return ({ Ready: 'Sessão disponível', Disabled: 'Busca desligada', Disconnected: 'Conecte a sessão', LoginRequired: 'Aguardando seu login', VerificationRequired: 'Verificação necessária', WaitingInterval: 'Aguardando próxima consulta', Paused: 'Consultas pausadas', BrowserError: 'Navegador precisa de atenção', ReadError: 'Leitura precisa de atenção' } as Record<string,string>)[state] || state; }
}
