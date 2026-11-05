# ADR 0001: motor de fluxo

Status: aceito (05/11/2026)

## Contexto

O fluxo de cada candidatura atravessa horas, depende de serviços externos e tem um passo irreversível (o envio). Precisa sobreviver a crash, reboot e queda de energia sem repetir envio. Roda num nó só, no PC do usuário, que já tem Docker e pouca folga de RAM.

## Opções

1. Temporal local: durabilidade e histórico prontos, mas é mais um servidor (com banco próprio) e um SDK, tudo para um nó só.
2. MassTransit ou Wolverine com sagas em Postgres: bom para mensageria; traz um modelo de mensagens que o problema não pede.
3. Máquina de estados própria em Postgres: grafo de transições como dado, fila com `FOR UPDATE SKIP LOCKED`, leases com TTL, checkpoints, outbox e histórico append-only.

## Decisão

Opção 3. O grafo, as invariantes e a recuperação ficam explícitos no domínio e testáveis com Postgres real. Para um nó, com o volume esperado (dezenas de candidaturas por noite), é a solução mais simples que passa nos testes de crash.

## Consequências

- Nós escrevemos e testamos leases, retries e recuperação. Os testes de caos da fase 1 cobrem esse risco.
- O contrato `IWorkflowEngine` isola o motor. Migrar para Temporal depois exige trocar a implementação, não o domínio.
- Sem Redis: Postgres é fila e fonte da verdade.
