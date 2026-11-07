# Estado da integração

Todas as linhas de trabalho abaixo foram integradas à `main`, preservando as correções da versão 1.0.7.

| Linha de trabalho | Conteúdo | Integração |
|---|---|---|
| `feature/captcha-lab` | Desafios locais, extração HTML/JSON-LD, paginação e testes sintéticos | PR #1 |
| `feature/candidate-workflow` | Aderência ao perfil, preparação de entrevistas, sanitização e currículo/PDF | PR #2 |
| `docs/functional-audit` | Auditoria histórica, prompt e evidências reproduzíveis | PR #3 |
| `feature/linkedin-captcha` | Busca por sessão local do LinkedIn, captura pela extensão, contato com evidência e CAPTCHA em servidor próprio | PR #4 |

Os PRs #1 e #2 já eram ancestrais da main. Os PRs #3 e #4 foram mesclados durante esta integração, sem conflitos. As branches de funcionalidades podem ficar atrás da main após o merge; isso não indica trabalho pendente.

## Verificações

- Backend: build, testes unitários, testes em banco e fluxos de navegador/recuperação aprovados no [CI da integração](https://github.com/gabrielandre-math/job-orchestrator/actions/runs/42354516463).
- Frontend: build de produção e testes da extensão aprovados no mesmo CI.
- Execução local: 424 verificações .NET com PostgreSQL descartável, cinco testes do extrator, OCR nativo e testes HTTP de captura, contato, descarte e CAPTCHA.
- Serviço externo: o servidor de referência chamou Cloudflare Siteverify com as credenciais públicas oficiais de teste.

O CI de navegador usa o ATS controlado. Ele não equivale à homologação com uma conta real do LinkedIn ou com chaves de produção de um domínio privado. Esses dois ensaios ainda dependem da configuração e sessão do proprietário. As candidaturas dentro do LinkedIn permanecem assistidas; links explícitos para ATS suportado seguem para os adaptadores existentes.

Instaladores v1.0.7 são anteriores a esta integração. Para usar as alterações agora, execute o código atualizado da `main` conforme [o guia de configuração](REAL_LINKEDIN_AND_CAPTCHA.md).
