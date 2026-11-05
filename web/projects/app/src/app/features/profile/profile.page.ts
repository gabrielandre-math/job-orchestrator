import { ChangeDetectionStrategy, Component, computed, effect, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge } from '@jo/ui/badge';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { Confirmations, Identity, Profile, ProfileFact, get, send, upload } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';

interface Group { kind: string; title: string; facts: (ProfileFact & { children: ProfileFact[] })[] }

const KINDS: [string, string][] = [['experience', 'Experiência'], ['education', 'Formação'], ['language', 'Idiomas'], ['certification', 'Certificações'], ['project', 'Projetos']];
const COUNTRIES: [string, string][] = [['BR', 'Brasil'], ['US', 'Estados Unidos'], ['PT', 'Portugal'], ['EU', 'União Europeia'], ['CA', 'Canadá'], ['UK', 'Reino Unido']];

/**
 * Perfil verdadeiro: você joga o CV e o sistema faz o resto. Cada fato mostra o trecho do documento; o que o modelo inferiu e o que
 * só você pode afirmar fica numa tela única, uma vez. Dados pessoais ficam aqui e nunca vão para modelo.
 */
@Component({
  selector: 'app-profile',
  imports: [RouterLink, Badge, Icon, Iso, PageHero],
  templateUrl: './profile.page.html',
  styleUrl: './profile.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProfilePage {
  protected readonly profile = query<Profile>(() => '/profile', 3000);
  protected readonly answers = query<Record<string, string>>(() => '/profile/answers?lang=pt', 0);
  protected readonly dragging = signal(false);
  protected readonly sending = command((f: File) => upload<{ documentId: string }>('/profile/documents', f), 'Currículo recebido. O Leitor começou a ler.');
  protected readonly retrying = command((id: string) => send('POST', `/profile/documents/${id}/retry`), 'Lendo o documento de novo.');
  protected readonly savingIdentity = command((id: Identity) => send('PUT', '/profile/identity', id), 'Dados pessoais salvos.');
  protected readonly confirming = command((body: { confirmations: Confirmations; decisions: Record<string, boolean> }) => send('PUT', '/profile/confirmations', body), 'Perfil confirmado. Daqui para frente o sistema usa estes dados.');
  protected readonly countries = COUNTRIES;

  protected readonly identity = signal<Identity>({ fullName: '', email: null, phone: null, location: null, linkedIn: null, gitHub: null, website: null });
  protected readonly form = signal<Confirmations | null>(null);
  protected readonly decisions = signal<Record<string, boolean>>({});
  private loadedVersion = -1;

  protected readonly groups = computed<Group[]>(() => {
    const facts = (this.profile.value()?.facts ?? []).filter((f) => f.status !== 'Rejected');
    return KINDS.map(([kind, title]) => ({
      kind, title,
      facts: facts.filter((f) => f.kind === kind).map((f) => ({ ...f, children: facts.filter((c) => c.parentId === f.id) })),
    })).filter((g) => g.facts.length > 0);
  });
  protected readonly skills = computed(() => (this.profile.value()?.facts ?? []).filter((f) => f.kind === 'skill' && f.status !== 'Rejected'));
  protected readonly headline = computed(() => (this.profile.value()?.facts ?? []).find((f) => f.kind === 'headline' && f.status !== 'Rejected') ?? null);
  protected readonly summary = computed(() => (this.profile.value()?.facts ?? []).find((f) => f.kind === 'summary' && f.status !== 'Rejected') ?? null);
  protected readonly inferred = computed(() => (this.profile.value()?.facts ?? []).filter((f) => f.status === 'Inferred'));
  protected readonly reading = computed(() => (this.profile.value()?.documents ?? []).some((d) => d.status === 'Extracting' || d.status === 'Stored'));
  protected readonly undecided = computed(() => this.inferred().filter((f) => !(f.id in this.decisions())).length);
  protected readonly heroStats = computed<HeroStat[]>(() => {
    const facts = this.profile.value()?.facts ?? [];
    const usable = facts.filter((f) => f.status === 'Verified' || f.status === 'Confirmed');
    return [
      { label: 'Fatos com evidência', value: usable.length, tone: 'ok' },
      { label: 'Para confirmar', value: facts.filter((f) => f.status === 'Inferred').length, tone: facts.some((f) => f.status === 'Inferred') ? 'warn' : undefined },
      { label: 'Experiências', value: usable.filter((f) => f.kind === 'experience').length },
      { label: 'Competências', value: usable.filter((f) => f.kind === 'skill').length },
    ];
  });
  protected readonly answerRows = computed(() => Object.entries(this.answers.value() ?? {}).filter(([k]) => !['first_name', 'last_name'].includes(k)));

  constructor() {
    effect(() => {
      // O formulário acompanha o servidor quando o perfil mude de versão (documento novo, confirmação salva).
      const p = this.profile.value();
      if (!p || p.version === this.loadedVersion) return;
      this.loadedVersion = p.version;
      this.identity.set({ ...p.identity });
      this.form.set({ ...p.confirmations, englishLevel: p.confirmations.englishLevel ?? this.englishFromFacts(p.facts) });
      this.decisions.set({});
      void this.answers.reload();
    });
  }

  private englishFromFacts(facts: ProfileFact[]) {
    return facts.find((f) => f.kind === 'language' && /ingl|english/i.test(f.statement))?.level ?? null;
  }

  protected async drop(e: DragEvent) {
    e.preventDefault();
    this.dragging.set(false);
    const file = e.dataTransfer?.files?.[0];
    if (file) await this.send(file);
  }
  protected async pick(e: Event) {
    const input = e.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (file) await this.send(file);
  }
  private async send(file: File) {
    if (await this.sending.run(file)) await this.profile.reload();
  }
  protected async retry(id: string) { if ((await this.retrying.run(id)) !== null) await this.profile.reload(); }

  protected setId(key: keyof Identity, value: string) { this.identity.update((i) => ({ ...i, [key]: value })); }
  protected async saveIdentity() { if ((await this.savingIdentity.run(this.identity())) !== null) await this.profile.reload(); }

  protected set<K extends keyof Confirmations>(key: K, value: Confirmations[K]) { this.form.update((f) => (f ? { ...f, [key]: value } : f)); }
  protected toggleIn(key: 'authorizedCountries' | 'modalities', value: string) {
    const list = this.form()?.[key] ?? [];
    this.set(key, list.includes(value) ? list.filter((v) => v !== value) : [...list, value]);
  }
  protected money(key: 'floorCltBrl' | 'floorPjBrl' | 'floorUsdMonth', raw: string) {
    const n = Number(raw.replace(/\D/g, ''));
    this.set(key, raw.trim() === '' || !Number.isFinite(n) || n === 0 ? null : n);
  }
  protected decide(id: string, accept: boolean) { this.decisions.update((d) => ({ ...d, [id]: accept })); }
  protected async confirm() {
    const f = this.form();
    if (!f) return;
    if ((await this.confirming.run({ confirmations: f, decisions: this.decisions() })) !== null) await this.profile.reload();
  }

  protected period(f: ProfileFact) {
    const fmt = (v: string | null) => (v ? (v.length >= 7 ? `${v.slice(5, 7)}/${v.slice(0, 4)}` : v.slice(0, 4)) : '');
    if (!f.start && !f.end) return '';
    return `${fmt(f.start)} até ${f.end ? fmt(f.end) : 'hoje'}`;
  }
  protected evidence(f: ProfileFact) {
    const doc = this.profile.value()?.documents.find((d) => d.id === f.documentId);
    if (f.status === 'Confirmed') return 'confirmado por você';
    if (!doc) return 'perfil de teste';
    return f.page ? `${doc.fileName}, página ${f.page}` : f.note?.includes('linha') ? f.note : doc.fileName;
  }
  protected statusLabel(s: Profile['status']) {
    return ({ Empty: 'Sem currículo', Extracting: 'Lendo o currículo', NeedsConfirmation: 'Falta confirmar', Ready: 'Pronto' } as const)[s];
  }
  protected statusTone(s: Profile['status']) { return ({ Empty: 'neutral', Extracting: 'warn', NeedsConfirmation: 'warn', Ready: 'ok' } as const)[s]; }
  protected docLabel(s: string) { return ({ Stored: 'Na fila', Extracting: 'Lendo', Ready: 'Lido', Failed: 'Falhou' } as Record<string, string>)[s] ?? s; }
  protected docTone(s: string) { return ({ Stored: 'neutral', Extracting: 'warn', Ready: 'ok', Failed: 'danger' } as Record<string, string>)[s] ?? 'neutral'; }
  protected keyLabel(k: string) {
    if (k.startsWith('years_experience:')) return `Anos com ${k.slice(17)}`;
    return ({
      full_name: 'Nome', email: 'E-mail', phone: 'Telefone', location: 'Onde mora', linkedin_url: 'LinkedIn', github_url: 'GitHub', portfolio_url: 'Site ou portfólio',
      work_authorization: 'Autorização de trabalho', needs_sponsorship: 'Precisa de visto', salary_expectation: 'Pretensão (CLT)', start_date: 'Quando pode começar',
      relocation: 'Aceita mudança', work_modality: 'Modalidade', self_id: 'Autoidentificação', english_level: 'Inglês', education: 'Formação', how_heard: 'Como soube da vaga',
    } as Record<string, string>)[k] ?? k;
  }
}
