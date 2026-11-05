import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { Badge } from '@jo/ui/badge';
import { Icon } from '@jo/ui/icon';
import { BenchRow, LlmCall, LlmResult, LocalModel, Provider, TaskUsage, send } from '../../core/api';
import { command } from '../../core/command';
import { query } from '../../core/query';

/** Logo oficial de cada provedor. Claude tem cor de marca; OpenAI e Ollama são monocromáticas e seguem o tema. */
const LOGOS: Record<string, { file: string; mono: boolean }> = {
  'claude-pro': { file: 'claude', mono: false },
  'claude-max': { file: 'claude', mono: false },
  codex: { file: 'openai', mono: true },
  ollama: { file: 'ollama', mono: true },
};

const TASKS: Record<string, string> = {
  'job.extract': 'Ler a vaga', 'question.map': 'Ligar pergunta ao banco', 'email.classify': 'Classificar e-mail',
  'profile.extract': 'Ler o currículo', 'resume.tailor': 'Currículo por vaga', 'resume.factcheck': 'Revisão cruzada',
  'cover.write': 'Carta', 'question.draft': 'Resposta aberta', 'interview.prep': 'Guia de entrevista',
};

const RUNGS: Record<string, string> = {
  Local: 'local rápido', LocalStrong: 'local forte', Light: 'leve', Medium: 'médio', Strong: 'forte', Top: 'topo',
};

/** Provedores de IA: status real de cada assinatura, ordem de gasto, cota, modelos locais e o uso de cada tarefa. */
@Component({
  selector: 'app-providers',
  imports: [Badge, Icon],
  templateUrl: './providers.page.html',
  styleUrl: './providers.page.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProvidersPage {
  protected readonly list = query<Provider[]>(() => '/providers', 15000);
  protected readonly models = query<LocalModel[]>(() => '/providers/local-models', 15000);
  protected readonly bench = query<BenchRow[]>(() => '/providers/bench', 0);
  protected readonly usage = query<TaskUsage[]>(() => '/providers/usage', 15000);
  protected readonly calls = query<LlmCall[]>(() => '/providers/calls?limit=12', 15000);
  protected readonly testing = signal<string | null>(null);
  protected readonly test = command((id: string) => send<LlmResult>('POST', `/providers/${id}/test`));
  protected readonly logos = LOGOS;

  protected readonly localRows = computed(() => {
    const bench = new Map((this.bench.value() ?? []).map((b) => [b.model, b]));
    return (this.models.value() ?? []).map((m) => ({ ...m, bench: bench.get(m.name) ?? null }));
  });
  protected readonly benchAt = computed(() => this.bench.value()?.[0]?.at ?? null);

  protected async run(id: string) {
    this.testing.set(id);
    await this.test.run(id);
    this.testing.set(null);
    await Promise.all([this.list.reload(), this.calls.reload(), this.usage.reload()]);
  }

  protected task(name: string) { return TASKS[name] ?? name; }
  protected rung(name: string) { return RUNGS[name] ?? name; }
  protected pct(x: number) { return `${Math.round(x * 100)}%`; }
  protected time(iso: string) { return new Date(iso).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }); }
  protected date(iso: string) { return new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' }); }

  protected statusLabel(s: string) {
    return ({ ready: 'Pronto', partial: 'Falta modelo', 'no-login': 'Sem login', missing: 'Não instalado', blocked: 'Bloqueado', reserve: 'Na reserva', paused: 'Pausado', stopped: 'Parado' } as Record<string, string>)[s] ?? s;
  }
  protected statusTone(s: string) {
    return ({ ready: 'ok', partial: 'warn', 'no-login': 'warn', missing: 'neutral', blocked: 'danger', reserve: 'warn', paused: 'warn', stopped: 'neutral' } as Record<string, string>)[s] ?? 'neutral';
  }
  protected reset(iso: string | null) {
    if (!iso) return '';
    const d = new Date(iso);
    return `volta ${d.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })} às ${d.toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}`;
  }
}
