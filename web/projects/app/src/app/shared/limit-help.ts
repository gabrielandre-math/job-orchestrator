import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Help } from './help';

/**
 * As explicações dos limites do envio automático (validade, teto por dia, nota mínima e formulários), iguais na autorização
 * da Campanha e no Automatizar tudo, com o exemplo feito do valor que está no controle.
 */
@Component({
  selector: 'jo-limit-help',
  imports: [Help],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @switch (kind()) {
      @case ('days') {
        <jo-help term="Validade" iso="receipt">
          <p>Por quantos dias o sistema pode enviar candidaturas sozinho, contando a partir de agora.</p>
          <p>Quando o prazo acaba, o envio automático desliga. As vagas continuam sendo preparadas, mas cada uma espera você aprovar. Para voltar ao automático, é só autorizar de novo.</p>
          <p>Você pode desligar antes, a qualquer hora, no botão <strong>Revogar</strong> da Campanha. As vagas que estavam prontas voltam para a sua aprovação.</p>
          <p class="ex"><span>Com o valor escolhido</span>{{ value() }} {{ value() === 1 ? 'dia' : 'dias' }}: o automático vale até {{ until() }}.</p>
        </jo-help>
      }
      @case ('cap') {
        <jo-help term="Teto por dia" iso="queue">
          <p>O máximo de candidaturas que podem sair num mesmo dia com o automático ligado. A conta do dia inclui as que você aprovou à mão.</p>
          <p>Quando o teto enche, as vagas prontas que sobraram não se perdem: ficam na fila e saem a partir da meia-noite, no fuso da campanha ({{ timeZone() }}), dentro do teto do dia novo.</p>
          <p>No automático, cada empresa recebe no máximo uma candidatura a cada 24 horas.</p>
          <p>Fica dentro do limite geral da campanha ({{ max() }} por dia) e nunca passa de 15.</p>
          <p class="ex"><span>Com o valor escolhido</span>Teto {{ value() }}: se 20 vagas ficarem prontas hoje, {{ value() }} saem hoje e as outras saem nos próximos dias, {{ value() }} por dia.</p>
        </jo-help>
      }
      @case ('score') {
        <jo-help term="Nota mínima" iso="chart">
          <p>Toda vaga ganha uma nota de 0 a 100. Quanto maior, mais ela combina com os cargos, as palavras-chave e os locais da campanha.</p>
          <p>No automático, só sai sozinha a vaga com nota igual ou maior que esta. A vaga com nota menor também é preparada, mas espera você aprovar.</p>
          <p>Não pode ficar abaixo da nota de corte da campanha ({{ cut() }}), que decide quais vagas entram na fila. Com as duas iguais, toda vaga da fila pode sair sozinha.</p>
          <p class="ex"><span>Exemplo</span>Corte da campanha 80 e nota mínima 90. A vaga com nota 95 sai sozinha. A vaga com 85 é preparada e espera você. A vaga com 70 nem entra na fila.</p>
        </jo-help>
      }
      @case ('forms') {
        <jo-help term="Formulários que podem enviar" iso="form">
          <p>Cada sistema de vagas que as empresas usam tem um formulário diferente, e o sistema tem um preenchedor para cada um.</p>
          <p>Aqui você escolhe por quais desses formulários o envio pode sair sozinho. O número embaixo de cada nome mostra quantos envios por ele já foram confirmados, para você escolher os que já provaram que funcionam.</p>
          <p>A vaga de um formulário que não está marcado também é preparada, mas espera você aprovar.</p>
          <p class="ex"><span>Com todos marcados</span>A autorização vale também para formulários novos que entrarem depois.</p>
        </jo-help>
      }
    }`,
  styles: `:host { display: inline-flex; vertical-align: middle; }`,
})
export class LimitHelp {
  readonly kind = input.required<'days' | 'cap' | 'score' | 'forms'>();
  /** O valor que está no controle agora: dias, envios por dia ou nota. */
  readonly value = input(0);
  /** Nota de corte da campanha. */
  readonly cut = input(0);
  /** Limite geral de envios por dia da campanha. */
  readonly max = input(0);
  readonly timeZone = input('');
  protected readonly until = computed(() => new Date(Date.now() + this.value() * 86_400_000).toLocaleDateString('pt-BR', { day: 'numeric', month: 'long' }));
}
