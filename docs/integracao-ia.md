# Integração com Claude, ChatGPT (Codex) e Ollama

Data: 05/11/2026. Versões testadas: Claude Code 2.1.281 e codex-cli 0.162.1.

Objetivo: funcionar sem você pensar nela.
- Conecta uma vez.
- Nunca cobra API.
- Nunca atrapalha o seu uso do Claude Code e do Codex no dia a dia.
- Quando algo falha, diz o que houve e o que já foi feito no lugar.

## 1. Conectar, uma vez

| Provedor | O painel confere | Se faltar |
|---|---|---|
| Claude | `claude --version`, `claude auth status` (assinatura ou API key) | botão Conectar abre um terminal com `claude auth login` |
| Codex (ChatGPT) | `codex --version`, `codex login status` (ChatGPT ou API key) | botão Conectar abre um terminal com `codex login` |
| Ollama | serviço no ar, modelos instalados, VRAM livre | botão para instalar ou baixar o modelo, com seu ok |

- O login é o de sempre, no navegador.
- O painel percebe sozinho quando terminou.
- O orquestrador nunca vê senha nem token.
- O botão Testar faz uma chamada mínima com schema e mostra latência e modelo.

## 2. Sem surpresa de cobrança

- **Ambiente limpo.** O processo filho recebe só `PATH`, `HOME`, `LANG`, `TERM` e `XDG_*`. Saem `ANTHROPIC_API_KEY`, `ANTHROPIC_BASE_URL`, `CLAUDE_CODE_USE_BEDROCK`, `CLAUDE_CODE_USE_VERTEX`, `AWS_*`, `OPENAI_API_KEY`, `OPENAI_BASE_URL`, `GEMINI_API_KEY`, `GOOGLE_API_KEY` e `GOOGLE_GENAI_USE_VERTEXAI`.
- **Login conferido toda noite.** Antes de cada noite o `auth status` confirma o login por assinatura. Se o método virou API key, o provedor fica bloqueado e você recebe aviso.
- **`--bare` proibido.** Nesse modo o Claude Code aceita só API key.
- **API paga** existe só como stub desligado.

## 3. Sem atrapalhar o seu uso

Isolamento de cada chamada:
- **Claude:**
  - cwd vazio próprio;
  - `--tools ""`, `--strict-mcp-config`, `--no-session-persistence`;
  - `--setting-sources` sem `user`, para seus hooks, plugins e permissões não carregarem (validar que o login segue funcionando);
  - `--system-prompt` próprio, sem o prompt longo do Claude Code, o que também gasta menos.
- **Codex:**
  - `--ephemeral`, `--sandbox read-only`, `--skip-git-repo-check`, cwd vazio;
  - os 2 MCP do seu `~/.codex/config.toml` desligados por `-c` (validar a chave).
- **Instruções globais:** hoje não existem `~/.claude/CLAUDE.md` nem `~/.codex/AGENTS.md`. Se um dia aparecerem, o probe avisa, porque seriam carregados.
- **Resultado:** nada aparece no seu `claude --resume`, no histórico do Codex nem na sua thread viva do Codex. Nenhuma memória sua é lida ou escrita.

Cortesia de cota:
- **Uso real lido onde existe:**
  - Claude: `~/.claude/state/statusline.json`, campo `rate_limits` (`five_hour` e `seven_day`, `used_percentage`, `resets_at`), gravado pela sua statusline. Em 05/11 marcava 42% na semanal.
  - Codex: eventos `rate_limits` do `codex exec --json` (`primary`, `secondary`, `rate_limit_reached_type`, `plan_type`, hoje `plus`).
- **Reserva para você:** o orquestrador para de usar um provedor quando a janela de 5 h passa de 60% ou a semanal passa de 75%. Os valores são configuráveis.
- **Sem dado:** o uso aparece como `unknown` e valem só os tetos por noite.
- **Você em uso:** se você estiver usando o Claude Code ou o Codex na hora, o que pode esperar espera.
- **Uma chamada por vez** por provedor.
- **Limite atingido:** pausa aquele provedor até o `resets_at`, passa a tarefa para o próximo da cadeia e retoma sozinho depois.

## 4. Qualidade

