# LinkedIn com sessão local e CAPTCHA em servidor próprio

Esta versão acrescenta busca real pelo navegador conectado ao LinkedIn, captura da aba por extensão e um fluxo de teste de CAPTCHA contra servidor configurável. Abra **Fontes → Testar LinkedIn e CAPTCHA**, ou `/testes`.

## Antes de testar

Use o código atualizado da branch `main`. Instaladores da release 1.0.7 não contêm estas mudanças. Para executar pelo código, siga o modo de desenvolvimento do README: API e Worker precisam estar ativos. O frontend compilado é servido pela API; um navegador Chrome/Chromium/Edge/Brave instalado é necessário. O login requer uma sessão gráfica na máquina em que a API roda.

Na cópia local do repositório, com .NET 10, Node e Docker instalados:

```sh
git fetch origin main
git switch main
git pull --ff-only origin main
npm ci --prefix web
npm run build --prefix web
scripts/dev.sh
```

Abra `http://127.0.0.1:7410/testes`. Se uma versão instalada já ocupa a porta 7410, pare-a antes para não testar por engano a versão anterior. No Omarchy com os serviços deste projeto: `systemctl --user stop job-orchestrator-api.service job-orchestrator-worker.service`. O modo de desenvolvimento usa dados separados em `.dev/`; substitua o perfil sintético pelo seu e confirme-o antes de candidaturas reais.

Se a API não estiver na porta padrão 7410, configure `LinkedIn:ApiBase` no Worker para `http://127.0.0.1:PORTA`. Esse endereço é somente loopback: o Worker pede a busca à API, que é a única dona do perfil do navegador. Não abra o mesmo diretório de perfil em outro Chromium.

## Buscar vagas reais do LinkedIn

1. No app, configure seu perfil verdadeiro, confirme os fatos e ajuste campanha, locais, tecnologias, nota mínima e modo de candidatura.
2. Em `/testes`, ative **Buscar vagas pela minha sessão do LinkedIn** e salve os limites. A configuração inicial lê até dez vagas, abre até cinco descrições por termo, filtra remotas no Brasil e usa intervalo de sessenta minutos. A busca pede anúncios das últimas 24 horas, ordenados pelos mais recentes.
3. Clique em **Abrir LinkedIn para login**. Faça login diretamente no site na janela separada. O app não recebe sua senha; o navegador guarda a sessão localmente. Conclua MFA e verificações nessa janela e clique em **Conferir sessão**.
4. Informe um termo, por exemplo `.NET backend`, e clique em **Adicionar esta busca à campanha**. A fonte adicionada é `linkedin-session:.NET backend`. Repita para outros termos se necessário.
5. Clique em **Buscar agora pela campanha** e acompanhe em **Busca ao vivo**. As vagas entram pelo mesmo normalizador, avaliação, deduplicação e fila das outras fontes. Configurar somente a sessão não inicia uma rodada: a fonte precisa estar na campanha e o Worker precisa estar rodando.
6. Para rodar sem sua presença, mantenha a busca recorrente da campanha ligada e a máquina acordada. O horário da campanha e o intervalo do Explorador continuam aplicáveis. A sessão pode ser reutilizada após reiniciar a API, enquanto o site aceitar seus cookies. Verifique o primeiro ciclo antes de deixar rodando.

O conector é experimental e depende do layout do LinkedIn. As fontes experimentais precisam estar habilitadas em Fontes. Não é a API oficial nem uma autorização concedida pelo LinkedIn: seu uso pode ser limitado pelo site, cujos termos estão vinculados no registro. Não há troca de IP, ocultação do navegador, busca de senhas ou contorno de bloqueios. Se aparecer login, checkpoint, verificação ou 429, a consulta pausa e informa o motivo. Resultado vazio reconhecido é diferente de página cujo layout não foi entendido.

O intervalo por termo reaproveita o resultado em memória. Entre novas consultas há no mínimo um minuto; o limite diário padrão é de doze pesquisas, contado em UTC e persistido para não ser zerado pelo reinício. Cartões sem descrição ficam identificáveis como incompletos; não são preenchidos por invenção. Abre-se apenas o número de detalhes configurado, sem promessa de cobertura de todos os resultados da plataforma.

### O que ocorre depois da coleta

