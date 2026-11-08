import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { TaskRow } from '../../core/api';
import { BrowserCopilot } from '../../core/browser-copilot';
import { ago } from '../../core/format';
import { query } from '../../core/query';

/** Cada tipo de pendência com a estação que a representa e o que fazer. */
const KINDS: Record<string, { label: string; iso: string; action: string; hint: string }> = {
  Approval: { label: 'Aprovação', iso: 'gate', action: 'Revisar e aprovar', hint: 'O envio espera o seu ok para estes valores.' },
  Review: { label: 'Revisão', iso: 'reviewer', action: 'Revisar', hint: 'O pacote mudou e precisa de um novo olhar.' },
  Captcha: { label: 'CAPTCHA', iso: 'guardian', action: 'Resolver no navegador', hint: 'O site pediu prova de pessoa. Só você resolve.' },
  Login: { label: 'Login', iso: 'identity', action: 'Entrar no site', hint: 'O site pediu login. A sessão é sua e não é guardada.' },
  Question: { label: 'Pergunta nova', iso: 'form', action: 'Responder', hint: 'O formulário perguntou algo que você ainda não respondeu.' },
  Reconcile: { label: 'Envio incerto', iso: 'receipt', action: 'Conferir', hint: 'Não deu para confirmar se o envio chegou. Confira antes de tentar de novo.' },
  // A Review task that hands the job to you (login, CAPTCHA, a site with no open form) is not an approval: it is yours to send.
  Site: { label: 'Pelo site', iso: 'send', action: 'Ver a candidatura', hint: 'O app não envia por este site. Abra preenchida: o copiloto completa o formulário e anexa o currículo, você confere e envia.' },
};

/** Atenção: tudo que só você resolve, mais antigo primeiro, agrupado por tipo e com o próximo passo de cada um. */
@Component({
  selector: 'app-attention',
  imports: [RouterLink, Icon, Iso, PageHero],
  templateUrl: './attention.page.html',
  styleUrl: './attention.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttentionPage {
  protected readonly tasks = query<TaskRow[]>(() => '/tasks', 4000);
  protected readonly copilot = inject(BrowserCopilot);
  protected readonly opening = signal(false);
  protected readonly bySite = computed(() => this.open().filter((t) => this.kindOf(t) === 'Site'));
  protected readonly filter = signal<string | null>(null);
  protected readonly ago = ago;

  protected readonly open = computed(() => (this.tasks.value() ?? []).filter((t) => !t.resolvedAt).sort((a, b) => a.createdAt.localeCompare(b.createdAt)));
  protected readonly kinds = computed(() => {
    const by = new Map<string, number>();
    for (const t of this.open()) by.set(this.kindOf(t), (by.get(this.kindOf(t)) ?? 0) + 1);
    return [...by].map(([key, n]) => ({ key, n, ...this.meta(key) })).sort((a, b) => b.n - a.n);
  });
  protected readonly rows = computed(() => this.open().filter((t) => !this.filter() || this.kindOf(t) === this.filter()));

  protected readonly stats = computed<HeroStat[]>(() => {
    const list = this.open();
    const count = (k: string[]) => list.filter((t) => k.includes(this.kindOf(t))).length;
    const oldest = list[0];
    return [
      { label: 'Esperando você', value: list.length, tone: list.length > 0 ? 'warn' : 'ok' },
      { label: 'Aprovações', value: count(['Approval', 'Review']) },
      { label: 'Pelo site', value: count(['Site']) },
      { label: 'CAPTCHA e login', value: count(['Captcha', 'Login']) },
      { label: 'Perguntas novas', value: count(['Question']), note: oldest ? `a mais antiga, ${ago(oldest.createdAt)}` : undefined },
    ];
  });

  protected meta(kind: string) { return KINDS[kind] ?? { label: kind, iso: 'you', action: 'Abrir', hint: '' }; }
  protected kindOf(t: TaskRow) { return t.kind === 'Review' && t.title.startsWith('Candidatar-se pelo site') ? 'Site' : t.kind; }
  protected pick(kind: string | null) { this.filter.set(this.filter() === kind ? null : kind); }

  /** The next five that go by the site, each in a tab already filled, through the extension of this browser. */
  protected async openNext() {
    this.opening.set(true);
    try { await this.copilot.openMany(this.bySite().slice(0, 5).map((t) => t.applicationId)); }
    finally { this.opening.set(false); }
  }
}
