# Plano

Data: 05/11/2026. Base: spec original do usuário (SUPER_PROMPT_JOB_ORCHESTRATOR_OMARCHY.md), `docs/research/HOST_AUDIT.md` e `docs/research/REPO_AUDIT.md`.

## 1. A ideia

De madrugada o PC procura e prepara vagas. Durante o dia, o sistema se atualiza sozinho pelo seu e-mail. Você só entra para aprovar, para responder o que só você sabe e para ir às entrevistas. Tudo roda local: Postgres, Ollama, Playwright e os CLIs `claude` e `codex` com as suas assinaturas.

Sugestão principal: começar pelo **acompanhamento** (o mini sistema), e não pelo robô de envio. Ele resolve o problema de hoje, que é controlar o que você aplicou à mão no LinkedIn, na Gupy e em outros sites. Faz isso lendo os e-mails de confirmação e de retorno, e vira a base onde o resto se encaixa.

## 2. O que é automático

| Etapa | Automático | Fica com você |
|---|---|---|
| Achar vagas nas fontes permitidas | toda noite | adicionar empresas ou colar links |
| Tirar duplicadas e conferir se a vaga segue aberta | sim | |
| Filtros eliminatórios e nota explicada | sim | ajustar pesos |
| Currículo PT/EN por vaga, com a fonte de cada linha | sim | conferir na primeira semana |
| Respostas de triagem | sim, a partir das respostas que você já aprovou | perguntas novas, uma vez |
| Preencher formulário Greenhouse, Lever, Ashby | sim | |
| Enviar | só no modo autorizado, por ATS e empresa, com limite diário | aprovar com um toque no modo padrão |
| CAPTCHA, login, MFA, declaração legal, dado sensível | nunca | sempre |
| LinkedIn, Gupy e sites com login | prepara o kit (PDF, respostas, carta) e abre a página | enviar |
| Status das candidaturas | sim, pelo e-mail, inclusive as feitas à mão | corrigir quando errar |
| Entrevista marcada | evento no painel e guia de preparação | ir |
| Follow-up | rascunho na data certa | enviar |

## 3. O mini sistema

Painel local em `127.0.0.1:7410`:

1. **Hoje:** o que chegou de madrugada, o que precisa de você, as entrevistas do dia e as contagens reais da noite.
2. **Funil:** quadro com Encontrada, Preparada, Enviada, Em processo, Entrevista e Oferta, mais Recusada, Sem resposta e Desisti. O e-mail move os cards sozinho, e arrastar corrige à mão.
3. **Candidatura:** linha do tempo com descoberta, nota, versão do CV enviada (com o PDF), respostas, e-mails, entrevistas e suas notas. Também mostra contatos e próximo passo.
4. **Revisão:** o pacote exato que vai ser enviado. Dá para aprovar, recusar ou aprovar em lote.
5. **Vagas:** tabela com filtros, nota explicada, motivo do corte e link original.
6. **Perfil verdadeiro:** fatos com evidência, respostas padrão e autorizações dadas.
7. **Demais telas:**
   - Campanhas e fontes.
   - Provedores de IA: status, uso da noite e limite observado.
   - Execuções: log e diagnóstico.
   - Configurações: modo de envio, limites, horário, revogar, exportar e apagar.

Fora do painel:

- **Widget na barra do omarchy-shell**, no mesmo formato do plugin `whoami.leds`. Mostra pendências e entrevistas do dia; o clique abre o painel.
- **Notificação de manhã** via `notify-send`, com o resumo da noite.
- **Botão "Salvar vaga" no navegador** (bookmarklet). Manda para o painel o link e o texto da página que você está vendo. Nenhum robô navega no LinkedIn.
- **Painel no celular pelo Tailscale**, opcional e só se você ligar.

Números do painel:
- taxa de resposta por fonte, por versão de CV e por senioridade;
- tempo até a primeira resposta;
- quantas candidaturas ficaram sem resposta.

## 4. Acompanhamento pelo e-mail

1. **Acesso.** Leitura do Gmail com escopo `gmail.readonly`, por OAuth de app desktop num projeto pessoal do Google Cloud, sem custo. Plano B: IMAP com senha de app. O token fica no gnome-keyring.
2. **Filtro.** A consulta já chega filtrada pelo servidor:
   - remetentes de ATS e plataformas: greenhouse, lever, ashby, gupy, linkedin, smartrecruiters, workable, indeed;
   - palavras como "candidatura" e "application".
   O resto da caixa nunca é lido.