- O texto e a elegibilidade passam pelas regras da campanha e do perfil. Vagas incompatíveis podem ser rejeitadas com motivo; coleta não equivale a shortlist.
- Se a publicação fornece explicitamente um link HTTPS de Greenhouse, Lever, Ashby, SmartRecruiters ou Workable, esse link é usado como destino para os adaptadores existentes. A automação depende do formulário e das autorizações de envio.
- Se a candidatura ocorre dentro do LinkedIn, o aplicativo prepara o material e encaminha ao copiloto: **não há envio automático de Easy Apply nesta mudança**.
- Um e-mail só fica disponível para o módulo Mail quando o texto o publica para candidatura e há um único contato identificado. Contatos ambíguos, de suporte/privacidade e e-mails sem contexto não são escolhidos automaticamente. O trecho de origem pode ser conferido no detalhe da vaga.
- Quando há um ATS reconhecido, ele é o canal preferido; o contato permanece na evidência, sem liberar simultaneamente um segundo envio automático por e-mail para a mesma vaga.

### Capturar ou completar uma vaga aberta

A extensão funciona no navegador em que você já usa sua conta. Em `chrome://extensions`, ative o modo de desenvolvedor e carregue a pasta `extension` deste projeto (recarregue-a depois de atualizar).

Na página de resultados ou nos detalhes da vaga, clique no ícone da extensão → **Ler esta página** → revise a prévia → **Salvar selecionadas no app**. Expanda a descrição antes de capturar os detalhes. O extrator lê JSON-LD e o conteúdo do documento atual; não abre outras páginas, não lê mensagens ou contatos pessoais e não envia candidaturas. Cada captura tem limite de vinte vagas.

O ID numérico do LinkedIn normaliza URLs com slug, subdomínio e tracking. Capturar novamente atualiza a mesma vaga. Um cartão incompleto não apaga a descrição mais rica já salva. O favorito antigo de captura também usa o novo caminho para URLs reconhecidas do LinkedIn.

## CAPTCHA no seu servidor privado

O app mantém **a mesma página e contexto** entre abrir, preencher, obter resposta, conferir e enviar. Há três modos de resolução:

| Tipo | Comportamento |
|---|---|
| Imagem | OCR local via Tesseract, lendo os pixels da imagem exibida. Se não houver resposta reconhecida, você pode digitá-la na janela. Não usa o alfabeto do laboratório antigo. |
| Soma/subtração | Interpretação restrita de dois inteiros e `+`/`-`; não executa código da página. |
| Turnstile, reCAPTCHA v2, hCaptcha | O widget oficial gera o token. Em chaves reais, você conclui a verificação na janela quando exigida; o servidor valida a resposta no endpoint oficial. Chaves de teste permitem testar o caminho automatizado previsto pelo provedor. |

**Esta mudança não promete resolução automática universal de desafios de provedores.** A integração de tokens reais, a resolução humana com retomada e o OCR local têm capacidades diferentes. Confirmação só aparece quando o servidor devolve recibo após a validação. “Resposta disponível” ainda não significa “CAPTCHA aceito”.

### Servidor de referência que você pode subir

O projeto `backend/src/Tools/JobOrchestrator.FakeAts` ganhou uma rota independente `/controlled-test`. Os provedores externos chamam os servidores verdadeiros de validação. O nome histórico FakeAts se refere aos cenários antigos; nessa rota o modo de validação aparece explicitamente.

Exemplo local com imagem e OCR, na raiz do repositório:

```sh
JO_CONTROLLED_TESTS=1 JO_CONTROLLED_CAPTCHA=image FAKEATS_URL=http://127.0.0.1:5471 \
  dotnet run --project backend/src/Tools/JobOrchestrator.FakeAts
```

Instale Tesseract na máquina do aplicativo para OCR. No Omarchy/Arch, os pacotes são `tesseract` e `tesseract-data-eng`. O processo `tesseract` precisa estar no PATH da API. A imagem simples de referência é uma amostra de integração, não um mecanismo de proteção robusto.

Turnstile com **chaves reais suas**, em servidor publicado atrás de HTTPS:

```sh
export JO_CONTROLLED_TESTS=1
export JO_CONTROLLED_CAPTCHA=turnstile
export JO_CAPTCHA_SITEKEY='SUA_SITEKEY'
export JO_CAPTCHA_SECRET='SEU_SECRET'
export JO_CAPTCHA_HOSTNAME='captcha.seudominio.com'
export FAKEATS_URL='http://0.0.0.0:5471'
dotnet run --project backend/src/Tools/JobOrchestrator.FakeAts
```

Use um domínio cadastrado no provedor, HTTPS no proxy e certificado confiável na máquina do app. O secret fica **somente no servidor privado**. Não o coloque na extensão ou na configuração do app. O servidor verifica `success`, hostname e, para Turnstile, a action `job_orchestrator_test`. Para hCaptcha use `JO_CONTROLLED_CAPTCHA=hcaptcha`; para reCAPTCHA v2, `recaptcha-v2`. Não há suporte a pontuação reCAPTCHA v3 nesta versão.

