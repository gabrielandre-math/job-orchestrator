# Job Orchestrator

Orquestrador local de candidaturas, para qualquer área. Descobre vagas nas fontes oficiais (Gupy, Greenhouse, Lever, Ashby e outros quadros, APIs públicas como GetOnBoard e Himalayas, repositórios de vagas do GitHub, sites de carreira pelo coletor) e acha fontes novas sozinho com o Explorador, avalia com nota explicada, gera um currículo por vaga só com fatos do seu currículo e perfeito para ATS, prepara formulários e envia só com a sua aprovação ou com uma autorização válida. Toda candidatura tem histórico append-only e prova de envio.

Como usar: `docs/guia.md`. Como operar: `docs/runbook.md`. Plano em `docs/PLANO.md`, arquitetura em `docs/architecture.md`, progresso e como retomar em `docs/PROGRESS.md`.

## Rodar

```bash
scripts/dev.sh                     # desenvolvimento: Postgres (compose), ATS falso, API e Worker; dados em .dev/
scripts/docker.sh up               # tudo em contêineres (JO_PROFILES=gpu para Ollama em contêiner)
scripts/install-units.sh --enable  # serviços de usuário do systemd, com a busca da noite, o resumo e o backup
```

- Painel e API: http://127.0.0.1:7410
- ATS falso: http://127.0.0.1:5470 (os 10 cenários de teste, sem tocar em site real)
- Na primeira vez, o primeiro contato guia: conectar a sua IA, enviar o currículo e montar a busca.
- Depois, **Automatizar tudo** (Início) liga o resto num caminho só: perfil, envio, busca sozinha, Explorador, noite e e-mail.

## Testes

```bash
docker compose -f infra/compose.test.yaml up -d --wait
cd backend
dotnet test tests/JobOrchestrator.Tests --filter "Category!=E2E"                  # unidade
dotnet test tests/JobOrchestrator.Tests --filter "FullyQualifiedName~FlowTests.X"  # ponta a ponta, um de cada vez
```

- Unidade: grafo de estados, Guardião (com varredura de combinações: só preparar nunca envia, automático sem autorização válida nunca envia sozinho), nota para qualquer área, sentinela de golpe, identidade canônica, política de URL, token de envio, redação de dados pessoais.
- Ponta a ponta com processos reais: só preparar não envia; aprovação envia uma vez; confirmação lenta vira reconciliação; o Worker morto antes do clique, depois do clique e durante a reconciliação nunca duplica; Lever, Ashby, seletor que muda, MFA e prompt injection; automático só com autorização e dentro do teto.

## Estrutura

```text
backend/   .NET 10, monólito modular no padrão do Grifo (VSA, DDD, CQRS, outbox e inbox)
  src/Modules/          Campaigns, Jobs, Applications, Automation, Intelligence, Profile, Materials,
                        Assistant, Mail e Operations, cada um com o seu contrato
  src/Bootstrappers/    Api (painel + endpoints) e Worker (outbox, fila de etapas, navegador, e-mail, limpeza)
  src/Tools/            ATS falso e a CLI jo
web/       Angular 22: projects/app (core, shared, features) e projects/ui (componentes, isométricos, gráficos)
scraper/   coletor próprio sobre o Scrapy (BSD-3), num ambiente Python à parte
infra/     compose (Postgres local e de teste, app), Dockerfile, units e timers do systemd
docs/      guia, runbook, plano, arquitetura, ameaças, ADRs, pesquisa
```
