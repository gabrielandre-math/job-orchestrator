import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { Score } from '@jo/ui/score';
import { JobRow, Profile, ProfileFact, get, send, upload } from '../../core/api';
import { command } from '../../core/command';
import { ago } from '../../core/format';
import { AGENT_ICON, Live } from '../../core/live';
import { query } from '../../core/query';
import { Wizard } from '../../shared/wizard';

interface FoundExport { name: string; bytes: number; modifiedAt: string }
interface LinkedInStatus { connected: boolean; fileName: string | null; importedAt: string | null; documentStatus: string | null; facts: number; found: FoundExport[]; folder: string }
interface Compose { ok: boolean; versionId: string | null; error: string | null }

const PAGE = 'https://www.linkedin.com/mypreferences/d/download-my-data';
/** Quando você pediu a cópia (só neste navegador), para a tela dizer há quanto tempo e quando vale pedir de novo. */
const ASKED = 'jo.linkedin.askedAt';
const GROUPS: { kind: string; label: string }[] = [
  { kind: 'experience', label: 'Experiências' }, { kind: 'education', label: 'Formação' }, { kind: 'certification', label: 'Certificados' },
  { kind: 'language', label: 'Idiomas' }, { kind: 'project', label: 'Projetos' },
];

/**
 * Conectar o LinkedIn pelo caminho oficial, sem robô e sem login dentro do app: pedir a cópia dos dados (a página certa e o que
 * marcar), esperar o e-mail enquanto o app olha a pasta de downloads, ler o ZIP sozinho quando ele chega, mostrar o que entrou no
 * perfil (somado ao currículo, sem repetir) e levar direto aos currículos das vagas da fila.
 */
