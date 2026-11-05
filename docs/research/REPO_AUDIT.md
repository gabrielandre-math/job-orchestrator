# Auditoria dos repositórios de referência

Data: 05/11/2026. Clones rasos em `references/` (fora do git). Só leitura de README, manifestos e trechos de código; nenhum script dos repos foi executado.

| Repo | Commit | Data | Licença | Stack |
|---|---|---|---|---|
| suxrobGM/jobpilot | 794445f | 2026-11-04 | MIT (LICENSE; `package.json` sem campo) | Bun/TS (api, web), .NET (terminal host), plugin Claude Code/Codex |
| vaibhavarora14/job-application-agent | c3ff066 | 2026-11-04 | MIT | Node 20+, dependência única `sql.js`, Agent Skill + CLI, workers Cloudflare |
| R238Company/meu-emprego-novo | e51e667 | 2026-11-03 | MIT | Python, `crewai[bedrock]==1.15.4`, langchain-* |
| Aditya-00a/Instaply | 1c0c9ea | 2026-06-10 | MIT | Python + pnpm, SQLite, Playwright, extensão |
| Liam-Frost/AutoApply | 47d9e8b | 2026-05-22 | PolyForm Noncommercial 1.0.0 | Python, Postgres + Redis + Celery, React |

## JobPilot 2.3.3

- **O que é:** plugin para Claude Code/Codex, mais um "terminal host" local (.NET, `apps/terminal`), mais uma API (Bun + Prisma) e um dashboard web.
- **Dependências externas:** o README manda criar conta em `jobpilot.suxrobgm.net` e subir o currículo lá (README.md:61-78). O perfil sai da máquina.
- **Fontes:** declara busca em LinkedIn, Indeed e Hiring Cafe (README.md:39-60). Os termos de LinkedIn e Indeed proíbem automação.
- **CAPTCHA:** a API tem solver de CAPTCHA via `@2captcha/captcha-solver` e CapSolver (`apps/api/src/modules/captcha/captcha.service.ts:1-43`). Viola a regra de não contornar CAPTCHA.
- **Outros recursos:** outreach para recrutador (e-mail e LinkedIn, com aprovação), leitura de respostas no Gmail, conector Upwork, "Pilot" com limite diário e notificação no celular.
- **Decisão: REJECT para integração.** Fontes proibidas, conta hospedada, solver de CAPTCHA, dados fora da máquina.
- **Ideias que valem:** piloto com limite diário, diário de bordo, notificação no celular, PDF salvo por versão de currículo, casar respostas do Gmail com candidaturas.

## JobAppAgent 3.8.6

- **O que é:** Agent Skill instalada em `~/.agents/skills/job-application-agent`, mais um CLI (`job-application.mjs`) que guarda perfil, importa currículo, dá nota, checa duplicadas e mantém fila de atenção e ledgers. A descoberta e o navegador ficam com o agente hospedeiro (README.md:147-162). Cada candidatura é conduzida por LLM e gasta cota.
- **Modos:** `review-each` (padrão, para antes do Submit) e `routine-auto` (permissão permanente via `autonomy grant`, revogável). A página de aprovação separada foi removida na 3.8.6 (README.md:104-133).
- **Travas:** pausa em senha, SSO, MFA, CAPTCHA, declaração legal, documento oficial, perguntas demográficas, autorização de trabalho, salário ou local pouco claros, fatos não verificáveis (README.md:134-146). Registra intenção antes do envio, preserva "sent-unverified" e inspeciona o ATS antes de qualquer retry, confirma só com sucesso visível (`platforms.json`, instructions).
- **Privacidade:** três coisas vêm ligadas por padrão (README.md:216-247):
  - analytics (PostHog), por install ID;
  - nome e e-mail do perfil enviados a um banco de contatos do mantenedor;
  - compartilhamento anônimo das vagas confirmadas.
  Estado em nuvem (D1) é opcional. Assistente LLM opcional (Free.ai).
