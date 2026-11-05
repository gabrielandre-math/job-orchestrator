# Plano mestre do Job Orchestrator

Data: 05/11/2026. Escopo: a spec original completa (SUPER_PROMPT_JOB_ORCHESTRATOR_OMARCHY.md), ajustada pelo que a pesquisa provou (`docs/research/`). Detalhe técnico em `docs/architecture.md`, ameaças em `docs/threat-model.md`, decisões em `docs/adr/`.

## 1. Missão e definição de sucesso

O PC trabalha enquanto você dorme:
- procura vagas compatíveis em fontes permitidas;
- classifica cada vaga e explica a nota;
- prepara currículo e respostas específicos, só com fatos verdadeiros;
- preenche os formulários;
- envia quando, e somente quando, existe autorização válida e nada bloqueia;
- trata falhas trocando apenas a etapa que falhou;
- acompanha o que acontece depois de cada candidatura.

De manhã há um relatório confiável e uma fila curta de decisões.

Sucesso medido por:

| Métrica | Alvo |
|---|---|
| Envio duplicado ou sem autorização | zero, provado por teste de caos |
| Confirmadas | só com evidência forte (página de confirmação, recibo ou e-mail casado) |
| Tempo seu por candidatura revisada | menos de 1 minuto nos fluxos de um toque |
| Noites sem intervenção técnica | todas, salvo quebra de fonte ou cota |
| Custo incremental | zero; nenhuma API paga chamada |
| Fatos inventados em currículo ou resposta | zero, bloqueado pela checagem de proveniência |

"Formulário preenchido", "candidatura enviada" e "candidatura confirmada" são estados diferentes, e o painel nunca os mistura.

## 2. O que o sistema inteiro faz

| Módulo | Função |
|---|---|
| Host/Worker | bootstrap, DI, execução persistente, agendador, health, desligamento limpo |
| CandidateProfile | fatos verificados com evidência, preferências, respostas aprovadas, autorizações |
| JobDiscovery | conectores de fontes permitidas, sync incremental, rate limit, snapshots, fingerprint |
| EligibilityEngine | filtros eliminatórios (local, autorização de trabalho, modalidade, salário conhecido, stack essencial, vaga encerrada) |
| FitEngine | nota 0–100 explicável, com fatores, incerteza e dados desconhecidos |
| ResumeComposer | CV, carta, respostas e guia de entrevista PT/EN, com proveniência por linha e PDF validado |
| CapabilityRegistry | o que cada adapter declara e o que passou em teste, por ATS, operação e versão |
| ApplicationRouter | escolhe o adapter de cada etapa e calcula o fallback |
| WorkflowEngine | máquina de estados persistente, fila com leases, checkpoints, outbox, DLQ |
| BrowserAutomation | Playwright isolado, locators semânticos, snapshots redigidos |
| SubmissionGuard | único portão de envio: consentimento, checklist, idempotência, recibo |
| LLMRouter | decide se a etapa precisa de IA e qual provedor (regras, Ollama, Claude, Codex) |
| Tracking (CRM) | funil das candidaturas, e-mail, entrevistas, follow-ups, contatos, métricas |
| NotificationDigest | resumo da manhã, notificações locais, widget da barra |
| WebConsole | painel local com todas as telas da spec e do CRM |
| Observability | eventos de negócio, logs técnicos redigidos, traces, métricas, histórico de execuções |

## 3. Níveis de autonomia

| Nível | Modo | Faz sozinho | Precisa de você |
|---|---|---|---|
| L0 | `DISCOVERY_ONLY` | encontra, deduplica, classifica | nada |
| L1 | `PREPARE_ONLY` (padrão) | tudo até o formulário validado | enviar |
| L2 | `REVIEW_REQUIRED` | tudo, e espera aprovação do pacote exato | um toque por candidatura, ou aprovação em lote |
| L3 | `AUTHORIZED_ROUTINE_AUTO_SUBMIT` | envia sozinho o que está dentro da autorização | nada, salvo o que cai em atenção |

