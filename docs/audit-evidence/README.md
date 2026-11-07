# Evidências da auditoria

Base auditada: `6e1d04a978b06b747cf4dffcbf4a85543aed62bc`.

`validation.json` separa testes reais, fixtures e limitações. `registry.json` contém metadados do registro antes da sondagem. Os dois JSONs de sondagem guardam somente resultados agregados e presença de campos; amostras individuais foram removidas. `mail-and-wipe.json` contém apenas destinatários e mensagens sintéticos.

## Sete sondas C#

`AuditBehaviorTests.cs.txt` foi executado temporariamente como `backend/tests/JobOrchestrator.Tests/Unit/AuditBehaviorTests.cs`, com filtro `FullyQualifiedName~AuditBehaviorTests`: 7 passaram. Foi retirado da suíte normal para não tornar esses defeitos comportamento desejado. Para reproduzir, copie ao diretório acima, execute `dotnet test backend/tests/JobOrchestrator.Tests --filter 'FullyQualifiedName~AuditBehaviorTests'` e remova a cópia. Após as correções, transforme os casos em testes que esperam o comportamento correto.

## API, SMTP e exclusão

`reproduce-http.sh` prepara PostgreSQL 17.11 descartável, compilação e `reproduce-http.py`. Requer Linux x64, .NET 10, Python 3, openssl, curl, unzip, tar e porta local 5463 livre. Use uma cópia de trabalho de testes, pois os JSONs de evidência são atualizados. Opcionalmente configure `JO_AUDIT_DOTNET` com caminho absoluto do SDK. Execute da raiz do repositório:

```sh
bash docs/audit-evidence/reproduce-http.sh
```

O script baixa PostgreSQL com hash fixado, usa caminhos temporários exclusivos, um modelo HTTP simulado, perfil sintético, chaveiro simulado limitado a chaves Mail e SMTP TLS local. Faz também quatro sondagens de fontes públicas, sem envio a terceiros. Nenhuma credencial real é necessária. O processo da API é deliberadamente encerrado após aceitação SMTP para reproduzir o defeito. O banco temporário é removido ao sair.

As asserções esperam os **defeitos observados**: envio sem anexo, mensagem repetida e HTTP 500 ao apagar. Após corrigir o produto, essas asserções precisam ser invertidas. O teste revoga as configurações durante a sequência, mas não prova por si só o cenário de aprovação automática anterior à revogação: esse achado foi rastreado no código.

O teste SMTP verifica o transporte e a recuperação com rascunhos sintéticos inseridos no banco; não verifica redação real por IA, geração real de PDF, entrega em Gmail/Outlook nem classificação por IMAP.

## Laboratórios

Os comandos completos estão em [labs.md](../labs.md). Com Fake ATS e `FAKEATS_CAPTCHA_LAB=1`, execute unittest com `PYTHONPATH=scripts` e `FAKEATS_LAB_TEST_PORT` correspondente. `labs-validation.log` registra 11 testes aprovados, inclusive HTTP. Eles não fazem chamadas ao LinkedIn real ou a provedores comerciais de CAPTCHA.
