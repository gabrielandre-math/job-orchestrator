import { ChangeDetectionStrategy, Component } from '@angular/core';
import { Badge } from '@jo/ui/badge';
import { Adapter } from '../../core/api';
import { query } from '../../core/query';
import { ago } from '../../core/format';

/** Adapters: o que cada um faz, onde atua e o placar real que alimenta o roteador. */
@Component({
  selector: 'app-adapters',
  imports: [Badge],
  template: `
    <section class="page">
      <header class="page-head"><div><h1>Adapters</h1><p>O roteador escolhe por capacidade testada, domínio, risco e placar. Circuito aberto tira o adapter da rota por 10 minutos.</p></div></header>
      @if (list.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
      <section class="panel">
        <table class="grid">
          <thead><tr><th>Adapter</th><th>Tipo</th><th>Capacidades</th><th>Domínios</th><th class="right">Tentativas</th><th class="right">Confirmados</th><th class="right">Falhas</th><th>Circuito</th></tr></thead>
          <tbody>
            @for (a of list.value() ?? []; track a.id) {
              <tr>
                <td><strong>{{ a.name }}</strong><span class="mono faint" style="display:block;font-size:12px">{{ a.id }} {{ a.version }}</span></td>
                <td class="muted">{{ a.kind }}</td>
                <td class="muted">{{ a.capabilities.join(', ') }}</td>
                <td class="mono faint" style="font-size:12px">{{ a.domains.join(', ') }}</td>
                <td class="right num">{{ a.attempts }}</td>
                <td class="right num">{{ a.confirmed }}</td>
                <td class="right num">{{ a.failures }}</td>
                <td>@if (a.circuitOpen) { <jo-badge label="Aberto" tone="danger" /> <span class="faint">até {{ ago(a.circuitOpenUntil) }}</span> } @else { <jo-badge label="Fechado" tone="ok" /> }</td>
              </tr>
            }
          </tbody>
        </table>
      </section>
    </section>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdaptersPage {
  protected readonly list = query<Adapter[]>(() => '/adapters', 6000);
  protected readonly ago = ago;
}