Como funciona a autorização do L3:
- É criada só pelo painel, com escopo (ATS, empresas, tipos de vaga, limite diário, janela, validade).
- É revogável na hora.
- Arquivo de configuração não consegue ampliar a autorização.

O que fica sempre com você, em qualquer nível:
- CAPTCHA, login e MFA;
- declaração legal e documento oficial;
- pergunta nova sensível e consentimento novo;
- qualquer resultado incerto.

Para que isso custe poucos minutos:
- **Sessão assistida da manhã.** As candidaturas paradas em CAPTCHA ou login abrem uma por vez num navegador dedicado, já preenchidas até o ponto do bloqueio. Você resolve, clica em Enviar, o sistema detecta a confirmação e passa para a próxima.
- **Copiloto para LinkedIn, Gupy e sites com login.** Os termos proíbem automação ou exigem conta nesses sites. Um painel lateral mostra o kit pronto (PDF, respostas com botão de copiar, carta). Você aplica no seu navegador, e o e-mail de confirmação move o card sozinho.

### 3.1 Zero fricção

O caminho de uso:
1. Você joga o CV (ou o export do LinkedIn) no painel.
2. O sistema extrai tudo: experiências, datas, stack, formação, idiomas e conquistas. Cada fato guarda o trecho do CV de onde saiu, e o que está escrito no documento já conta como verificado.
3. Uma tela única, uma vez só, pergunta o que o CV não diz e só você pode afirmar:
   - autorização de trabalho por país;
   - pisos por moeda e vínculo (CLT, PJ, USD);
   - modalidade e cidades;
   - disponibilidade para começar;
   - nível de inglês, se o CV não disser;
   - resposta padrão para autoidentificação voluntária;
   - autorização de envio (ATS, limite diário, validade).
4. Você conecta o Gmail com um clique (OAuth).
5. A partir daí roda sozinho:
   - toda noite busca, avalia, gera CV e respostas, preenche e envia o que estiver dentro da autorização;
   - de dia o e-mail atualiza o funil.

O que ainda chega até você, e só isso:
- CAPTCHA e login, em lote, na sessão assistida;
- pergunta sensível nova (uma vez por tipo, depois a resposta entra no banco);
- envio incerto que a reconciliação não fechou;
- entrevistas.

Para reduzir o que sobra:
- o roteador prioriza fluxos de candidatura sem CAPTCHA, e o painel mostra a taxa de bloqueio por ATS;
- o banco de respostas já nasce com as perguntas de triagem mais comuns respondidas a partir dos fatos: anos com cada tecnologia, autorização, sponsor, pretensão, aviso prévio, mudança, inglês;
- a sessão assistida pode ser feita pelo celular, via Tailscale (opcional, desligado por padrão).

CAPTCHA nunca é resolvido por máquina nem por serviço externo.

## 4. Os agentes

Cada agente é um papel dentro do motor de fluxo, com entradas mínimas, saída validada por schema e sem poder sobre políticas.

| Agente | Faz | Como | Provedor (padrão → fallback) | Efeito externo |
|---|---|---|---|---|
| Supervisor | conduz o fluxo, leases, orçamentos, janela | determinístico | não usa IA | não |
| Batedor | descoberta nas fontes | conectores HTTP | Ollama para texto livre → regras | leitura HTTP |
| Normalizador | canonicaliza, fingerprint, snapshot, dedup | determinístico | Ollama para extrair campos → regex | não |
| Sentinela | fraude, prompt injection, domínio suspeito | regras + classificador | Ollama → regras | não |
| Juiz | filtros eliminatórios | determinístico | não usa IA | não |
| Analista | nota explicável | pesos + embeddings | Ollama → Claude Haiku (só explicação) | não |
| Redator | CV, carta, respostas novas | geração com fatos selecionados | Claude Sonnet → Codex → Ollama (marcado como rascunho) | não |
| Revisor | confere proveniência linha a linha | regra + segundo modelo | Codex → Claude Haiku | não |
| Preparador | inspeciona e preenche formulário | adapters de ATS; IA só para mapear campo novo | Claude Haiku → Codex → Ollama | navegação, sem envio |
| Guardião | autoriza ou bloqueia envio | determinístico, sem IA | não usa IA | emite o token de envio |
| Remetente | clique final, sob lease e token | adapter | não usa IA | **único que envia** |
| Verificador | recibo, confirmação, reconciliação | padrões por ATS | Ollama para ler página → regras | leitura |
| Rastreador | e-mail, convites, status, follow-up | ingestão + classificação | Ollama (nunca nuvem para e-mail bruto) | leitura do Gmail |
| Coach | guia de entrevista | geração | Claude Sonnet → Codex | não |
| Mensageiro | resumo da manhã, notificações, barra | determinístico | Ollama para o texto do resumo | notificação local |

