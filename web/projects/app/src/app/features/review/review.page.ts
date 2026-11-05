import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Empty } from '@jo/ui/empty';
import { Score } from '@jo/ui/score';
import { ApplicationDetail, ApplicationRow, get, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';

/** Revisão: o pacote exato de cada envio, com aprovação explícita (um clique para pedir, outro para confirmar). */
@Component({
  selector: 'app-review',
  imports: [RouterLink, Empty, Score],
  templateUrl: './review.page.html',
  styleUrl: './review.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReviewPage {
  protected readonly list = query<ApplicationRow[]>(() => '/applications?state=AwaitingApproval', 4000);
  protected readonly details = signal<Record<string, ApplicationDetail>>({});
  protected readonly confirming = signal<string | null>(null);
  protected readonly decide = command(async (a: { id: string; hash: string | null; approve: boolean }) =>
    a.approve ? send('POST', `/applications/${a.id}/approve`, { payloadHash: a.hash }) : send('POST', `/applications/${a.id}/withdraw`));
  protected readonly rows = computed(() => {
    const rows = this.list.value() ?? [];
    for (const r of rows) if (!this.details()[r.id]) void this.load(r.id);
    return rows;
  });

  private async load(id: string) {
    const d = await get<ApplicationDetail>(`/applications/${id}`);
    this.details.update((all) => ({ ...all, [id]: d }));
  }

  protected async approve(id: string) {
    if (this.confirming() !== id) { this.confirming.set(id); setTimeout(() => this.confirming.update((c) => (c === id ? null : c)), 5000); return; }
    this.confirming.set(null);
    await this.decide.run({ id, hash: this.details()[id]?.payloadHash ?? null, approve: true });
    await this.list.reload();
  }

  protected async reject(id: string) {
    await this.decide.run({ id, hash: null, approve: false });
    await this.list.reload();
  }
}