3. **Classificação.** O Ollama local classifica cada e-mail como confirmação, visualizada, rejeição, convite, teste ou case, oferta, contato de recrutador ou outro. Também extrai empresa, cargo e datas, com saída em JSON validada.
4. **Casamento.** O e-mail é ligado à candidatura por empresa, cargo, domínio e link da vaga. Se não houver par, cria a candidatura como "importada do e-mail". É assim que entram as que você fez à mão.
5. **Convites.** Um convite `.ics` vira evento no painel e notificação, com data, hora e link. Google Calendar fica para depois.
6. **Regras de tempo.** A candidatura vira "Sem resposta" depois de 21 dias (configurável). Vaga que sumiu do ATS vira "encerrada".
7. **Privacidade.** O e-mail não sai do PC. Para Claude ou Codex vai só o mínimo necessário, como cargo e empresa para o guia de entrevista, nunca o e-mail inteiro.

A confirmar na implementação:
- modo de publicação do app OAuth (em modo Testing, o refresh token expira em 7 dias);
- remetentes exatos de cada plataforma, calibrados com a sua caixa real.

## 5. Fontes de vagas

| Tier | Fontes | Observação |
|---|---|---|
| 1, integrar primeiro | Greenhouse `boards-api.greenhouse.io/v1/boards/{empresa}/jobs`, Lever `api.lever.co/v0/postings/{empresa}`, Ashby `api.ashbyhq.com/posting-api/job-board/{empresa}`, SmartRecruiters `api.smartrecruiters.com/v1/companies/{empresa}/postings` | APIs públicas de vagas. Precisam de lista de empresas: começa com uma lista curada de quem contrata remoto LATAM/Brasil, e o sistema descobre o slug a partir de qualquer link de carreiras salvo |
| 1 | `backend-br/vagas` (44 abertas, push em 03/11) e `frontendbr/vagas` no GitHub | issues via API do GitHub; vagas BR com stack e modelo de contratação |
| 1 | HN "Who is hiring" (API do Algolia) | mensal, muito remoto internacional |
| 2, depois | Remotive, RemoteOK, Himalayas, We Work Remotely (RSS) | conferir termos antes (atribuição, limite de chamadas); filtrar "LATAM" e "worldwide" |
| Só link manual | LinkedIn, Indeed, Glassdoor, Gupy, Vagas.com, Catho, InfoJobs | os termos proíbem automação ou a candidatura exige login. Gupy: os dois endpoints públicos de busca conhecidos deram 404 em 05/11 |
| Proibido | scraping com login, contornar anti-bot, solver de CAPTCHA, proxy para fugir de limite | |

## 6. IA: quem faz o quê

| Tarefa | Quem |
|---|---|
| Parsing, dedup, filtros, nota base | código, sem IA |
| Extrair campos da vaga, classificar e-mail, embeddings de afinidade | Ollama local |
| Respostas curtas de triagem | Claude Haiku |
| Currículo e carta por vaga, guia de entrevista | Claude Sonnet |
| Checagem cruzada do currículo (cada linha tem fonte no perfil?) e plano B quando o Claude bater limite | Codex |
| API paga | desligada |

**Claude.** Chamado com `claude -p` e as flags `--tools ""`, `--json-schema`, `--no-session-persistence`, `--strict-mcp-config`, `--system-prompt` e `--model`. Roda num diretório vazio, sem `ANTHROPIC_API_KEY` e sem variáveis de Bedrock/Vertex no ambiente. Nunca usa `--bare`: pelo help da 2.1.281, esse modo aceita só API key e ignora o login da assinatura.

**Codex.** Chamado com `codex exec --sandbox read-only --skip-git-repo-check --ephemeral --output-schema <arquivo> -o <saída>` (versão 0.162.1).

**Cota.** A cota é a mesma que você usa de dia no Hub e na DR-Meddie. Por isso:
- teto por noite para cada provedor;
- pausa quando o CLI acusar limite (status `PAUSED_PROVIDER`);
- cache por vaga e versão do perfil;
- nenhum laço de autoavaliação.

**Ollama.** Roda como serviço de usuário e só sobe na janela da noite e na leitura de e-mail. Há cerca de 4,6 GB de VRAM livres. Candidatos: qwen3 4B, gemma3 4B, phi-4-mini e llama3.2 3B, mais um modelo de embedding (qwen3-embedding 0.6B ou embeddinggemma). A escolha sai de benchmark com vagas e e-mails reais.

## 7. Envio e travas

**Modos:**
- `PREPARE_ONLY`: padrão na instalação.
- `REVIEW_REQUIRED`: um toque por candidatura.
- `AUTO`: só para Greenhouse, Lever e Ashby, nas empresas e tipos que você liberar, com limite diário e sem pergunta nova no formulário.

**Envio:**
- O `SubmissionGuard` é o único que libera um envio, e a intenção é gravada antes do clique.
- `SUBMISSION_UNKNOWN` nunca reenvia. O sistema reconcilia lendo a página ou o e-mail e, se não fechar, a candidatura vai para você.
- Uma candidatura só conta como confirmada com página de confirmação, recibo ou e-mail de confirmação casado.