Regras dos agentes:
- Nenhum agente altera permissão, limite, modo ou consentimento.
- Texto vindo de vaga, página, e-mail ou ferramenta é dado, nunca instrução.
- Só o Remetente chama envio, e só com token válido emitido pelo Guardião para aquela intenção e aquele payload.

## 5. Roteamento inteligente

São dois roteadores independentes.

**Capability routing (qual adapter faz a etapa):**
1. Primeiro são eliminados os adapters que não servem:
   - a capability não foi testada naquele domínio e versão;
   - o circuito está aberto ou o adapter está em quarentena;
   - a política proíbe;
   - a etapa exige navegador visível e não há sessão gráfica.
2. Entre os que sobram, vence a maior nota:
   `0,35·S + 0,15·C + 0,15·(1−R) + 0,15·H − 0,10·L − 0,10·Q`
   - **S:** taxa de sucesso confirmado por domínio, capability e versão, suavizada: (confirmados+1)/(tentativas+2).
   - **C:** compatibilidade com o ATS.
   - **R:** risco.
   - **H:** saúde nas últimas 24 h.
   - **L:** latência.
   - **Q:** consumo de cota.

Os pesos ficam na configuração. Cada decisão grava todos os candidatos, os fatores e o motivo (`RoutingDecision.why`).

**LLM routing (se precisa de IA e qual):**
- Cada tarefa tem perfil de privacidade, qualidade, contexto e orçamento, com uma cadeia de provedores (catálogo em `architecture.md` §7).
- E-mail bruto e dados pessoais só vão para modelo local.
- O CV final não aceita rebaixamento silencioso: sem Claude e sem Codex, vira rascunho marcado ou `PAUSED_PROVIDER`.

**Fallback por etapa:**

| Falha | Ação |
|---|---|
| `Unsupported` ou campo não suportado | próximo adapter com `PrepareForm` testado no domínio, retomando do checkpoint semântico; nunca reexecuta envio |
| DOM mudou | nova leitura da página e mapeamento semântico (IA) com validação. Continua só em preparo. O mapeamento aprendido só vira rotina depois de uma aprovação sua |
| Provedor de IA fora ou sem cota | próximo provedor compatível; sem nenhum, `PAUSED_PROVIDER` e reagenda para depois do reset |
| Login, MFA, CAPTCHA, termos, consentimento | `NEEDS_HUMAN` (sessão assistida) ou `BLOCKED_BY_POLICY`; nunca troca de bot para contornar |
| Envio incerto | `SUBMISSION_UNKNOWN`: só reconciliação por leitura (página, portal, e-mail); nunca outro adapter |
| Rede ou leitura falhou | retry com backoff exponencial e jitter até o limite, depois `RETRY_SCHEDULED` ou `DEAD_LETTER` |

Circuit breaker por adapter + ATS + capability. Quarentena automática quando o canário falha.

## 6. Vida de uma candidatura

