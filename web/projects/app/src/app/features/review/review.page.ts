import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { HeroStat, PageHero } from '@jo/ui/page-hero';
import { Score } from '@jo/ui/score';
import { ApplicationDetail, ApplicationRow, Overview, get, send } from '../../core/api';
import { command } from '../../core/command';
import { ago } from '../../core/format';
import { query } from '../../core/query';

/** Revisão: o pacote exato de cada envio, com aprovação explícita (um clique para pedir, outro para confirmar em 5 s). */
@Component({
  selector: 'app-review',
  imports: [RouterLink, Company, Icon, Iso, PageHero, Score],
  templateUrl: './review.page.html',
  styleUrl: './review.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewPage {
  protected readonly list = query<ApplicationRow[]>(() => '/applications?state=AwaitingApproval', 4000);
  protected readonly overview = query<Overview>(() => '/overview', 5000);
  protected readonly details = signal<Record<string, ApplicationDetail>>({});
  protected readonly confirming = signal<string | null>(null);
  protected readonly leaving = signal<Record<string, 'sent' | 'out'>>({});
  protected readonly ago = ago;
  protected readonly decide = command(async (a: { id: string; hash: string | null; approve: boolean }) =>
    a.approve ? send('POST', `/applications/${a.id}/approve`, { payloadHash: a.hash }) : send('POST', `/applications/${a.id}/withdraw`),
  (r) => 'Decisão registrada.');
  protected readonly rows = computed(() => {
    const rows = this.list.value() ?? [];
    for (const r of rows) if (!this.details()[r.id]) void this.load(r.id);
    return rows;
  });

  protected readonly stats = computed<HeroStat[]>(() => {
    const rows = this.rows();
    const o = this.overview.value();
    const avg = rows.length ? Math.round(rows.reduce((n, r) => n + r.score, 0) / rows.length) : 0;
    return [
      { label: 'Esperando o seu ok', value: rows.length, tone: rows.length ? 'accent' : undefined },
      { label: 'Nota média', value: avg },
      { label: 'Enviadas hoje', value: o?.confirmedToday ?? 0, tone: 'ok' },
      { label: 'Limite por dia', value: o?.campaign?.maxSubmissionsPerDay ?? 0 },
    ];
  });

  private async load(id: string) {
    const d = await get<ApplicationDetail>(`/applications/${id}`);
    this.details.update((all) => ({ ...all, [id]: d }));
  }

  protected async approve(id: string) {
    if (this.confirming() !== id) { this.confirming.set(id); setTimeout(() => this.confirming.update((c) => (c === id ? null : c)), 5000); return; }
    this.confirming.set(null);
    if ((await this.decide.run({ id, hash: this.details()[id]?.payloadHash ?? null, approve: true })) !== null) await this.leave(id, 'sent');
  }

  protected async reject(id: string) {
    if ((await this.decide.run({ id, hash: null, approve: false })) !== null) await this.leave(id, 'out');
  }

  /** O cartão sai com animação (para a direita quando aprovado, esmaecido quando recusado) antes de a lista recarregar. */
  private async leave(id: string, how: 'sent' | 'out') {
    this.leaving.update((l) => ({ ...l, [id]: how }));
    await new Promise((r) => setTimeout(r, 340));
    await this.list.reload();
    this.leaving.update((l) => { const n = { ...l }; delete n[id]; return n; });
  }
}
