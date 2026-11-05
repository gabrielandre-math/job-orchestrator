import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { Badge } from '@jo/ui/badge';
import { Icon } from '@jo/ui/icon';
import { LlmResult, Provider, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';

/** Provedores de IA: status real de cada assinatura, ordem de gasto, cota e um teste curto pedido por você. */
@Component({
  selector: 'app-providers',
  imports: [Badge, Icon],
  templateUrl: './providers.page.html',
  styleUrl: './providers.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProvidersPage {
  protected readonly list = query<Provider[]>(() => '/providers', 15000);
  protected readonly testing = signal<string | null>(null);
  protected readonly test = command((id: string) => send<LlmResult>('POST', `/providers/${id}/test`));
  protected readonly logos: Record<string, string> = { 'claude-pro': 'claude', 'claude-max': 'claude', gemini: 'gemini', ollama: 'ollama' };

  protected async run(id: string) {
    this.testing.set(id);
    await this.test.run(id);
    this.testing.set(null);
    await this.list.reload();
  }

  protected statusLabel(s: string) {
    return ({ ready: 'Pronto', 'no-login': 'Sem login', missing: 'Não instalado', blocked: 'Bloqueado', reserve: 'Na reserva', paused: 'Pausado', stopped: 'Parado' } as Record<string, string>)[s] ?? s;
  }
  protected statusTone(s: string) {
    return ({ ready: 'ok', 'no-login': 'warn', missing: 'neutral', blocked: 'danger', reserve: 'warn', paused: 'warn', stopped: 'neutral' } as Record<string, string>)[s] ?? 'neutral';
  }
  protected reset(iso: string | null) {
    if (!iso) return '';
    const d = new Date(iso);
    return `volta ${d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })} às ${d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}`;
  }
}
