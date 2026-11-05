# Job Orchestrator

Orquestrador local de candidaturas. O PC descobre vagas em fontes permitidas, avalia com nota explicada, gera currículo só com fatos do perfil, prepara formulários e envia somente com autorização válida. Toda candidatura tem histórico append-only e prova de envio.

Plano completo em `docs/PLANO.md`; arquitetura em `docs/architecture.md`; progresso e como retomar em `docs/PROGRESS.md`.

## Rodar

```bash
scripts/dev.sh     # Postgres (compose), ATS falso, API e Worker; dados isolados em .dev/
scripts/stop.sh    # para API, Worker e ATS falso (o Postgres continua)
```

- Painel e API: http://127.0.0.1:7410 (o build do painel sai de `web/`: `cd web && npx ng build`)
- ATS falso: http://127.0.0.1:5470 (vagas e cenários de teste, sem tocar em site real)
- Logs: `.dev/logs/`

Em desenvolvimento o perfil é sintético (`config/profile.example.json`) e só vai para o ATS falso: perfil de teste nunca segue para vaga real.

## Testes

```bash
docker compose -f infra/compose.test.yaml up -d --wait
cd backend && dotnet test tests/JobOrchestrator.Tests
```

- Unidade: grafo de estados, Guardião, nota, identidade canônica, política de URL do navegador, token de envio.
- Ponta a ponta com processos reais: só preparar não envia; aprovação envia uma vez; confirmação lenta vira reconciliação; o Worker morto antes do clique, depois do clique e durante a reconciliação nunca duplica.

## Estrutura

```text
backend/   .NET 10, monólito modular no padrão do Grifo
  src/BuildingBlocks/   CQRS (MediatR), Result, pipeline, outbox, inbox, leitura com Dapper, Carter
  src/Modules/          Campaigns, Jobs, Applications, Automation (cada um com Contracts)
  src/Bootstrappers/    Api (painel + endpoints) e Worker (outbox, fila de etapas, navegador)
  src/Tools/            ATS falso
  tests/                unidade, ponta a ponta e caos
web/       Angular 22: projects/app (core + features) e projects/ui
infra/     compose (Postgres local e de teste)
docs/      plano, arquitetura, ameaças, ADRs, pesquisa
```