@Component({
  selector: 'app-linkedin-import',
  imports: [Company, Icon, Iso, RouterLink, Score, Wizard],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <jo-wizard [steps]="['Pedir a cópia', 'Baixar', 'Conferir', 'Currículos']" [step]="step()" label="Conectar o LinkedIn" (close)="close.emit()">
      @switch (step()) {
        @case (1) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="identity" [size]="96" />
              <div>
                <h2>{{ status.value()?.connected ? 'Atualize o LinkedIn conectado' : 'Conecte o seu LinkedIn' }}</h2>
                <p class="muted">Conectar é pedir a cópia oficial dos seus dados: o LinkedIn gera um ZIP com perfil, cargos, formação e competências, e o app lê
                  sozinho. Ninguém entra na sua conta, não há robô, e mensagens e conexões nunca são lidas.</p>
              </div>
            </div>
            @if (latest(); as f) {
              <div class="found">
                <jo-icon name="download-simple" [size]="18" />
                <span><strong>Já tem um export na pasta de downloads:</strong> {{ f.name }} ({{ size(f.bytes) }}, {{ ago(f.modifiedAt) }}).</span>
                <button type="button" class="btn primary" (click)="importFound(f)">Ler este agora</button>
              </div>
            }
            <a class="btn primary big" [href]="page" target="_blank" rel="noopener" (click)="asked()"><jo-icon name="arrow-square-out" [size]="15" />Abrir o LinkedIn na página certa</a>
            <ol class="how">
              <li>Na página que abrir, escolha <strong>baixar dados específicos</strong> (a opção "Quer algo em particular?").</li>
              <li>Marque <strong>Perfil, Cargos, Formação, Competências, Certificações, Idiomas e Projetos</strong>. O arquivo completo também serve, só demora mais.</li>
              <li>Clique em <strong>Solicitar arquivo</strong> e confirme a sua senha. O resto é com o app.</li>
            </ol>
          </div>
        }
        @case (2) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="inbox" [size]="96" />
              <div>
                <h2>{{ latest() ? 'Achei o arquivo' : 'Agora o LinkedIn prepara o arquivo' }}</h2>
                <p class="muted">Quando o arquivo fica pronto, o LinkedIn manda um e-mail. Com dados específicos ele chega em minutos; o arquivo completo leva até 24 horas.
                  Clique em <strong>Baixar</strong> no e-mail ou na página do LinkedIn: o ZIP cai na pasta de downloads e o app acha sozinho.</p>
              </div>
            </div>
            @if (latest(); as f) {
              <div class="found ready">
                <jo-icon name="check" [size]="18" />
                <span><strong>{{ f.name }}</strong> ({{ size(f.bytes) }}, {{ ago(f.modifiedAt) }})</span>
                <button type="button" class="btn primary" (click)="importFound(f)">Ler agora</button>
              </div>
            } @else {
              <div class="watch" role="status">
                <span class="pulse" aria-hidden="true"></span>
                <span>Olhando a pasta <span class="mono">{{ status.value()?.folder ?? 'Downloads' }}</span> a cada poucos segundos.@if (askedLabel(); as a) { Pedido {{ a }}. } Pode fechar esta janela: quando você voltar, o app acha o arquivo.</span>
              </div>
              <div class="late">
                <strong>Não chegou?</strong>
                <ol class="how">
                  <li>Abra de novo a página do LinkedIn. Quando o arquivo fica pronto, ela mostra o botão para baixar, sem depender do e-mail.</li>
                  <li>Procure o e-mail do LinkedIn no endereço principal da sua conta de lá, inclusive em Spam, Promoções e Social.</li>
                  <li>Dados específicos chegam em minutos. Se passou de uma hora, peça de novo: o pedido só vale depois que você confirma a senha.</li>
                  <li>O arquivo completo leva até 24 horas. Pronto, ele fica disponível para baixar por 72 horas.</li>
                </ol>
                <a class="btn" [href]="page" target="_blank" rel="noopener" (click)="mark(false)"><jo-icon name="arrow-square-out" [size]="14" />Abrir a página do LinkedIn</a>
              </div>
            }
            <label class="other">
              <input type="file" accept=".zip" (change)="choose($event)" />
              <jo-icon name="file-text" [size]="14" />O ZIP está em outra pasta? Escolher o arquivo
            </label>
            @if (failed(); as p) { <p class="problem">{{ p }}</p> }
          </div>
        }
        @case (3) {
          <div class="wz-step">
            @if (busy()) {
              <div class="intro">
                <jo-iso name="reader" [size]="96" />
                <div><h2>Lendo o export</h2><p class="muted">Cada linha dos CSVs vira um fato com a origem (arquivo e linha). O que já está no seu currículo não entra de novo: soma tecnologias e conquistas ao cargo que já existe.</p></div>
              </div>
              <div class="watch"><span class="pulse" aria-hidden="true"></span><span>{{ stage() }}</span></div>
              @if (feed().length > 0) {
                <ol class="feed">@for (l of feed(); track l.message.id) { <li><jo-iso [name]="icons[l.message.agent] ?? 'router'" [size]="26" /><span><strong>{{ l.message.agent }}</strong> {{ l.message.title }}</span></li> }</ol>
              }
            } @else {
              <div class="intro">
                <jo-iso name="checker" [size]="96" />
                <div>
                  <h2>LinkedIn conectado: {{ mine().length }} {{ mine().length === 1 ? 'fato' : 'fatos' }} no perfil</h2>
                  <p class="muted">{{ added() > 0 ? added() + ' entraram agora' : 'Nada novo desta vez' }}{{ shared() > 0 ? ', e ' + shared() + ' já estavam no currículo e não foram repetidos' : '' }}. Cada fato mostra a origem no Perfil (arquivo e linha do export).</p>
                </div>
              </div>
              <div class="groups">
                @for (g of groups(); track g.label) {
                  <section>
                    <h3>{{ g.label }} <span class="faint num">{{ g.items.length }}</span></h3>
                    <ul>@for (f of g.items.slice(0, 5); track f.id) { <li>{{ f.statement }}@if (f.start) { <span class="faint small"> {{ f.start }} – {{ f.end ?? 'hoje' }}</span> }</li> }</ul>
                  </section>
                }
              </div>
              @if (skills().length > 0) {
                <div class="skills"><h3>Competências <span class="faint num">{{ skills().length }}</span></h3>
                  <p>@for (s of skills().slice(0, 28); track s) { <span class="chip">{{ s }}</span> }@if (skills().length > 28) { <span class="faint small"> e mais {{ skills().length - 28 }}</span> }</p>
                </div>
              }
            }
          </div>
        }
        @case (4) {
          <div class="wz-step">
            <div class="intro">
              <jo-iso name="writer" [size]="96" />
              <div><h2>Currículos para as vagas da sua fila</h2><p class="muted">Cada currículo é escrito para a vaga, só com fatos do seu perfil, e conferido antes de virar PDF. Nada é enviado daqui.</p></div>
            </div>
            @if (queue().length === 0) {
              <p class="notice">A fila ainda não tem vaga. Quando a busca trouxer vagas da sua área, o currículo de cada uma sai na tela da vaga.
                <a routerLink="/vagas" (click)="close.emit()">Ver as vagas</a></p>
            } @else {
              <ul class="queue">
                @for (j of queue(); track j.id) {
                  <li>
                    <jo-company [name]="j.company" [size]="34" />
                    <span class="what"><strong>{{ j.title }}</strong><span class="faint small">{{ j.company }}, {{ j.location }}</span></span>
                    <jo-score [value]="j.score ?? 0" />
                    @if (made()[j.id]; as id) {
                      <a class="btn" [href]="'/api/materials/resumes/' + id + '/pdf'" target="_blank" rel="noopener"><jo-icon name="file-text" [size]="14" />Abrir o PDF</a>
                    } @else {
                      <button type="button" class="btn primary" (click)="write(j.id)" [disabled]="writing() !== null">
                        @if (writing() === j.id) { <span class="spinner" aria-hidden="true"></span>Escrevendo… } @else { <jo-icon name="pen-nib" [size]="14" />Gerar currículo }
                      </button>
                    }
                  </li>
                }
              </ul>
              @if (writeFailed(); as p) { <p class="problem">{{ p }}</p> }
            }
          </div>
        }
      }
      @if (!busy()) {
        <footer class="wz-foot" wz-foot>
          @if (step() === 2) { <button type="button" class="btn ghost" (click)="step.set(1)"><jo-icon name="arrow-left" [size]="14" />Voltar</button> } @else { <span></span> }
          <span class="end">
            @switch (step()) {
              @case (1) { <button type="button" class="btn" (click)="mark(false); step.set(2)">Já pedi<jo-icon name="arrow-right" [size]="14" /></button> }
              @case (3) { <button type="button" class="btn primary" (click)="step.set(4)">Gerar currículos<jo-icon name="arrow-right" [size]="14" /></button> }
              @case (4) { <button type="button" class="btn primary" (click)="close.emit()">Concluir</button> }
            }
          </span>
        </footer>
      }
    </jo-wizard>`,
  styles: `
    :host { display: contents; }
    .big { justify-self: start; padding: 10px 16px; font-size: 14px; }
    .how { margin: 0; padding-left: 20px; display: grid; gap: 8px; line-height: 1.55; color: var(--text-2); }
    .found { display: grid; grid-template-columns: auto minmax(0, 1fr) auto; gap: 12px; align-items: center; padding: 12px 14px; border-radius: 6px;
      border: 1px solid var(--line-strong); background: var(--surface-2); animation: wz-step 0.36s cubic-bezier(0.2, 0.8, 0.2, 1) both; }
    .found.ready { border-color: color-mix(in oklch, var(--ok) 55%, var(--line)); }
    .found jo-icon { color: var(--accent); }
    .found.ready jo-icon { color: var(--ok); }
    .watch { display: flex; gap: 10px; align-items: center; padding: 12px 14px; border-radius: 6px; background: var(--surface-2); color: var(--text-2); font-size: 13px; line-height: 1.5; }
    .pulse { flex: none; width: 10px; height: 10px; border-radius: 50%; background: var(--accent); box-shadow: 0 0 0 0 var(--accent-weak); animation: li-pulse 1.6s ease-out infinite; }
    @keyframes li-pulse { 0% { box-shadow: 0 0 0 0 color-mix(in oklch, var(--accent) 45%, transparent); } 100% { box-shadow: 0 0 0 12px transparent; } }
    .late { display: grid; gap: 8px; justify-items: start; padding: 12px 14px; border-radius: 6px; border: 1px solid var(--line); font-size: 13px; }
    .late .how { font-size: 12.5px; }
    .other { position: relative; justify-self: start; display: inline-flex; gap: 6px; align-items: center; font-size: 12.5px; color: var(--text-2); cursor: pointer; }
    .other:hover { color: var(--text); }
    .other input { position: absolute; inset: 0; opacity: 0; cursor: pointer; }
    .feed { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
    .feed li { display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 8px; align-items: center; font-size: 12.5px; }
    .groups { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(240px, 100%), 1fr)); gap: 12px; }
    .groups section, .skills { padding: 10px 12px; border: 1px solid var(--line); border-radius: 6px; background: var(--surface-2); }
    h3 { margin: 0 0 6px; font-size: 12.5px; font-weight: 600; }
    .groups ul { margin: 0; padding-left: 16px; display: grid; gap: 4px; font-size: 12.5px; line-height: 1.45; }
    .skills p { margin: 0; display: flex; flex-wrap: wrap; gap: 5px; }
    .chip { padding: 2px 7px; border-radius: 3px; background: var(--surface-3); font-size: 11.5px; }
    .queue { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; }
    .queue li { display: grid; grid-template-columns: auto minmax(0, 1fr) auto auto; gap: 12px; align-items: center; padding: 10px 12px; border: 1px solid var(--line); border-radius: 6px; }
    .what { display: grid; gap: 2px; min-width: 0; }
    .what strong { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    @media (max-width: 640px) { .found { grid-template-columns: auto minmax(0, 1fr); } .found .btn { grid-column: 1 / -1; } .queue li { grid-template-columns: auto minmax(0, 1fr); } }
  `,
})
export class LinkedInImport {
  private readonly live = inject(Live);
  readonly close = output<void>();
  readonly changed = output<void>();
  protected readonly page = PAGE;
  protected readonly ago = ago;
  protected readonly icons = AGENT_ICON;
  protected readonly step = signal(1);
  protected readonly busy = signal(false);
  protected readonly stage = signal('Abrindo o ZIP');
  protected readonly added = signal(0);
  protected readonly failed = signal<string | null>(null);
  protected readonly writing = signal<string | null>(null);
  protected readonly made = signal<Record<string, string>>({});
  protected readonly writeFailed = signal<string | null>(null);
  private readonly docId = signal<string | null>(null);
  private readonly profile = signal<Profile | null>(null);
  /** O app olha a pasta de downloads enquanto o assistente está aberto (só o nome dos exports do LinkedIn). */
  protected readonly status = query<LinkedInStatus>(() => '/profile/linkedin', 4000);
  protected readonly jobs = query<JobRow[]>(() => (this.step() === 4 ? '/jobs?status=Shortlisted' : null), 0);
  protected readonly feed = computed(() => this.live.feed().filter((l) => l.kind === 'profile').slice(-5).reverse());
  protected readonly latest = computed(() => this.status.value()?.found[0] ?? null);
  protected readonly queue = computed(() => (this.jobs.value() ?? []).filter((j) => j.status === 'Shortlisted').sort((a, b) => (b.score ?? 0) - (a.score ?? 0)).slice(0, 3));
  /** Os fatos que vieram do LinkedIn (do export lido agora), por grupo, e as competências. */
  protected readonly mine = computed<ProfileFact[]>(() => (this.profile()?.facts ?? [])
    .filter((f) => (f.documentId === this.docId() || (f.alsoIn ?? []).includes(this.docId() ?? '')) && f.status !== 'Rejected'));
  /** Quantos fatos do LinkedIn já estavam no currículo (somados, não repetidos). */
  protected readonly shared = computed(() => this.mine().filter((f) => f.documentId !== this.docId()).length);
  protected readonly groups = computed(() => GROUPS.map((g) => ({ label: g.label, items: this.mine().filter((f) => f.kind === g.kind) })).filter((g) => g.items.length > 0));
  protected readonly skills = computed(() => this.mine().filter((f) => f.kind === 'skill').map((f) => f.statement));

  private readonly openedAt = Date.now();
  protected readonly askedAt = signal<string | null>(stored());
  private readonly now = signal(Date.now());
  /** "há 12 min", recalculado a cada 30 s enquanto o assistente está aberto. */
  protected readonly askedLabel = computed(() => { this.now(); const a = this.askedAt(); return a ? ago(a) : null; });

  constructor() {
    const tick = setInterval(() => this.now.set(Date.now()), 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(tick));
    // O ZIP baixado com o assistente aberto (o clique em Baixar no e-mail do LinkedIn) é lido na hora; um export antigo da pasta espera o seu clique.
    effect(() => {
      const f = this.latest();
      if (f && this.step() <= 2 && !this.busy() && !this.failed() && Date.parse(f.modifiedAt) > this.openedAt) this.importFound(f);
    });
  }

  protected asked() { this.mark(true); setTimeout(() => this.step.set(2), 600); }
  /** Guarda a hora do pedido. Reabrir a página para baixar não conta como pedido novo. */
  protected mark(fresh: boolean) {
    if (!fresh && this.askedAt()) return;
    const at = new Date().toISOString();
    this.askedAt.set(at);
    try { localStorage.setItem(ASKED, at); } catch { /* sem armazenamento: a tela só não mostra há quanto tempo */ }
  }
  protected size(bytes: number) { return bytes >= 1 << 20 ? `${(bytes / (1 << 20)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`; }

  protected importFound(f: FoundExport) { void this.read(() => send<{ documentId: string }>('POST', '/profile/linkedin/import', { name: f.name })); }

  protected choose(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0];
    if (file) void this.read(() => upload<{ documentId: string }>('/profile/documents', file));
  }

  private readonly reading = command((call: () => Promise<{ documentId: string }>) => call());

  /** Manda ler, acompanha o Leitor e mostra o que entrou no perfil. */
  private async read(call: () => Promise<{ documentId: string }>) {
    this.failed.set(null);
    const before = (await get<Profile>('/profile')).facts.length;
    this.step.set(3);
    this.busy.set(true);
    this.stage.set('Abrindo o ZIP e separando os CSVs do perfil');
    const r = await this.reading.run(call);
    if (!r) { this.busy.set(false); this.step.set(2); this.failed.set(this.reading.problem()?.detail ?? 'Não foi possível ler o arquivo.'); return; }
    this.docId.set(r.documentId);
    this.stage.set('Montando os fatos e somando ao que já está no perfil');
    for (let i = 0; i < 120; i++) {
      await new Promise((res) => setTimeout(res, 1200));
      const p = await get<Profile>('/profile');
      const d = p.documents.find((x) => x.id === r.documentId);
      if (d?.status === 'Failed') { this.busy.set(false); this.step.set(2); this.failed.set(d.error ?? 'O LinkedIn mandou um arquivo que o app não conseguiu ler.'); return; }
      if (d?.status === 'Ready') { this.profile.set(p); this.added.set(Math.max(0, p.facts.length - before)); break; }
    }
    this.busy.set(false);
    this.changed.emit();
    await this.status.reload();
  }

  protected async write(jobId: string) {
    this.writing.set(jobId);
    this.writeFailed.set(null);
    try {
      const r = await send<Compose>('POST', `/materials/preview/${jobId}`);
      if (r.versionId) this.made.update((m) => ({ ...m, [jobId]: r.versionId! }));
      else this.writeFailed.set(r.error ?? 'O currículo não ficou pronto. Tente de novo na tela da vaga.');
    } catch (e) {
      this.writeFailed.set((e as { detail?: string }).detail ?? 'O currículo não ficou pronto. Tente de novo na tela da vaga.');
    } finally {
      this.writing.set(null);
    }
  }
}

function stored(): string | null {
  try { return localStorage.getItem(ASKED); } catch { return null; }
}