- **Prompts e schemas:** prompts versionados em `prompts/<tarefa>/<versão>.md`, com schema JSON por tarefa. Toda saída é validada; se vier inválida, há um reparo com o erro do validador e, depois disso, falha com mensagem clara.
- **Modelos:** por alias (`haiku`, `sonnet`) para acompanhar versões novas. O modelo real usado fica gravado.
- **Escrita e revisão:** um escreve e outro revisa (Claude escreve, Codex confere), porque modelos de famílias diferentes erram em lugares diferentes.
- **Parâmetros:** idioma, tom e limite de caracteres do campo são parâmetros, e não pedidos soltos no prompt.
- **`jo llm bench`:** com vagas e perfis de teste, mede JSON válido, afirmação sem fonte, aderência à vaga e latência. Roda quando você pede ou quando um CLI atualiza.
- **Cache por entrada:** o mesmo pedido nunca é feito duas vezes.

## 5. Atualizações dos CLIs

- **Checagem:** no boot e quando a versão muda, o runner confere no `--help` se as flags exigidas existem e faz o teste mínimo.
- **Matriz no código:** versão testada, flags exigidas, formato de saída.
- **Se algo mudou:** o provedor fica "degradado" com a mensagem exata (ex.: "flag X sumiu na versão Y"), a cadeia segue com o próximo, e nada falha em silêncio.
- **Testes do runner:** contra um CLI falso que imita saídas normais, limite atingido, login expirado, timeout e JSON quebrado. Não gasta cota.

## 6. Privacidade

- **Dados pessoais:** o modelo nunca vê nome, e-mail, telefone, endereço ou documento. Placeholders são trocados só na renderização.
- **Prompt mínimo:** só os fatos selecionados para a vaga vão no prompt.
- **E-mail:** o e-mail bruto só passa pelo Ollama.
- **Modo privado:** um interruptor desliga Claude e Codex de uma vez. Tudo segue local, com os textos marcados como de qualidade menor.
- **Registro de cada chamada:** tarefa, provedor, modelo, duração, cache, schema válido e hashes. O texto completo só é guardado no modo diagnóstico, redigido e com retenção curta.

## 7. Quando algo dá errado

| Situação | O que acontece | O que você vê |
|---|---|---|
| Login expirou | provedor pausa, tarefas vão para o próximo | aviso com botão Conectar |
| Limite atingido | pausa até o reset, a cadeia segue | "Claude no limite desde 02:14, volta 05:00; Codex fez 6 currículos" |
| CLI atualizou e mudou flag | provedor degradado | a mensagem exata e a última versão que funcionava |
| Saída inválida duas vezes | a etapa vai para o próximo provedor | registro na execução |
| Timeout | o grupo de processos é encerrado; retry só se a etapa for segura | tempo e etapa |
| Todos fora | o CV final não sai como definitivo: vira rascunho marcado ou espera | linha no resumo da manhã |

## 8. Tela Provedores

Um card por provedor, com:
- status, versão e método de login;
- modelo em uso;
- uso real (ou `unknown`) e a reserva configurada;
- chamadas da noite por tarefa e latência;
- último erro;
- botões Conectar, Testar e Pausar.

No topo fica o interruptor do modo privado.

## 9. Várias contas e ordem de gasto

Cada conta vira um provedor separado, com login, cota e reserva próprios. A ordem abaixo é a padrão e pode ser mudada arrastando na tela Provedores.

| Ordem | Provedor | Como conecta | Estado em 05/11 |
|---|---|---|---|
| 0 | Ollama na RTX (local) | serviço de usuário | a instalar; sempre primeiro nas tarefas locais (extração, e-mail, embeddings, resumo) e último recurso para rascunho |
| 1 | Claude Pro (segunda conta) | pasta própria: `CLAUDE_CONFIG_DIR=~/.local/share/job-orchestrator/accounts/claude-pro`, login uma vez com `claude auth login` nessa pasta | a conectar |
| 2 | ChatGPT Plus (Codex) | `~/.codex` atual | logado, plano `plus` |
| 3 | Google AI Pro (Gemini CLI) | `~/.gemini` atual, login Google | logado (`oauth-personal`), CLI 0.60.0 (0.67.0 disponível) |
| 4 | Claude Max (o "ultra") | `~/.claude` atual | logado, `subscriptionType: max`; reserva alta: só entra com a janela de 5 h abaixo de 30% e a semanal abaixo de 50% |

