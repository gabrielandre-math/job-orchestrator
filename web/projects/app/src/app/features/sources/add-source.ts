import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { send } from '../../core/api';
import { command } from '../../core/command';
import { PLATFORMS, describeSource } from '../../core/sources';
import { Wizard } from '../../shared/wizard';

type Way = 'ats' | 'api' | 'site' | 'link';
interface TestResult { ok: boolean; count: number; seconds: number; sample: { title: string; company: string; location: string; applyUrl: string; applyEmail: string | null }[]; error: string | null }

/** Plataformas de ATS com o formato do endereço público, para a pessoa achar o nome da empresa. */
const ATS = [
  { key: 'gupy', hint: '', after: '.gupy.io' }, { key: 'greenhouse', hint: 'boards.greenhouse.io/', after: '' }, { key: 'lever', hint: 'jobs.lever.co/', after: '' },
  { key: 'ashby', hint: 'jobs.ashbyhq.com/', after: '' }, { key: 'smartrecruiters', hint: 'jobs.smartrecruiters.com/', after: '' },
  { key: 'recruitee', hint: '', after: '.recruitee.com' }, { key: 'breezy', hint: '', after: '.breezy.hr' }, { key: 'personio', hint: '', after: '.jobs.personio.de' },
  { key: 'workable', hint: 'apply.workable.com/', after: '' }, { key: 'teamtailor', hint: '', after: '.teamtailor.com' },
];
/** APIs públicas com o que cada uma espera depois dos dois pontos. */
const APIS = [
  { key: 'getonbrd', what: 'Palavra de busca', example: '.net, react, dados' }, { key: 'remotive', what: 'Palavra de busca', example: 'enfermagem, vendas, .net' }, { key: 'arbeitnow', what: 'Palavra de busca', example: 'marketing' },
  { key: 'himalayas', what: 'Palavra de busca', example: 'designer' }, { key: 'jobicy', what: 'Tag', example: 'marketing, dev, finance' },
  { key: 'themuse', what: 'Categoria', example: '', options: ['Software Engineering', 'Data and Analytics', 'Design and UX', 'Sales', 'Marketing', 'Customer Service', 'Healthcare', 'Education', 'Accounting and Finance', 'Human Resources and Recruitment', 'Project Management', 'Administration and Office', 'Law', 'Retail', 'Manufacturing and Warehouse'] },
  { key: 'remoteok', what: 'Tag', example: 'dev, design, marketing' },
  { key: 'wwr', what: 'Categoria', example: '', options: ['remote-programming-jobs', 'remote-back-end-programming-jobs', 'remote-front-end-programming-jobs', 'remote-full-stack-programming-jobs', 'remote-design-jobs', 'remote-sales-and-marketing-jobs', 'remote-customer-support-jobs', 'remote-management-and-finance-jobs', 'remote-devops-sysadmin-jobs', 'all-other-remote-jobs'] },
  { key: 'github', what: 'Repositório de vagas', example: '', options: ['backend-br/vagas', 'frontendbr/vagas', 'react-brasil/vagas', 'qa-brasil/vagas', 'androiddevbr/vagas', 'uxbrasil/vagas'] },
  { key: 'hn', what: 'Fio', example: '', options: ['whoishiring'] },
];
const DETECT: [RegExp, string][] = [
  [/(?:job-)?boards(?:\.eu)?\.greenhouse\.io\/(?:embed\/job_board\?for=)?([^/?#&]+)/i, 'greenhouse'], [/jobs\.lever\.co\/([^/?#]+)/i, 'lever'],
  [/jobs\.ashbyhq\.com\/([^/?#]+)/i, 'ashby'], [/(?:jobs|careers)\.smartrecruiters\.com\/([^/?#]+)/i, 'smartrecruiters'],
  [/([a-z0-9-]+)\.recruitee\.com/i, 'recruitee'], [/([a-z0-9-]+)\.breezy\.hr/i, 'breezy'], [/([a-z0-9-]+)\.jobs\.personio\.(?:de|com)/i, 'personio'],
  [/([a-z0-9-]+)\.gupy\.io/i, 'gupy'], [/github\.com\/([^/?#]+\/[^/?#]+)/i, 'github'],
  [/apply\.workable\.com\/(?!j\/)([^/?#]+)/i, 'workable'], [/([a-z0-9-]+)\.workable\.com/i, 'workable'], [/([a-z0-9-]+(?:\.[a-z]{2})?)\.teamtailor\.com/i, 'teamtailor'],
];

/** Reconhece a fonte a partir de qualquer link de vagas: quadro de ATS, repositório ou, se nada bater, o site da empresa. */
export function detect(link: string): string | null {
  const text = link.trim();
  if (text.length < 4) return null;
  for (const [re, platform] of DETECT) { const m = re.exec(text); if (m) return `${platform}:${m[1]}`; }
  try { return `web:${new URL(/^https?:\/\//.test(text) ? text : `https://${text}`).hostname.replace(/^www\./, '')}`; } catch { return null; }
}

/**
 * Adicionar fonte em quatro passos: onde estão as vagas (ATS, API pública, site ou colar um link), os detalhes com o formato
 * do endereço ao lado, o teste de verdade pela porta oficial e pronto. Salva na campanha.
 */
@Component({
  selector: 'app-add-source',
  imports: [Company, Icon, Iso, Wizard],
  templateUrl: './add-source.html',
  styleUrl: './add-source.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddSource {
  readonly existing = input<string[]>([]);
  readonly keyword = input('');
  readonly close = output<void>();
  readonly add = output<string>();

  protected readonly platforms = PLATFORMS;
  protected readonly atsList = ATS;
  protected readonly apiList = APIS;
  protected readonly step = signal(1);
  protected readonly way = signal<Way | null>(null);
  protected readonly ats = signal('greenhouse');
  protected readonly company = signal('');
  protected readonly api = signal('remotive');
  protected readonly term = signal('');
  protected readonly site = signal('');
  protected readonly link = signal('');
  protected readonly result = signal<TestResult | null>(null);
  protected readonly testing = command((source: string) => send<TestResult>('POST', '/sources/test', { source }));

  protected readonly apiInfo = computed(() => APIS.find((a) => a.key === this.api())!);
  protected readonly atsInfo = computed(() => ATS.find((a) => a.key === this.ats())!);
  protected readonly source = computed(() => {
    switch (this.way()) {
      case 'ats': { const c = this.company().trim().replace(/^https?:\/\//, '').split(/[/?#]/).filter(Boolean).at(-1) ?? ''; return c ? `${this.ats()}:${c}` : null; }
      case 'api': { const t = this.term().trim(); return t ? `${this.api()}:${t}` : null; }
      case 'site': return detect(this.site())?.replace(/^(?!web:)[a-z]+:/, 'web:') ?? null;
      case 'link': return detect(this.link());
      default: return null;
    }
  });
  protected readonly info = computed(() => (this.source() ? describeSource(this.source()!) : null));
  protected readonly duplicate = computed(() => !!this.source() && this.existing().includes(this.source()!));

  protected pick(w: Way) {
    this.way.set(w);
    if (w === 'api' && !this.term()) this.pickApi(this.api());
    this.step.set(2);
  }
  protected pickApi(key: string) {
    this.api.set(key);
    const a = APIS.find((x) => x.key === key)!;
    this.term.set(a.options ? a.options[0] : this.keyword());
  }
  protected async test() {
    const s = this.source();
    if (!s) return;
    this.result.set(null);
    this.step.set(3);
    this.result.set(await this.testing.run(s));
  }
  protected save() {
    const s = this.source();
    if (!s) return;
    this.add.emit(s);
    this.step.set(4);
  }
  protected again() {
    this.step.set(1); this.way.set(null); this.company.set(''); this.site.set(''); this.link.set(''); this.result.set(null);
  }
  protected back() {
    const s = this.step();
    this.step.set(s === 3 ? 2 : 1);
    if (s === 2) this.way.set(null);
  }
}