- **CAPTCHA:** módulo de solver CapSolver/2captcha, desligado por padrão (`scripts/captcha-vendor.mjs:1-12`).
- **Instalação:** `npx job-application-agent@latest install`, que executa código remoto.
- **Decisão: REFERENCE_ONLY.** Os motivos:
  - o navegador é dirigido por agente, o que custa cota e não é determinístico;
  - o compartilhamento de dados vem ligado por padrão;
  - há código de solver de CAPTCHA.
  A semântica das travas confirma o desenho do nosso `SubmissionGuard`.
- **Ideias que valem:** `review-each` / `routine-auto`, intenção persistida antes do clique, "sent-unverified" sem retry, fila de atenção, "filled ≠ applied".

## meu-emprego-novo

- **O que é:** pipeline CrewAI com 5 agentes: estrategista, gerente (nota de 1 a 10), editor, candidato e coach de entrevista. O ciclo gerente ⇄ editor para quando:
  - a nota chega a 8.5;
  - uma revisão não melhora a nota;
  - o limite de 3 revisões é atingido.
  Fica a versão de melhor nota (README.md:116-155).
- **Entrada e saída:** recebe `profile_truth.md`, currículos antigos e a vaga. Gera JSON, LaTeX, PDF, revisão, relatório e guia de entrevista.
- **Provedores:** só API paga (Anthropic, OpenAI, Gemini, xAI) ou Bedrock, e o `.env` vem com `bedrock` como padrão (README.md:171-182, `src/llm_factory.py:7-22`, `src/bedrock_llm.py`). Nesta máquina o boto3 usaria o perfil `default` do `~/.aws`. Não há Ollama nem CLI.
- **Custo:** cada execução chama o provedor várias vezes mais do que o número de agentes (README.md:150-155).
- **Decisão: REFERENCE_ONLY.** Usar com Ollama ou CLI exigiria mudar o código, e a regra do usuário proíbe portar código de terceiros.
- **Ideias que valem:** `profile_truth.md` com prioridade sobre tudo, base `resume.json` imutável, parar quando a revisão não melhora, guardar a melhor versão, guia de entrevista a partir do currículo final.

## Instaply

- **O que é:** local, SQLite + JSON, sem telemetria declarada. Usa o Chrome real do usuário, com cookies e IP residencial (README.md:197-222), o que vai contra a regra de perfil isolado.
- **Travas:** pausa em hCaptcha e reCAPTCHA Enterprise e deixa o Submit para a pessoa. `INSTAPLY_AUTO_CLOSE=1` pula a espera; a confirmação é marcada à mão (README.md:251-293).
- **Manutenção:** parado desde junho, 17 estrelas. O repo tem `fly.toml`, `supabase` e notas de pagamento (Paddle), o que conflita com o "sem servidor" do README.
- **Decisão: REFERENCE_ONLY.**
- **Ideias que valem:** memória de respostas salvas, cobertura de vários tipos de campo.

## AutoApply

- **O que é:** PolyForm Noncommercial 1.0.0. Arquitetura próxima da nossa:
  - Job Index com snapshots;
  - matching explicável;
  - materiais com proveniência;
  - fila de revisão, digest e DLQ.
  O envio é sempre aprovado por uma pessoa (README.md:140-200). README declara 1720 testes passando em 21/05.
- **Decisão: REFERENCE_ONLY** (licença e regra do usuário).
- **Ideias que valem:** a separação em camadas e o modelo de segurança ("agents run inside bounded tool registries").

## Conclusão

Nenhum dos cinco entra como dependência de runtime. O núcleo é próprio, e o contrato de adapter fica pronto para encaixar um deles depois como processo externo, se algum dia compensar. O que se aproveita são as regras de segurança (todos convergem: CAPTCHA, login e declarações vão para a pessoa; preenchido não é enviado) e ideias de produto.

Achados que mudam o plano:
1. Todos os que automatizam o envio pausam no CAPTCHA, e o Instaply cita hCaptcha e reCAPTCHA Enterprise nos formulários. Parte relevante das candidaturas vai cair na fila humana.
2. Dois dos cinco trazem solver de CAPTCHA pago. Nosso código não terá esse caminho, nem desligado.
3. Os dois mais completos vêm com dados compartilhados ou conta hospedada por padrão. Nosso padrão é zero rede fora das fontes e dos provedores configurados.