1. **Descoberta.** O Batedor acha a vaga numa fonte permitida e grava o snapshot (`DISCOVERED`).
2. **Normalização.** O Normalizador extrai os campos e resolve a identidade canônica. Duplicada vira `DUPLICATE` com link para a original (`NORMALIZED`).
3. **Abertura.** O sistema confere na fonte oficial se a vaga segue aberta. Encerrada vira `EXPIRED`.
4. **Elegibilidade.** O Juiz aplica os filtros eliminatórios (`ELIGIBILITY_CHECKED`). Se reprovar, vira `REJECTED` com motivo.
5. **Nota.** O Analista dá a nota com fatores (`SCORED`). Acima do corte e dentro dos limites da campanha, vira `SHORTLISTED`.
6. **Materiais.** O Redator gera CV, carta e respostas. O Revisor confere a proveniência. O PDF é validado (`MATERIALS_READY`).
7. **Formulário.** O Preparador preenche e o sistema valida, com checkpoint semântico a cada passo (`FORM_PREPARING` → `FORM_VALIDATED`).
8. **Política.** O Guardião aplica o checklist:
   - `PREPARE_ONLY`: a candidatura para preparada;
   - `REVIEW_REQUIRED`: vai para `AWAITING_APPROVAL`;
   - `AUTO` com autorização válida: vai para `READY_TO_SUBMIT`.
9. **Envio.** A intenção e o lease são gravados antes do clique (`SUBMITTING`).
10. **Confirmação.** O Verificador exige evidência forte (`SUBMITTED_CONFIRMED`). Sem ela, vira `SUBMISSION_UNKNOWN` e entra em reconciliação.
11. **Acompanhamento.** O Rastreador move o resultado pelo e-mail: vista, em processo, teste, entrevista, oferta, contratado, recusada, sem resposta.

## 7. Acompanhamento (CRM das candidaturas)

- **Funil.** Encontrada, Preparada, Enviada, Confirmada, Em processo, Entrevista, Oferta, Contratado, mais Recusada, Sem resposta e Desisti. O e-mail move os cards; arrastar corrige, e a correção vira regra.
- **Candidatura.** Linha do tempo com descoberta, nota, rota escolhida, adapters tentados, versão do CV com PDF, respostas, evidências, e-mails, entrevistas e notas suas. Também mostra contatos, próximo passo e lembretes.
- **E-mail.** Gmail `gmail.readonly` com consulta filtrada no servidor (remetentes de ATS e plataformas). O Ollama classifica e extrai, e o e-mail é casado com a candidatura. Sem par, vira "importada do e-mail"; é assim que entram as que você fez à mão. Confirmação casada vale como evidência L5.
- **Entrevistas.** Convite `.ics` vira evento com lembrete e guia do Coach.
- **Follow-up.** Rascunho na data certa. Envio só com consentimento separado, desligado por padrão.
- **Regras de tempo.** "Sem resposta" depois de 21 dias. Vaga sumida vira encerrada. Reabertura é detectada.
- **Métricas.** Taxa de resposta por fonte, ATS, versão de CV e senioridade, com tamanho da amostra e sem afirmação de causa. Também: tempo até a primeira resposta e funil por campanha.

## 8. Fontes

| Tier | Fontes | Uso |
|---|---|---|
| 1 | APIs públicas de vagas: Greenhouse, Lever, Ashby, SmartRecruiters | descoberta e conferência de abertura |
| 1 | GitHub `backend-br/vagas` e `frontendbr/vagas` | vagas BR via API do GitHub |
| 1 | HN "Who is hiring" (Algolia) | remoto internacional |
| 2 | Remotive, RemoteOK, Himalayas, We Work Remotely | depois de conferir termos (atribuição, limite) |
| Copiloto | LinkedIn, Indeed, Glassdoor, Gupy, Vagas.com, Catho, InfoJobs | você cola o link ou usa o botão "Salvar vaga"; nenhum robô navega nesses sites |
| Proibido | scraping com login, anti-bot, solver de CAPTCHA, proxy para fugir de limite | nunca |

A lista de empresas cresce sozinha:
- uma lista inicial curada;
- todo link salvo, e-mail ou vaga de agregador que aponta para um board Greenhouse, Lever ou Ashby adiciona aquela empresa à vigilância;
- você vê e remove pela tela de fontes.

## 9. Autopiloto e autocura

