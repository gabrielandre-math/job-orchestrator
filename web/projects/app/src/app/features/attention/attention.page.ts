import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Empty } from '@jo/ui/empty';
import { TaskRow } from '../../core/api';
import { query } from '../../core/query';
import { ago } from '../../core/format';

/** Atenção: tudo que só você resolve, mais antigo primeiro. */
@Component({
  selector: 'app-attention',
  imports: [RouterLink, Empty],
  template: `
    <section class="page">
      <header class="page-head"><div><h1>Atenção</h1><p>Aprovações, CAPTCHA, login, perguntas novas e envios incertos. Nada disso o sistema decide sozinho.</p></div></header>
      @if (tasks.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
      <section class="panel">
        @if (tasks.value(); as list) {
          @if (list.length === 0) {
            <jo-empty title="Nada esperando por você." hint="Quando um formulário pedir CAPTCHA, login ou uma resposta que você ainda não aprovou, ele para aqui." />
          } @else {
            <table class="grid">
              <thead><tr><th>Pendência</th><th>Detalhe</th><th>Tipo</th><th class="right">Desde</th></tr></thead>
              <tbody>
                @for (t of list; track t.id) {
                  <tr class="row" [routerLink]="['/candidaturas', t.applicationId]">
                    <td><strong>{{ t.title }}</strong></td><td class="muted">{{ t.detail }}</td><td class="faint">{{ kind(t.kind) }}</td><td class="right faint">{{ ago(t.createdAt) }}</td>
                  </tr>
                }
              </tbody>
            </table>
          }
        }
      </section>
    </section>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttentionPage {
  protected readonly tasks = query<TaskRow[]>(() => '/tasks', 4000);
  protected readonly ago = ago;
  protected kind(k: string) { return ({ Approval: 'Aprovação', Captcha: 'CAPTCHA', Login: 'Login', Question: 'Pergunta nova', Reconcile: 'Envio incerto', Review: 'Revisão' } as Record<string, string>)[k] ?? k; }
}
