# Modelo de ameaças

Data: 08/11/2026 (versão 1.3.0, em que o app só prepara e quem envia é a pessoa). Cada mitigação tem um teste correspondente em `backend/tests`.

| # | Ameaça | Vetor | Mitigação | Teste |
|---|---|---|---|---|
| 1 | Prompt injection | descrição da vaga, HTML, e-mail, resposta de ferramenta | conteúdo delimitado e marcado como não confiável; IA sem ferramentas nem MCP; saída só em JSON validado; saída nunca vira configuração | cenário 9 do Fake ATS + fixtures pedindo segredo, troca de modo ou comando |
| 2 | Shell via agente | modelo ou plugin tentando executar comando | `--tools ""` no Claude, `--sandbox read-only` no Codex, plugins sem shell, cwd isolado | fixture pedindo execução; auditoria de processos filhos |
| 3 | Segredo vazando | log, artefato, prompt, captura | segredos só no keyring; enricher de redação; capturas mascaradas; PII por placeholder | segredo-canário plantado e procurado em logs, artefatos e prompts |
| 4 | Envio sem a pessoa | bug, modelo, plugin, configuração | o app não tem caminho de envio de formulário: prepara num navegador próprio que nunca clica em enviar, e quem envia é a pessoa, no navegador dela, com o copiloto; e-mail só sai com a aprovação dela ou com o automático que ela consentiu, dentro do teto do dia | os E2E conferem que nenhum preparo chega ao endpoint de envio do Fake ATS (contador zero) |
| 5 | Candidatura duplicada | retry, crash, dois workers, reboot | uma candidatura por vaga (UNIQUE); fila com lease e `FOR UPDATE SKIP LOCKED`; a confirmação do copiloto marca a candidatura como enviada uma vez só | testes da fila e do copiloto contra o banco |
| 6 | Declaração falsa | autorização de trabalho, fato, proficiência | fatos verificados com evidência; checagem de proveniência; categoria sensível só com resposta permanente aprovada | fato plantado; pergunta de autorização sem fato confirmado vira `NEEDS_HUMAN` |
| 7 | Vaga falsa ou golpe | pagamento, cripto, contato só por mensageiro, documento cedo, domínio que não bate | Sentinela bloqueia e manda para revisão | fixtures de golpe |
| 8 | SSRF | link da vaga para IP interno, `file://`, redirecionamento | allowlist de domínio, bloqueio de IP privado e de esquema, downloads desligados | URLs maliciosas no Fake ATS |
| 9 | Dependência maliciosa | NuGet ou npm | poucas dependências, versões fixadas, lockfiles, SBOM básico, revisão antes de adicionar | inventário no `jo doctor` |
| 10 | Executável de terceiros | plugin externo, clones em `references/` | nada de `references/` roda; plugin só por manifesto, com ambiente limpo, `HOME` próprio e timeout | plugin fake tentando ler `~/.aws` e `~/.ssh` |
| 11 | Painel exposto | bind em 0.0.0.0 | bind em 127.0.0.1; token por instalação no bookmarklet; CORS fechado; acesso remoto só por Tailscale com opt-in | teste de bind e de origem |
| 12 | Dado indo para IA em nuvem | perfil completo, e-mail bruto | minimização por tarefa; e-mail só local; modo privado desliga a nuvem | auditoria do payload de cada `ModelInvocation` |
| 13 | Cobrança inesperada | `ANTHROPIC_API_KEY`, `--bare`, Bedrock ou Vertex no ambiente | ambiente filho limpo; `--bare` proibido; `jo doctor` alerta se achar essas variáveis | ambiente com chave falsa: o provedor recusa rodar |
| 14 | Credencial de empresa | perfil `default` do `~/.aws` (Boatlux, DR-Meddie) | nenhum componente usa AWS; plugins com `AWS_CONFIG_FILE` e `AWS_SHARED_CREDENTIALS_FILE` em `/dev/null` | plugin fake chamando a cadeia padrão do boto3 |
| 15 | PC sem recurso | RAM ou VRAM à noite | `jobs.slice` com teto; Ollama descarrega modelo ocioso; 1 navegador de preparo | noite simulada medindo o slice |
| 16 | Conta bloqueada ou violação de termos | automação em site que proíbe, volume alto | tiers de fonte; copiloto para sites proibidos; limites e espaçamento; sem disfarce de automação | lint de configuração recusa fonte proibida |
| 17 | Perda de dados | crash, disco, erro humano | Postgres com volume, `pg_dump` diário, restic, `jo restore --verify` | restauração verificada no smoke test |