- **Canários antes de cada noite.** O Fake ATS roda contra cada adapter, e sondas só de leitura passam nos ATS reais. A saúde é atualizada e adapter que quebrou entra em quarentena sozinho.
- **Mapeamento de formulário que aprende.** Campo novo é mapeado por IA e validado. Depois de uma aprovação sua, vira regra versionada daquele domínio.
- **Banco de respostas.** Cresce com suas aprovações. Pergunta parecida (por embedding) reutiliza a resposta. Categoria sensível só com resposta permanente aprovada por você.
- **Cota por prioridade.** Vagas de maior nota recebem Claude primeiro; o resto espera a próxima janela. Quando o CLI acusa limite, pausa e retoma depois do reset.
- **Cache.** Material gerado é guardado por vaga + versão do perfil; nada é gerado duas vezes.
- **Reconciliação automática.** Candidatura em `SUBMISSION_UNKNOWN` é conferida sozinha pela página e pelo e-mail antes de virar tarefa sua.
- **Diário do piloto.** Narrativa do que foi feito em cada noite, com links para as evidências.
- **Resumo da manhã com decisões em lote.** Por exemplo: "aprovar 8, responder 2 perguntas novas, sessão assistida de 3 CAPTCHAs".

## 10. Interface

Painel em `127.0.0.1:7410`, React + Vite, sóbrio, claro e escuro, sem número simulado.

1. **Visão geral:** a última noite com contagens provadas, campanha, ações pendentes, cota.
2. **Campanhas:** criar, pausar, editar; fontes, cargos, modalidade, remuneração por moeda e vínculo, idiomas, senioridade, limites, janela, autonomia.
3. **Vagas:** tabela com filtros, nota explicada, motivo de corte, link original, status de abertura, duplicadas.
4. **Candidatura:** linha do tempo, versões de CV, respostas, adapters tentados, capturas redigidas, comprovantes.
5. **Funil (CRM):** quadro, contatos, entrevistas, follow-ups, métricas de resposta.
6. **Revisão:** o que vai ser enviado, destino, dados, respostas, risco; aprovar ou recusar com confirmação explícita; lote.
7. **Atenção:** CAPTCHA, login, perguntas novas, reconciliações; botão de sessão assistida.
8. **Adapters:** capacidades declaradas e testadas, saúde, histórico de sucesso, fallbacks executados, quarentena.
9. **Provedores de IA:** Ollama, Claude e Codex; autenticação sem segredo exposto, status, uso por noite, limite observado (`unknown` quando não houver dado), política de custo.
10. **Perfil verdadeiro:** fatos, evidências, autorização de trabalho, respostas padrão, consentimentos.
11. **Configurações e segurança:** modo privado (tudo local), retenção, janela, limites, backups, revogar autorizações, exportar e apagar dados.
12. **Execuções:** histórico, diagnósticos, replay seguro das fases reversíveis.

Fora do painel:
- widget na barra do omarchy-shell;
- notificação de manhã;
- bookmarklet "Salvar vaga";
- copiloto lateral;
- painel no celular pelo Tailscale, opcional e desligado por padrão.

## 11. Fases de entrega

Cada fase fecha com testes verdes, commit local e `docs/PROGRESS.md` atualizado.

### Fase 0: auditoria (concluída)
Host, cinco repos, fontes, provedores. Resultado: núcleo próprio, os cinco como referência (`docs/research/`).

### Fase 1: núcleo e fatia vertical no Fake ATS

**Entregas:**
1. **Base do projeto:**
   - solution .NET 10 com os projetos de `architecture.md` §2, central package management e nullable com warnings como erro;
   - `infra/compose.yml` com Postgres 17 em `127.0.0.1:5462`;
   - `jo doctor` e `scripts/doctor.sh`.
2. **Banco e motor de fluxo:**
   - schema e migrations (núcleo, fluxo, auditoria append-only, outbox, fila, DLQ);
   - máquina de estados com o grafo de transições como dado e testes de todas as arestas;
   - fila com leases (`SKIP LOCKED`), retries só em etapas seguras, checkpoints, recuperação de crash (`SUBMITTING` sem recibo vira `SUBMISSION_UNKNOWN`) e desligamento limpo.