Para chaves oficiais de teste, use explicitamente `JO_CAPTCHA_TEST_KEYS=1`. Esse modo aparece na página e não é ativado automaticamente quando uma chave real falha. hCaptcha tem requisitos próprios de domínio para uso local; use um hostname de desenvolvimento adequado em vez de pressupor que `127.0.0.1` será aceito.

No app:

1. Cadastre a origem exata, como `https://captcha.seudominio.com`, e ative os testes.
2. Deixe **Executar sem janela** desligado para desafios que exigem sua interação.
3. Informe `https://captcha.seudominio.com/controlled-test`, confirme que é seu servidor e abra a sessão.
4. Resolva na janela quando necessário, volte ao painel e atualize o estado.
5. Clique em **Enviar teste ao meu servidor**. Confira `Confirmed` e o recibo. `Rejected` é resposta recusada; `Unknown` exige conferência no servidor e não provoca reenvio cego.
6. Feche a sessão para descartar contexto e cookies. Sessões abertas expiram; a configuração admite uma sessão de teste por vez.

HTTP é aceito em loopback e IP de rede local. Para domínio público, use HTTPS. Não há opção de ignorar certificado inválido.

### Adaptar seu próprio formulário

O formulário pode ser outro servidor, desde que publique este contrato:

```html
<meta name="job-orchestrator-controlled" content="v1">
<form method="post" action="/receber" data-jo-test-form data-jo-provider="turnstile">
  <input name="name" data-jo-test-field required>
  <input name="email" type="email" data-jo-test-field required>
  <!-- widget oficial e suas configurações -->
  <button type="submit">Enviar</button>
</form>
```

Uma única tag `form[data-jo-test-form]`, método POST para a mesma origem. Campos de teste preenchíveis recebem `data-jo-test-field`. O painel usa identidade sintética por padrão. O provedor é `turnstile`, `recaptcha-v2`, `hcaptcha`, `image` ou `math`.

Em imagem, use `img[data-jo-captcha-image]` e `input[data-jo-captcha-answer]`. Opcionalmente informe `data-jo-captcha-alphabet="0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"` na imagem; o padrão é numérico. Em aritmética, use um elemento `[data-jo-captcha-expression]` e o mesmo campo de resposta.

Seu backend deve validar o desafio **antes de aceitar** o envio e implementar idempotência/recuperação. Após sucesso, retorne HTML com `<p data-jo-receipt="SEU-RECIBO-UNICO">Recebido</p>`. Para rejeição, retorne `[data-jo-error]`. O app não consegue provar a validação interna de um servidor que apenas imprime um recibo: esse é o contrato que você implementa. O servidor de referência efetivamente valida e devolve o mesmo recibo para repetição da mesma intenção.

## Dados e descarte

A sessão do LinkedIn é guardada no diretório de dados local `linkedin-browser`, separado do perfil pessoal do Chrome. Ela precisa persistir para permitir busca recorrente. Use **Desconectar e apagar a sessão local** para removê-la e desligar a busca.

CAPTCHA controlado mantém tokens/cookies em memória; não os salva no histórico do app. Fechar ou expirar descarta o contexto. As vagas e evidências ficam no banco local. **Apagar tudo**, em Configurações, também fecha os navegadores e remove a autenticação local do LinkedIn. Os scripts de verificação usam dados sintéticos e não enviam candidaturas ou e-mails a terceiros. Na sua campanha real, os envios obedecem ao modo e às autorizações configurados.

## Verificação desta implementação

```sh
npm ci --prefix tests/extension
npm test --prefix tests/extension
JO_DOTNET=dotnet bash scripts/test-controlled.sh --external
```

O segundo comando de integração é para Linux x64, exige porta 5463 livre e prepara PostgreSQL descartável com download verificado. Usa identidade sintética. `--external` chama a validação real do Turnstile com credenciais oficiais públicas de teste; não é prova de resolver um desafio de produção. O banco e processos temporários são removidos no encerramento.

Foram exercitados: extração DOM/JSON-LD, normalização, contatos, validação de domínio, atualização sem duplicação via HTTP e PostgreSQL, cobertura, políticas do servidor privado, rejeição/aceitação e idempotência do recibo, além de validação externa do Turnstile. A interação Chromium foi bloqueada pelo ambiente de desenvolvimento (`socket() failed: Operation not permitted`); nenhuma conta autenticada do LinkedIn foi disponibilizada aqui. Assim, **o navegador real, os seletores no layout da sua conta e suas chaves reais ainda precisam da validação que você fará**. Build e teste sintético não são apresentados como essa homologação.

Documentação dos provedores:

- https://developers.cloudflare.com/turnstile/troubleshooting/testing/
- https://developers.cloudflare.com/turnstile/get-started/server-side-validation/
- https://developers.google.com/recaptcha/docs/faq
- https://docs.hcaptcha.com/#integration-testing-test-keys