**Duplicidade:** fingerprint de empresa + cargo + link canônico + campanha, com constraint UNIQUE no banco.

**Navegador:** Playwright .NET com o Chromium do próprio Playwright, perfil isolado por execução e headless. Headed só quando você for terminar algo à mão.

**Adapters de terceiros**, se algum dia entrarem: processo externo com ambiente limpo (sem `AWS_*`, sem `~/.aws`), `HOME` próprio e timeout.

## 8. Arquitetura

- **Processo:** um processo .NET 10 (SDK 10.0.401 já instalado) com a API (Carter + vertical slices), os workers (`BackgroundService`) e o agendador.
- **Banco:** Postgres em Docker em `127.0.0.1:5462`, sem Redis.
- **Motor de fluxo:** máquina de estados própria em Postgres, com leases (`FOR UPDATE SKIP LOCKED`), outbox e histórico append-only. Sem Temporal, porque para um nó só é complexidade sem ganho. O contrato permite trocar depois.
- **Projetos:**
  - `Host`;
  - `Core` (domínio e slices);
  - `Adapters` (Greenhouse, Lever, Ashby, navegador próprio, Fake);
  - `Llm` (roteador e provedores);
  - `Tests`;
  - `fake-ats`;
  - `dashboard` (React + Vite, servido pela própria API).
- **Execução:** `job-orchestrator.service` (`systemd --user`) num `jobs.slice` com teto de 4 GB e peso baixo de CPU e IO, mais um timer da janela da noite. Linger desligado. Logs no journal.
- **Dados:** perfil, CVs e evidências em `~/.local/share/job-orchestrator`; configuração em `~/.config/job-orchestrator`; segredos no gnome-keyring. O `pg_dump` diário entra no restic.
- **Repo:** CLAUDE.md e AGENTS.md ficam fora do git. Commits locais, sem push.

## 9. Fases

| Fase | Entrega | Pronto quando |
|---|---|---|
| 1. Núcleo + acompanhamento | Host, Postgres, estados, painel (Hoje, Funil, Candidatura), leitura do Gmail, classificação com Ollama, widget na barra, resumo da manhã | suas candidaturas reais dos últimos 60 dias aparecem sozinhas no funil |
| 2. Descoberta + nota | Greenhouse, Lever, Ashby, SmartRecruiters, backend-br, HN; dedup; filtros; nota explicada; "Salvar vaga" | duas vagas iguais de fontes diferentes viram uma; o corte vem explicado |
| 3. Perfil + currículo | onboarding a partir do seu CV, CV PT/EN com fonte por linha, PDF pelo Chromium checado com `pdftotext`, Claude/Codex com teto | o CV sai sem nenhum fato fora do perfil |
| 4. Formulários | Fake ATS com os 10 cenários do prompt, adapters Greenhouse/Lever/Ashby em `PREPARE_ONLY`, fallback por etapa, circuit breaker | todos os cenários do Fake ATS passam |
| 5. Envio controlado | fila de revisão, `SubmissionGuard`, recibo, `SUBMISSION_UNKNOWN`, reconciliação; primeiro só no Fake ATS, depois real com a sua autorização | matar o processo no meio do envio não duplica |
| 6. Noite | systemd, janela, Ollama só na janela, teste de reinício com relógio acelerado | uma noite simulada gera o resumo certo |
| 7. Ajuste fino | pesos da nota pelas respostas reais, novas fontes, desempenho por adapter | |

## 10. Decisões do usuário

1. Conta de e-mail das candidaturas e método de leitura. Padrão: Gmail API read-only.
2. Instalar o Ollama (`ollama-cuda` 0.33.3 do repo oficial, precisa de sudo) e baixar dois modelos pequenos (alguns GB).
3. Seu CV ou export do LinkedIn, autorização de trabalho, nível de inglês e piso por vínculo (CLT, PJ, USD). Sem isso, o resto roda com dados de teste.
4. Liberar envio automático, e para quais ATS e empresas. Padrão: nada automático até o fim da fase 5.
5. Painel no celular pelo Tailscale. Padrão: desligado.

## 11. Limites honestos

- Muitos formulários têm CAPTCHA. Esses vão para a fila humana, sem exceção.
- Vaga .NET remota internacional que aceita Brasil é minoria, e os ATS públicos dependem de uma boa lista de empresas.
- A cota do Claude e do Codex é a mesma do seu dia de trabalho.
- A classificação de e-mail vai errar às vezes. O painel deixa corrigir, e a correção vira regra.
- RAM: com os containers atuais sobram cerca de 14 GiB, e o orquestrador fica sob teto de 4 GB.