3. **Roteamento e envio:**
   - CapabilityRegistry e ApplicationRouter v0 com o "why" gravado;
   - SubmissionGuard v0 com token de envio;
   - fila de revisão e verificação de recibo v0.
4. **Adapters e Fake ATS:**
   - adapters fake com os seis resultados de falha;
   - NativeBrowser v0 (Playwright .NET);
   - Fake ATS com o cenário Greenhouse e contador de envios no servidor.
5. **Painel v0:** visão geral, vagas, candidatura, revisão. Health checks e logs JSON redigidos.

**Aceite:**
- A fatia completa funciona: descoberta fake, nota, material, formulário, bloqueio em `PREPARE_ONLY`, aprovação, envio, recibo e `SUBMITTED_CONFIRMED` no painel.
- `kill -9` em três pontos não duplica (o contador do Fake ATS fica em 1).

**Precisa de você:** nada.

### Fase 2: descoberta, normalização e matching

**Entregas:**
1. **Conectores:**
   - Greenhouse, Lever, Ashby, SmartRecruiters, GitHub e HN;
   - rate limit por fonte e sync incremental;
   - vigilância de empresas com descoberta de slug.
2. **Normalização:**
   - cargo, senioridade e stack (taxonomia .NET/C#/AWS/React);
   - modalidade, país, fuso, salário com moeda e vínculo (CLT, PJ, contractor), sponsor e autorização, idiomas;
   - versão do parser.
3. **Identidade e abertura:** identidade canônica, dedup entre fontes, reabertura, conferência de abertura com validade.
4. **Avaliação:** EligibilityEngine e FitEngine com pesos, desconhecidos e confiança; embeddings opcionais.
5. **Interface:** tela de vagas completa, bookmarklet "Salvar vaga" e endpoint de entrada com token.

**Aceite:**
- Dois anúncios equivalentes de fontes diferentes viram uma entrada canônica.
- Os filtros duros são respeitados.
- Cada nota tem explicação por fator.
- Funciona com dados públicos reais (só leitura).

**Precisa de você:** nada (opcional: empresas que você quer vigiar).

### Fase 3: perfil verdadeiro, inteligência e currículos

**Entregas:**
1. **Onboarding de zero fricção (§3.1):**
   - você joga o CV (PDF, DOCX) ou o export do LinkedIn;
   - o que está escrito no documento vira `VerifiedFact` com evidência (página e trecho);
   - só inferências e o que o CV não diz vão para a tela única de confirmação;
   - o banco de respostas já sai preenchido a partir dos fatos e das confirmações.
2. **Ollama:**
   - instalação com seu ok;
   - `jo llm bench` com os candidatos (JSON válido, acerto em 50 fixtures, latência, VRAM) e escolha dos padrões.
3. **Provedores na nuvem:**
   - provedores Claude CLI e Codex CLI: diretório isolado, ambiente limpo, schema, timeout, detecção de cota, orçamento por noite, registro de cada chamada;
   - provedor de API paga existe só como stub desligado.
4. **LLMRouter:** catálogo de tarefas, níveis de privacidade, cadeias, cache.
5. **ResumeComposer:**
   - seleção de fatos, rascunho, checagem de proveniência, revisão cruzada;
   - template HTML ATS-friendly PT/EN;
   - PDF pelo Chromium, validado com `pdftotext` e `pdfinfo` (texto, páginas, overflow);
   - `ResumeVersion` com hash.
6. **Outros materiais:** carta, respostas e guia de entrevista.
7. **Proteção de PII:** dados pessoais entram por placeholder (`{{NOME}}`, `{{EMAIL}}`) só na renderização, então nenhum modelo vê.
8. **Interface:** telas Perfil verdadeiro e Provedores.

**Aceite:**
- Cada bullet do CV aponta para um fato.
- Um fato plantado na saída do modelo é rejeitado.
- Claude indisponível cai para o Codex; os dois indisponíveis geram rascunho marcado ou pausa.
- Nenhuma chamada paga.

**Precisa de você:** ok para instalar o Ollama (sudo) e baixar os modelos; seu CV ou export do LinkedIn; a tela única de confirmação (uma vez).

### Fase 4: acompanhamento (CRM)

**Entregas:**
1. **E-mail:**
   - Gmail read-only, com o token no gnome-keyring; IMAP como alternativa;
   - idempotência por Message-ID;
   - classificação e extração com Ollama, casamento com candidatura, importação das candidaturas manuais, evidência L5.
2. **Agenda:**
   - `.ics` vira entrevista com lembretes;
   - regras de follow-up com rascunhos e envio desligado.
3. **Interface:** funil, linha do tempo, contatos, notas, métricas; widget da barra (plugin do omarchy-shell); notificação da manhã.

**Aceite:**
- As candidaturas reais dos últimos 60 dias aparecem sozinhas.
- Você revisa uma amostra de 30 e-mails.
- Cada correção vira regra e é reaplicada.

**Precisa de você:** criar o app OAuth no Google Cloud (eu guio) ou gerar uma senha de app.

### Fase 5: adapters de formulário, navegador e fallback

**Entregas:**
1. **Adapters por ATS:**
   - Greenhouse, Lever e Ashby com inspeção, preparo, validação, envio, verificação e reconciliação;
   - SmartRecruiters e Workable entram se aparecerem nas vagas.
2. **Agente genérico de formulário:**
   - lê a árvore de acessibilidade redigida;
   - o modelo devolve o mapeamento de campos em JSON;
   - o preenchimento é determinístico e validado;
   - passos limitados e mapeamentos aprendidos com aprovação.
3. **Robustez:**
   - os 10 cenários do Fake ATS;
   - testes de contrato (o que o adapter declara contra o que ele passa);
   - canários diários;
   - circuit breaker e quarentena;
   - métricas por adapter e tela de Adapters.
4. **Plugins externos:** host de plugin por processo, com manifesto, JSON por stdio, timeout, ambiente limpo, `HOME` próprio e sem AWS. Fica pronto para plugins de terceiros no futuro.
5. **ATS reais:** só em `PREPARE_ONLY`, com perfil de navegador dedicado. Abre apenas páginas públicas de candidatura; nada é enviado.

**Aceite:**
- O cenário do seletor que muda se recupera.
- CAPTCHA e MFA pausam.
- Fallback depois de campo não suportado sem reexecutar envio.
- O circuito abre depois de N falhas.
- Adapter quebrado não derruba o host.

**Precisa de você:** nada.

### Fase 6: envio controlado e resiliência

**Entregas:**
1. **Autorizações:**
   - modelo de consentimento com escopo, limites, validade e revogação;
   - concessão só pela tela, nunca pelo arquivo de configuração.
2. **Guardião e envio:**
   - checklist completo da spec §9, limite diário, espaçamento por empresa e ATS, lock por fingerprint;
   - intenção, token e lease;
   - níveis de evidência;
   - reconciliação de `SUBMISSION_UNKNOWN` com tela de resolução.
3. **Proteções:** Sentinela de fraude e defesa contra prompt injection (cenário 9).
4. **Fluxos com você:** sessão assistida e copiloto.
5. **Primeiro envio real:** só com a sua autorização para uma vaga específica, com domínio e fluxo documentados.

**Aceite:**
- Teste de propriedade: `PREPARE_ONLY` nunca envia.
- `AUTO` sem autorização válida nunca envia.
- Cenários 6, 7 e 10 passam.
- O painel separa preparadas, tentadas, confirmadas, desconhecidas e bloqueadas.

**Precisa de você:** autorizar o primeiro envio real e as autorizações de rotina que quiser.

### Fase 7: operação noturna e UX completa

**Entregas:**
1. **systemd:**
   - units de usuário, `jobs.slice` e timers (arquivos no repo; instalação só com seu ok);
   - Ollama como serviço de usuário;
   - backup e retenção.
2. **Noite:**
   - pipeline com canários, descoberta, avaliação, materiais, preparo, envio espaçado, verificação e resumo;
   - encerramento limpo no fim da janela.
3. **Relatórios:** resumo em Markdown e JSON, notificações, widget final.
4. **Interface:** Execuções com replay seguro; Configurações e segurança (modo privado, retenção, exportar, apagar tudo, revogar).
5. **Documentação e testes:**
   - runbook e guia de uso;
   - `scripts/night-sim.sh` com relógio acelerado;
   - teste de reboot e de queda de energia.

**Aceite:**
- Checklist da spec §20 inteiro verde.
- Uma noite simulada gera o resumo certo.
- `systemctl --user` start, stop, status e journal testados.

**Precisa de você:** ok para instalar as units de usuário e ligar o timer.

### Fase 8: otimização contínua
- Métricas de resposta descritivas, com amostra.
- Pesos do roteador ajustados por resultado confirmado, dentro de limites.
- Diagnóstico de fontes: rendimento, duplicadas, taxa de encerramento.
- Benchmark de custo e latência por provedor.
- Guia "como criar um adapter".
- Fontes do tier 2.

## 12. Estratégia de testes

| Camada | O que prova |
|---|---|
| Unidade | grafo de estados, invariantes, política do Guardião, fórmula do roteador, fingerprint, nota |
| Integração | Postgres real via Testcontainers: leases, outbox, constraints UNIQUE, recuperação de crash |
| Contrato | cada adapter: declarado contra testado, resultados tipados, timeouts |
| E2E Fake ATS | os 10 cenários da spec, com o estado final esperado de cada um |
| Caos | `kill -9` antes do envio, depois do clique e antes da confirmação, e durante a reconciliação; reboot; disco cheio |
| Segurança | segredo-canário plantado nunca aparece em log, artefato ou prompt; fixtures de prompt injection; SSRF bloqueado |
| Recursos | noite simulada dentro do teto de RAM e VRAM |
| Aceite | checklist da spec §20, item a item, com o comando que prova cada um |

Cenários do Fake ATS:
1. Greenhouse simples.
2. Ashby com campos extras.
3. Lever com upload e pergunta condicional.
4. Seletor que muda entre tentativas.
5. CAPTCHA e MFA simulados.
6. Timeout depois de registrar a candidatura.
7. Duplicidade bloqueada.
8. Vaga encerrada e vaga duplicada em duas fontes.
9. Prompt injection na descrição.
10. Kill do processo nas três fases.

Envio real fica desligado em toda a suíte.

## 13. Riscos

| Risco | Mitigação |
|---|---|
| CAPTCHA frequente | sessão assistida; métrica de % bloqueada por ATS |
| ATS muda o DOM | reconhecedores por ATS, remapeamento validado, canário diário, quarentena |
| Cota insuficiente | prioridade por nota, cache, Ollama para rascunho, pausa e retomada |
| E-mail classificado errado | correção na tela vira regra; amostra revisada |
| Termos de fonte mudam | `tos_checked_at` por fonte, revisão trimestral, desligar a fonte |
| PC pesado à noite | `jobs.slice` com teto, Ollama descarrega modelo ocioso, 1 navegador de envio |
| Envio indevido ou duplicado | invariantes no banco, token do Guardião, testes de caos |
| Vazamento de dado | minimização, placeholders de PII, e-mail só local, logs redigidos |
| Escopo grande | fatia vertical primeiro, aceite por fase, retomada pelo PROGRESS |

## 14. O que destrava cada fase

| Item seu | Fase | Sem ele |
|---|---|---|
| Ok para instalar Ollama (sudo) e baixar modelos | 3 | regras e Claude/Codex apenas |
| CV ou export do LinkedIn, confirmação de fatos | 3 | dados de teste |
| Autorização de trabalho, inglês, pisos por moeda e vínculo | 3 | filtros ficam como "desconhecido" |
| App OAuth do Gmail ou senha de app | 4 | CRM só manual |
| Primeiro envio real e autorizações de rotina | 6 | tudo para em preparada ou revisão |
| Instalar units de usuário e ligar o timer | 7 | execução manual |
| Celular pelo Tailscale | qualquer | desligado |
