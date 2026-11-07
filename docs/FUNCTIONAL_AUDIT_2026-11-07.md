# Auditoria funcional — Job Orchestrator

**Registro histórico:** os achados abaixo se referem ao commit auditado. A main recebeu correções posteriores e a nova integração de LinkedIn e CAPTCHA; este documento não representa uma revalidação do estado atual. Consulte [o guia atual](REAL_LINKEDIN_AND_CAPTCHA.md).

Auditoria de 6–7 de novembro de 2026. Base: `6e1d04a978b06b747cf4dffcbf4a85543aed62bc`.

## Veredito

**O projeto tem componentes funcionais e coleta vagas reais, mas ainda não cumpre de forma confiável a promessa “vou dormir e ele procura, personaliza, candidata e acompanha tudo sozinho”.** Serve melhor, neste estado, como assistente de descoberta e preparação com supervisão. Não há evidência suficiente para liberar uma operação ampla sem acompanhamento.

Os maiores bloqueios são: LinkedIn real sem crawler integrado; CAPTCHA real sem resolução automática integrada; cobertura limitada de formulários; envio SMTP duplicável após crash; contato de candidatura extraído sem contexto suficiente; currículo com validação factual incompleta; configuração que declara prontidão sem testar todas as dependências.

Não existe evidência nesta auditoria de aumento de entrevistas ou contratações. Quantidade coletada, score interno, PDF legível e SMTP aceito são métricas intermediárias.

## Integração e padrão de prova