Regras:
- A ordem vale para tarefas de nuvem. Cada tarefa percorre a lista e pula quem não atende a qualidade exigida.
- O revisor é sempre de família diferente do redator. Exemplo: se o Claude escreveu, Codex ou Gemini conferem.
- Cada conta pausa no próprio limite ou na própria reserva e volta sozinha depois do reset.
- Como cada conta tem a sua pasta, a conta Pro não carrega nada do seu Claude Code (plugins, hooks, MCP, memória).
- Uso real por conta:
  - Claude Max: pela sua statusline;
  - Claude Pro: só por erro de limite e teto por noite, porque não há statusline nessa pasta;
  - Codex: pelos eventos `rate_limits`;
  - Gemini: por erro de cota e pelas estatísticas do JSON (a validar).
- Gemini sem interação: `gemini -p ... -o json -m <modelo>`, com extensões e MCP desligados (`-e`, `--allowed-mcp-server-names`) e ferramentas negadas pelo Policy Engine (a validar).
- Nos termos de consumidor da Anthropic não há cláusula contra ter mais de uma conta (lido em 05/11). O sistema não cria conta, não compartilha login e usa cada conta dentro do próprio limite.
- O Gemini recebe a mesma minimização dos outros, sem PII. A política de uso de dados do plano Google deve ser conferida antes de ligar.

## 10. Modelo e esforço na medida

Cada tarefa usa o modelo e o esforço mínimos que dão conta do trabalho. Modelo top-tier nunca é a primeira tentativa.

Degraus equivalentes entre provedores (nomes do Codex confirmados no bench):

| Degrau | Claude | Codex | Gemini | Ollama |
|---|---|---|---|---|
| R0 local | | | | modelo do bench |
| R1 leve | Haiku, `--effort low` | luna ou terra, esforço baixo | Flash | |
| R2 médio | Sonnet, `--effort medium` | luna ou terra, esforço médio | Flash | |
| R3 forte | Sonnet, `--effort high` | sol, esforço médio | Pro | |
| R4 top | Opus ou Fable, `--effort high` | sol, esforço alto | Pro | |

Ponto de partida até o bench calibrar:

| Tarefa | Começa em | Sobe para |
|---|---|---|
| fit.explain | R0 | R1 |
| question.map | R1 | R2 |
| question.draft | R1 | R2 |
| form.remap | R1 | R2 |
| resume.factcheck | R1, família diferente do redator | R2 |
| cover.write | R2 | R3 |
| resume.tailor | R2 | R3, depois R4 |
| interview.prep | R2 | R3 |

Regras:
- **Subir de degrau.** Só quando a saída falha na validação: schema inválido depois do reparo, afirmação sem fonte na checagem ou nota de qualidade abaixo do corte.
- **Cota não sobe degrau.** Limite de cota troca de conta no mesmo degrau (§9).
- **R4 é exceção.** Tem teto próprio de 2 por noite e só vale para `resume.tailor`.
- **Sempre explícito.** Cada chamada passa modelo e esforço explícitos e nunca herda o padrão do seu CLI. O Codex está configurado com `gpt-6.1-sol` em `xhigh` no `~/.codex/config.toml`, e sem override cada chamada sairia no degrau mais caro.
- **Controles de esforço.** Claude usa `--effort low|medium|high|xhigh|max`. Codex usa `-c model_reasoning_effort=...` com `-m`. O Gemini CLI não expõe esforço, então escolhe só entre Flash e Pro.
- **Calibração.** O `jo llm bench` acha o degrau mínimo de cada tarefa que passa no corte nas fixtures: 100% de JSON válido, zero afirmação sem fonte e aderência acima do limite. Roda de novo quando um CLI ou modelo muda. A tela Provedores mostra o resultado, por exemplo: "resume.tailor: Sonnet médio passou, Haiku reprovou por afirmação sem fonte".
- **Rastreio.** Cada material registra quem fez: conta, modelo e esforço.