Foram mesclados os [PRs #1](https://github.com/gabrielandre-math/job-orchestrator/pull/1) e [#2](https://github.com/gabrielandre-math/job-orchestrator/pull/2), preservando as alterações recentes do proprietário. Os trabalhos correspondem aos laboratórios locais e ao fluxo de candidatura. A árvore auditada corresponde à main integrada acima. A release disponível consultada era `v1.0.4`: merge não atualiza os instaladores já publicados.

Esta entrega documenta defeitos; **não aplica as correções funcionais propostas**. Inclusive uma regressão introduzida pela feature de entrevistas integrada no PR #2 está identificada abaixo.

Legenda: **R** = reproduzido nesta auditoria; **C** = confirmado pelo caminho de código, sem execução completa do cenário; **L** = limitação explícita; **NV** = não verificado. P1 bloqueia confiança na operação autônoma ou afeta dados/envios; P2 reduz cobertura, qualidade ou previsibilidade. Prioridade é julgamento técnico, não classificação de vulnerabilidade.

## O que foi executado

| Verificação | Resultado | Limite da evidência |
|---|---|---|
| Build backend | Passou, zero erros e avisos | Não prova funcionamento externo |
| Build Angular de produção | Passou | Não substitui teste visual |
| Suíte .NET existente, excluindo E2E | 278 testes passaram | Cenários externos e browser não executados |
| Sondas adicionais de comportamento | 7 passaram | São testes que **confirmam defeitos**, não aprovação do produto |
| Laboratórios Python | 11 passaram | HTTP local, HTML sintético, CAPTCHA do Fake ATS |
| API + PostgreSQL descartável | Passou para captura/sanitização/aderência/entrevistas/contexto/hash/feedback/simulação | Modelo Ollama simulado; perfil sintético |
| Coleta pelos conectores reais | 40 conectores sondados; 39 retornaram registros; 2.667 registros brutos | Não são 2.667 vagas únicas, elegíveis, abertas ou candidaturas |
| E-mail | SMTP TLS real contra coletor local; duplicação e ausência de anexo reproduzidas | Nenhuma mensagem enviada a recrutador real; não valida Gmail/Outlook/IMAP externo |
| Apagar tudo | HTTP 500; entrevistas permaneceram | Falha reproduzida em banco descartável |
| Chromium | Inicialização impedida pelo ambiente: `socket() failed: Operation not permitted` | Não houve inspeção visual, PDF novo via navegador ou E2E real de ATS |
| Modelos reais e instaladores | NV nesta execução | Não medi inferência na GPU do usuário nem instalei em Windows/macOS |

Os dados de testes foram sintéticos, os bancos temporários foram removidos e as amostras individuais de vagas foram retiradas dos artefatos. A evidência persistida contém contagens, metadados das fontes, código de reprodução e mensagens fictícias.

## Matriz de capacidades

| Área | O que existe de fato | Avaliação |
|---|---|---|
| Fontes públicas/API/RSS | Diversos adaptadores efetivos, coleta e normalização | Funcional em amostras reais; falta cobertura de paginação/frescor e qualidade campo a campo |
| Descoberta de empresas e varredura | Catálogo, descoberta de boards e leitores genéricos | Parcial; catálogo não equivale a sites ativos nem à internet inteira |
| LinkedIn pessoal | Importação de dados do próprio perfil | Não equivale a buscar vagas ou contatos de terceiros |
| LinkedIn vagas | Captura assistida, alertas de e-mail, extrator de laboratório | Sem varredura real integrada do LinkedIn |
| E-mail de recrutadores | Campo de contato publicado em algumas fontes | Sem descoberta abrangente de recrutadores, vínculo validado ou extração real do LinkedIn |
| CAPTCHA | Solver de imagem/soma do laboratório; detecção no fluxo real | Real vai para `NeedsHuman`; não é solver universal |
| Candidatura ATS | Adaptadores Greenhouse, Lever, Ashby, Workable, SmartRecruiters | Cinco famílias de formulários; suporte real de ponta a ponta não homologado nesta auditoria |
| Gupy e demais links | Coleta e preparação de material, encaminhamento manual | Coletar vaga não implica conseguir se candidatar automaticamente |
| Perfil e importação | Fatos estruturados, identidade, preferências e confirmação | Útil; substituição do perfil e recuperação da importação precisam de correção |
| Ranking de vagas | Heurística transparente por campanha | Não representa aderência completa ao perfil, salário, idioma e senioridade |
| Comparação de aderência por IA | Requisitos e referências aos fatos | Consultiva; aceita referência existente mas sem relação com o requisito |
| Currículo personalizado | Seleção, redação, conferência, revisão, PDF e histórico | Implementado, porém veracidade, repetição e aprovação da revisão têm falhas |
| Inspetor ATS | Verificação interna do PDF/texto/geometria | Não comprova aprovação em todos os ATS |
| E-mail de candidatura/lembrete | Templates/IA, SMTP, limites, rotação de contas | Transporte funciona no coletor; falhas importantes de integridade e recuperação |
| Respostas e bounces | Leitura/classificação IMAP e ligação por referências/domínio | Código existente; heurística de associação arriscada; servidor externo NV |
| Provedores e roteamento | Ordem configurável, cotas, fallback local, cache | Funcional como política fixa; não há prova de otimização por tarefa |
| Entrevistas | Preparação por etapa, feedback anterior, referência ao currículo, prática local | API validada com modelo simulado; qualidade e idiomas não validados com modelo real |
| Campanhas/fila/autorização | Estados, outbox, limites, aprovação, intenção de envio | Boa base; recuperação real e transições têm lacunas |
| Painéis/assistente/fluxo/atenção/revisão | Telas e rotas implementadas | Build aprovado; UX examinada por código, sem avaliação visual ou usuários |
| Exportação/backup/retenção | Export JSON, backup do banco, limpeza parcial | Não equivalem a restauração integral nem a descarte automático de todos os dados |
| Instalação multiplataforma | Pacotes e caminhos Linux/macOS/Windows | Existência confirmada; instalação limpa e operação prolongada em cada SO NV |

## Achados priorizados e correções verificáveis

### A01 — P1/R — Envio duplicado após queda do processo

`MailService.SendDueAsync` mantém a transação aberta durante o SMTP. Se o servidor aceita a mensagem e o processo morre antes do commit, o banco volta a `Scheduled`. A nova tentativa gera outro `Message-ID`.

**Reprodução:** coletor local aceitou “Audit message 2”; matei a API antes da finalização; reiniciei e disparei o processamento. O coletor recebeu a mensagem duas vezes, com IDs diferentes. A trava advisory evita concorrência normal, mas não torna SMTP e PostgreSQL uma transação única.

**Correção/aceite:** persistir tentativa e Message-ID antes da chamada externa; após resultado ambíguo usar `SendUncertain`, reconciliar quando possível e impedir reenvio automático cego. Repetir crash antes/depois de DATA/250/commit e demonstrar ausência de duplicação. Message-ID estável sozinho não garante deduplicação pelo destinatário.

### A02 — P1/R — Currículo ausente é silenciosamente omitido do e-mail

`BuildAsync` anexa somente se `File.Exists(path)`. Um rascunho que aponta para arquivo perdido é enviado sem anexo e marcado `Sent`. O teste confirmou zero anexos no coletor. O envio também não compara hash do arquivo com o que foi aprovado.

**Correção/aceite:** guardar hash/tamanho da versão aprovada; validar imediatamente antes do transporte; arquivo ausente ou alterado bloqueia o envio com motivo claro. Testar exclusão e troca entre preparação e envio.

### A03 — P1/R — Extração pode escolher contato e link errados

`GitHubIssuesSource.Parse` escolhe o primeiro e-mail e primeiro link do texto. Na sonda, escolheu `privacy@empresa.example` e `/about`, apesar de depois existirem `jobs@empresa.example` e `/apply` sob “Como se candidatar”.

**Correção/aceite:** extrair contexto explícito de candidatura, preservar trecho de origem e distinguir recrutamento de privacidade/suporte. Sem evidência suficiente, não liberar envio automático. Testar múltiplos contatos e links em ordens diferentes.

### A04 — P1/R — “Apagar tudo” quebrou após integração das entrevistas

`Settings.cs` trunca `applications.applications`, mas não inclui a nova tabela `applications.interviews`, que possui FK. PostgreSQL rejeita a operação. A API retornou 500 e as duas entrevistas sintéticas continuaram consultáveis.

**Responsabilidade:** esta regressão veio da feature de entrevistas integrada no PR #2. A suíte anterior não cobria o cenário. A correção precisa incluir a tabela e um teste de exclusão com o schema completo; não basta testar entrevistas isoladamente. Depois, inventariar também arquivos, alertas, cache, backups e credenciais para que o alcance do botão seja explícito.

### A05 — P1/R — Checagem lexical permite afirmações sem suporte

`Claims.Unsupported` não demonstra relação semântica entre frase e evidência. Aceitou “implementei kubernetes em produção.” com apenas “Atuei no atendimento de suporte.”; também aceitou “Reduzi custos em 40%.” usando “Resolvi 40 chamados.”.

**Correção/aceite:** preservar sujeito, ação, tecnologia, contexto, unidade e magnitude por afirmação; frases sem suporte devem ser removidas ou revisadas. Criar conjunto adversarial com números reaproveitados, negações e tecnologias em minúsculas. Isso não se resolve apenas pedindo melhor ao modelo.

### A06 — P1/R+C — Revisão vazia é tratada como revisão aprovada

`ParseVerdicts({"verdicts":[]})` é aceito. `Conferente.Apply` não remove nada e o compositor marca `reviewed=true`. A sonda confirmou parser e aplicação; o restante é explícito no código.

**Correção/aceite:** exigir cobertura exata de todas as afirmações por identificador estável, rejeitar IDs ausentes/duplicados/desconhecidos e separar revisão incompleta de aprovação. Lista vazia nunca aprova currículo com afirmações.

### A07 — P1/C — Falta de revisão cruzada não impõe revisão humana

O compositor registra que a revisão cruzada indisponível fará o currículo ir para revisão do usuário. Contudo, `MaterialsStep` avança com qualquer `ComposeResult.Ok`; `Reviewed` entra no histórico, mas não constitui bloqueio de qualidade do envio. Em modo autorizado, material sem revisão cruzada pode prosseguir.

**Correção/aceite:** política de qualidade explícita no guardião, incluindo modo de geração, verificações e revisão. Demonstrar que material degradado segue exatamente a política apresentada na interface.

### A08 — P1/L — LinkedIn não entrega a varredura desejada

O registro marca a fonte LinkedIn como bloqueada. Importação do próprio perfil, captura por link/texto e alertas são caminhos diferentes. `linkedin_lab.py` processa localhost/arquivos e não está integrado como crawler do site real. Não há a cadeia “buscar LinkedIn → ler detalhes completos → identificar recrutador → obter e-mail vinculado à vaga”.

**Aceite de produto:** explicitar esses modos na UI e medir cobertura por campo/origem. Só declarar integração real quando houver teste da fonte real e método de acesso adequado. Não apresentar simulação como validação externa.

### A09 — P1/L — CAPTCHA real não é resolvido automaticamente

Os 11 testes do laboratório passaram, inclusive imagens/somas aceitas pelo servidor local. Isso comprova o experimento. No aplicativo real, `BotCheck` interrompe e devolve necessidade humana. Não existe evidência de integração operacional com reCAPTCHA, hCaptcha ou Turnstile, nem de retomada geral mantendo sessão.

**Aceite:** estados de desafio, sessão e retomada explícitos; testes por provedor em ambiente apropriado. Não prometer “qualquer CAPTCHA”: o comportamento correto para desafio não suportado é uma pendência clara.

### A10 — P1/L+C — Cobertura de coleta é muito maior que a de candidatura

Há cinco adaptadores de formulário registrados. Canais diferentes de `ats` viram `ManualHandoff`. E-mail tem worker próprio, separado. A reconciliação do adaptador genérico consulta recibos apenas nos hosts Fake ATS; fora deles retorna necessidade humana, sem portal de reconciliação.

**Aceite:** matriz por plataforma e versão: coletar, detalhar, preparar, enviar, confirmar e recuperar. Homologar separadamente cada etapa. Exibir a capacidade da vaga antes de o usuário confiar na execução noturna.

### A11 — P1/C — Revogar automático pode não parar e-mails já liberados

`ReleaseAsync` marca `ApprovedAt` inclusive para liberação automática. Revogação muda modo/consentimento, mas não invalida esses registros. `GuardAsync` aceita `ApprovedAt` sem distinguir consentimento automático revogado de aprovação individual.

**Aceite:** guardar origem da aprovação e autorização associada; revogar deve suspender pendências dependentes dela. Validar fila fora da janela, revogação e abertura posterior da janela. Esse cenário foi rastreado no código, não reproduzido de ponta a ponta nesta execução.

### A12 — P1/C — País de autorização de trabalho assume Brasil

`Bundles.Country` retorna US para algumas expressões americanas; todo o resto vira BR. Vagas no Canadá/Reino Unido/Europa ou localidade desconhecida podem receber contexto de autorização brasileiro.

**Aceite:** país estruturado e estado desconhecido; nunca inferir permissão de trabalho estrangeira pela autorização brasileira. Testar países, múltiplos países e remoto global.

### A13 — P1/C — Onboarding apaga o perfil antes de validar o substituto

`welcome.ts:97` executa DELETE do perfil existente antes do upload/importação do novo currículo. Arquivo inválido ou falha deixa o usuário sem o perfil anterior. O polling também não usa `finally`; falha na consulta pode manter o indicador de leitura ligado.

**Aceite:** importar em versão provisória e substituir apenas após sucesso e confirmação; restaurar estado visual em erro; deixar clara a recuperação. Testar PDF inválido, falha de rede e extração interrompida.

### A14 — P2/R — Bullets repetidos sobrevivem ao acabamento

`Tailor.Finish` não elimina duas frases idênticas em uma experiência; sonda reproduziu. A instrução de personalizar não constitui controle de repetição literal/semântica nem comparação entre currículos para vagas distintas.

**Aceite:** deduplicação dentro do currículo, revisão semântica e avaliação de relevância por vaga. Não forçar novidade artificial: fatos corretos podem aparecer em várias candidaturas, mas não devem ocupar espaço repetidamente no mesmo documento.

### A15 — P2/R — Score de aderência aceita fato irrelevante

Na nova comparação, um requisito literal “Experiência com Kubernetes” recebeu `direct` ligado ao ID de “Concluí curso de gastronomia.”; o parser retornou 100. Existência do ID não demonstra suporte. Requisitos omitidos pelo modelo também não penalizam a nota.

**Aceite:** exigir evidência que suporte o requisito e cobertura dos requisitos materiais. Manter a nota como consultiva enquanto isso. Ela não altera o ranking principal automaticamente. É outra limitação da feature integrada no PR #2.

### A16 — P2/C — Ranking não incorpora todas as preferências

`Scoring` usa stack/cargo/senioridade/modalidade, com pesos 45/25/15/15. Os dois primeiros termos viram essenciais. Senioridade é estimada por título, sem a senioridade do perfil. Pisos de salário servem para respostas de formulário, mas não estão integrados como filtro completo de seleção; preferência de modalidade e idioma também não está representada integralmente nessa avaliação.

**Aceite:** separar restrições obrigatórias de preferências e pontuação; apresentar quais foram verificadas, desconhecidas ou violadas. Testar vagas com mesma stack e condições incompatíveis.

### A17 — P2/R+C — Vaga antiga pode continuar aberta

O leitor JSON-LD marcou como aberta uma vaga com `validThrough` em 2020. A descoberta não fecha genericamente anúncios ausentes das respostas; respeita tombstones quando o conector os fornece. Ausência numa página truncada também não pode ser confundida com fechamento.

**Aceite:** interpretar validade, conservar `lastSeen`, diferenciar coleta completa de parcial e revalidar no destino antes da candidatura. Atualizar URL de candidatura e elegibilidade quando o anúncio muda.

### A18 — P2/C — Limites de paginação reduzem a cobertura

Há conectores com lotes fixos/limites: Workday, Oracle, SmartRecruiters, GitHub e outros. Exemplo: o teste Workday retornou 20; não demonstra todas as vagas daquela empresa. “HTTP 200” e “fonte funcionando” não demonstram completude.

**Aceite:** cursor persistente, motivo de parada, quantidade por página e indicação de cobertura parcial; deduplicação entre páginas/rodadas. Comparar conjuntos contra uma amostra de referência.

### A19 — P2/C — Mudar de “só buscar” não recupera automaticamente o passado

O consumidor de shortlist não cria candidatura em `DiscoveryOnly`. Vagas já `Shortlisted` não republicam automaticamente o evento ao mudar a campanha. Falta um backfill explícito para as já descobertas.

**Aceite:** oferecer “preparar vagas já encontradas”, processar de forma idempotente e mostrar quantidade. Testar descoberta seguida de mudança de modo.

### A20 — P2/C — “IA pronta” é mais fraco que “fluxo pronto”

O onboarding aceita disponibilidade de provedor; o probe Ollama pode considerar disponibilidade parcial quando falta um dos modelos configurados. Uma tarefa forte ainda pede o modelo ausente. O teste de provedor é uma resposta curta, sem perfil, currículo ou formulário.

O instalador trata ausência de navegador como aviso; sua mensagem enfatiza páginas JavaScript, mas renderização de PDF e formulários também dependem dele. Existência de `secret-tool` não prova chaveiro desbloqueado/operacional.

**Aceite:** prontidão por capacidade e ensaio completo com dados sintéticos: extrair perfil, redigir, gerar PDF, preencher Fake ATS e enviar ao coletor. Modelo rápido e forte testados separadamente; falha deve apontar a capacidade bloqueada.

### A21 — P2/C — Roteamento funciona, mas não é demonstravelmente ótimo

O roteador segue ordem de contas, tarefa/degrau, orçamento e fallback. Redação/importação usam nuvem primeiro em configuração normal; tarefas simples usam local; falha/cota pode levar ao modelo local forte. Não há avaliação que compare qualidade, latência e custo por tarefa para escolher a rota.

Uma resposta marcada `Ok` pode ser armazenada antes da validação semântica pelo consumidor. JSON parseável com conteúdo inválido não provoca automaticamente nova tentativa em outro provedor. O cache de 30 dias não inclui toda a configuração/modelo da conta, podendo reaproveitar resultado após mudança de modelo.

**Aceite:** validar schema e conteúdo antes de aceitar/cachear; retry/escalada controlados com motivo; cache versionado por política/modelo/prompt/validador; benchmark representativo por tarefa, incluindo currículo longo e revisão adversarial.

### A22 — P2/C+NV — Modelos existem; adequação universal não foi provada

As páginas oficiais de [gemma4:e4b](https://ollama.com/library/gemma4:e4b) e [qwen3.5:9b](https://ollama.com/library/qwen3.5:9b) responderam 200 com os nomes corretos. `docs/research/LLM_BENCH.md` contém benchmark histórico de 50 casos de extração/classificação/mapeamento, inclusive RTX 4060 Ti 8 GB. Não foi repetido nesta auditoria e não mede qualidade de currículos ou entrevistas.

No seu i5-14600KF/32 GB/4060 Ti 8 GB, a proposta merece um ensaio local, mas tamanho do arquivo não é consumo total de VRAM: contexto, runtime, offload e concorrência importam. Instalação e aceleração em NVIDIA/AMD/Apple/CPU variam. O conserto de extração Ollama/MLX da v1.0.4 foi preservado, sem homologação em Mac nesta execução.

**Aceite:** matriz SO/arquitetura/GPU, memória máxima e p50/p95 por tarefa, execução prolongada com navegador e modelo; redução controlada de contexto/tamanho quando faltar memória.

### A23 — P2/C — Privacidade declarada não é sempre uma regra rígida

`email.classify` diz “nunca vai para a nuvem”, mas não possui `LocalOnly:true`. Seu padrão é local e orçamento zero; contudo, configuração que altera degrau e orçamento pode habilitar rota cloud. As novas tarefas de aderência/entrevista possuem essa trava, testada com override. `Privacy:LocalOnly` global também é respeitado.

**Aceite:** se a promessa for absoluta, codificá-la como política inviolável por configuração de roteamento. Diferenciar armazenamento local de processamento em provedor externo. Os testes desta auditoria não enviaram currículos reais para nuvem.

### A24 — P2/C — Resposta de recrutador pode ser associada à vaga errada

Sem referências de thread, o IMAP associa pelo domínio do remetente a um registro do agrupamento de envios. Várias vagas da mesma empresa/agência tornam a escolha ambígua. Contato novo classificado como recruiter outreach sem vínculo pode ser ignorado. O cursor `LastUid` não guarda `UIDVALIDITY` para troca de caixa.

**Aceite:** usar thread/identificadores e contexto de vaga, preservar ambiguidade como pendência, aceitar oportunidade nova sem inventar vínculo, tratar troca de UIDVALIDITY. Validar IMAP com múltiplas vagas e recrutadores.

### A25 — P2/C — E-mail e candidaturas mantêm resultados separados

A candidatura por e-mail pode ficar em encaminhamento manual enquanto o módulo Mail envia e acompanha por conta própria. Status/resposta no Mail não equivale a transição da candidatura principal. O modo de campanha “só buscar” não é, sozinho, interruptor global do modo automático de e-mail.

**Aceite:** histórico unificado por candidatura, estado por canal e parada global que explique o que interrompe. Métricas devem distinguir preparado, SMTP aceito, confirmado e respondido.

### A26 — P2/C — Recuperação e concorrência têm hipóteses frágeis

`ReleaseStaleAsync` libera leases de outro processo na mesma máquina sem confirmar se morreu. Duas instâncias podem disputar trabalho ativo. Na importação de perfil, o recebimento é registrado antes da extração terminar, exigindo recuperação explícita após crash.

**Aceite:** exclusão de instância ou fencing/heartbeat verificável; testes com dois workers e falha durante importação, geração e envio. Limites devem valer para o conjunto dos processos.

### A27 — P2/C — Backup e retenção não representam todo o estado

Backup do PostgreSQL não inclui automaticamente PDFs, currículo original, configuração e chaveiro. Restaurar apenas o banco pode deixar referências para arquivos inexistentes. Retenção cobre mensagens/chamadas/snapshots/cache, com mínimo de sete dias; não é um modo “descartar absolutamente tudo ao fim de cada teste”.

**Aceite:** manifesto completo, backup/restauração de banco+arquivos compatíveis, credenciais reconectadas claramente; modo de laboratório isolado e descartável. Testar restauração numa instalação vazia, sem reutilizar arquivos antigos.

### A28 — P2/C+NV — Evidência final e idiomas precisam melhorar

Proveniência salva em `ResumeVersion` usa linhas anteriores a alterações de revisão/corte, podendo não descrever exatamente o PDF final. O inspetor mede propriedades internas, não compatibilidade universal. Preparação de entrevista usa prompts orientados a português e fatos atuais, além da referência arquivada; não há prova de avaliação multilíngue consistente ou de fatos históricos idênticos aos enviados.

**Aceite:** vincular cada frase final à versão dos fatos; guardar artefato e hash realmente enviados; selecionar idioma explícito; testar documentos longos, revisão/cortes e mudança de perfil após candidatura.

### A29 — P2/C+NV — UX e testes de entrega não sustentam a confiança anunciada

O produto oferece bons recursos de explicação, atenção, revisão, histórico e limites. Porém “Automatizar tudo” exige cruzar perfil, provedores, campanha, fontes, serviço e e-mail; os estados de prontidão não provam o caminho completo. Muitas áreas próximas — busca, varredura, fontes, registro, cobertura, conectores — exigem entender a arquitetura para decidir o que fazer.

Não localizei testes `.spec.ts` nas telas; o workflow versionado encontrado é de Pages. Não há, nesta base, evidência de CI cobrindo a matriz completa de instaladores, migrações e jornadas. Acessibilidade, teclado, leitura em telas menores e legibilidade precisam de validação visual; não atribuí nota fictícia a elas.

**Aceite:** primeiro uso com uma única meta (“encontrar e preparar uma vaga válida”), diagnóstico com próximo passo concreto, execução noturna resumida por resultados e pendências, CI de build/testes/migrações, ensaio de instalação em máquinas limpas e testes com usuários.

## Ordem recomendada de trabalho

1. **Integridade e controle:** A01–A07, A11–A13. Impedir duplicação, anexo errado/ausente, destino incorreto, aprovação factual falsa, revogação ineficaz e perda de dados.
2. **Busca que produz oportunidades válidas:** A15–A19 e A24–A25. Frescor, elegibilidade, contatos com origem e continuidade entre descoberta/candidatura/resposta.
3. **Automação honesta por plataforma:** A08–A10. Publicar matriz de capacidades; homologar fluxos completos antes de ampliar a promessa.
4. **Onboarding e operação local:** A20, A22, A26–A29. Ensaio de prontidão, recuperação, restauração, instaladores e UX.
5. **Qualidade e custo da IA:** A14, A21, A23 e A28. Benchmark de currículo/revisão, validação semântica e roteamento medido.

## Ensaio mínimo para considerar a promessa noturna confiável

Executar uma campanha de 8 horas em ambiente controlado com fontes reais limitadas e destinos de teste. Incluir reinício, internet interrompida, cota esgotada, modelo indisponível, arquivo removido, mudança de modo e autorização revogada. O navegador precisa estar operacional e a máquina acordada; fechar a janela não é o mesmo que suspender o sistema.

Critérios propostos: zero duplicações; zero destinos sem evidência; zero envio depois da revogação; todo anexo com hash validado; todo envio com resultado confirmado ou explicitamente incerto; nenhum erro silencioso; vagas rejeitadas com motivo; currículos avaliados contra fatos e relevância; tarefas pendentes recuperáveis. Depois, piloto pequeno com candidaturas reais previamente revisadas e medição de respostas/entrevistas. Isso ainda não garante conseguir emprego, mas verifica se o app executa a sua parte corretamente.

## Evidências e prompt aplicado

- [Prompt de auditoria aplicado](AUDIT_PROMPT.md).
- [Resumo estruturado dos testes](audit-evidence/validation.json).
- [Sondagens reais do registro](audit-evidence/live-source-probes.json) e [sondagens adicionais](audit-evidence/extra-source-probes.json).
- [SMTP/crash/exclusão](audit-evidence/mail-and-wipe.json) e [log HTTP](audit-evidence/http-validation.log).
- [11 testes dos laboratórios](audit-evidence/labs-validation.log).
- [Sete sondas de defeitos em C#](audit-evidence/AuditBehaviorTests.cs.txt).
- [Instruções de reprodução](audit-evidence/README.md).

Os achados por código são limitados ao commit auditado. Resultados de fontes públicas variam no tempo. A presença de testes verdes não invalida os defeitos reproduzidos fora da cobertura original.

## Índice do código auditado

| Achado | Fonte no commit auditado |
|---|---|
| A01–A03, A11, A24–A25 | [MailService.cs:323](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Mail/JobOrchestrator.Mail/Features/MailService.cs#L323) |
| A03 | [CommunitySources.cs:9](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Automation/JobOrchestrator.Automation/Adapters/CommunitySources.cs#L9) |
| A04 | [Settings.cs:61](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Operations/JobOrchestrator.Operations/Features/Settings.cs#L61) |
| A05 | [Claims.cs:20](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Profile/JobOrchestrator.Profile.Contracts/Claims.cs#L20) |
| A06 | [Draft.cs:115](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Materials/JobOrchestrator.Materials/Compose/Draft.cs#L115) |
| A06–A07, A14, A28 | [ResumeComposer.cs:83](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Materials/JobOrchestrator.Materials/Compose/ResumeComposer.cs#L83) |
| A08 | [SourceRegistry.cs:134](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Automation/JobOrchestrator.Automation.Contracts/SourceRegistry.cs#L134) |
| A09–A10 | [GreenhouseLikeForm.cs:106](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Automation/JobOrchestrator.Automation/Adapters/GreenhouseLikeForm.cs#L106) |
| A10 | [AutomationModule.cs:41](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Automation/JobOrchestrator.Automation/AutomationModule.cs#L41) |
| A07, A10 | [Steps.cs:49](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Applications/JobOrchestrator.Applications/Features/Workflow/Steps.cs#L49) |
| A12 | [Profile.cs:31](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Applications/JobOrchestrator.Applications/Features/Workflow/Profile.cs#L31) |
| A13 | [welcome.ts:97](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/web/projects/app/src/app/features/welcome/welcome.ts#L97) |
| A15 | [FitComparison.cs:50](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Jobs/JobOrchestrator.Jobs/Features/FitComparison.cs#L50) |
| A16 | [Scoring.cs:26](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Jobs/JobOrchestrator.Jobs/Domain/Scoring.cs#L26) |
| A17 | [PageReader.cs:214](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Automation/JobOrchestrator.Automation/Adapters/PageReader.cs#L214) |
| A17 | [Discovery.cs:13](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Jobs/JobOrchestrator.Jobs/Features/Discovery.cs#L13) |
| A19 | [Shortlisted.cs:15](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Applications/JobOrchestrator.Applications/Features/Shortlisted.cs#L15) |
| A20 | [Setup.cs:98](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Bootstrappers/JobOrchestrator.App/Setup.cs#L98) |
| A20 | [Providers.cs:270](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Intelligence/JobOrchestrator.Intelligence/Providers/Providers.cs#L270) |
| A21 | [IntelligenceApi.cs:70](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Intelligence/JobOrchestrator.Intelligence/Features/IntelligenceApi.cs#L70) |
| A22 | [LocalSetup.cs:15](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Intelligence/JobOrchestrator.Intelligence/Features/LocalSetup.cs#L15) |
| A23 | [Contracts.cs:58](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Intelligence/JobOrchestrator.Intelligence.Contracts/Contracts.cs#L58) |
| A26 | [Queue.cs:57](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Applications/JobOrchestrator.Applications/Features/Workflow/Queue.cs#L57) |
| A26 | [Import.cs:15](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/Modules/Profile/JobOrchestrator.Profile/Features/Import.cs#L15) |
| A27 | [PortablePostgres.cs:190](https://github.com/gabrielandre-math/job-orchestrator/blob/6e1d04a978b06b747cf4dffcbf4a85543aed62bc/backend/src/BuildingBlocks/JobOrchestrator.BuildingBlocks.Data/PortablePostgres.cs#L190) |
