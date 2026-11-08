# Guia de uso

O Job Orchestrator procura vagas de qualquer área, monta um currículo por vaga (perfeito para ATS, só com fatos do seu currículo), prepara cada candidatura e abre a vaga já preenchida no seu navegador: você confere e clica em enviar. Depois, acompanha as respostas das empresas pelo seu e-mail. Tudo roda na sua máquina, em `http://127.0.0.1:7410`.

## Instalar

Rode `./job-orchestrator-instalador.run` (o arquivo que `scripts/package.sh` gera em `dist/`). A janela de instalação tem cinco passos:

1. **Boas-vindas:** o que o app faz e o que nunca faz.
2. **Sua máquina:** confere Docker, navegador, Python, modelo local, placa de vídeo, disco e chaveiro. O que faltar vem com o comando para copiar; aviso não impede.
3. **Como instalar:** atalho no menu, abrir junto com o computador (a busca roda sozinha com a janela fechada) e trazer o que você já tem do modo de desenvolvimento.
4. **Instalando:** arquivos, dados, banco, coletor, modelo local, atalho e serviço, um de cada vez, ao vivo.
5. **Pronto:** "Abrir o Job Orchestrator", e a mesma janela vira o app.

Depois: o app abre pelo menu (ou `job-orchestrator`); enquanto os serviços sobem, aparece a abertura animada. `job-orchestrator --status` diz se está no ar, `--stop` para, `--setup` abre o instalador de novo (para atualizar ou mudar as opções).

## Primeiro contato

Na primeira vez, o assistente de configuração abre sozinho e passa por cinco passos:

1. **Boas-vindas.** O que o app faz e o que nunca faz.
2. **Sua IA.** Conecte a assinatura que você já paga (Claude Pro ou Max, ChatGPT Plus ou Pro), uma chave de API ou o modelo local. Pode conectar mais de uma: a pergunta difícil sobe para o modelo mais forte.
3. **Seu currículo.** PDF, DOCX, TXT ou o ZIP do export do LinkedIn. Os agentes leem, tiram os fatos e conferem cada um no texto original. Nome, e-mail e telefone ficam só na máquina.
4. **Sua busca.** O assistente propõe área, cargos-alvo, palavras-chave, locais, nota mínima e fontes. Cada fonte é testada ali mesmo; a que não responde fica desmarcada. Ajuste o que quiser.
5. **Pronto.** A primeira busca começa. Daqui você vai para o Início, instala o copiloto do navegador ou confirma o perfil.

"Agora não" fecha o assistente até recarregar a página. O primeiro passo do Início reabre quando quiser.

## O menu

São cinco itens:

- **Início:** um próximo passo por vez, com um botão só. Embaixo, o seu caminho na ordem (configurar a busca, instalar o copiloto, responder as perguntas dos formulários, enviar as candidaturas prontas, conectar o e-mail das candidaturas), os resultados (enviadas hoje, no total, empresas que responderam e entrevistas) e os últimos movimentos. O número ao lado de Início é o que espera por você.
- **Vagas:** tudo o que as fontes trouxeram, em cartões ou em lista, com filtros, a nota explicada, a logo da empresa e o currículo gerado ali mesmo. Os números das vagas (funil, vagas por fonte, ferramentas mais pedidas) ficam em "Ver os números das vagas", e a Busca ao vivo mostra cada rodada em tempo real.
- **Candidaturas:** cada candidatura na sua etapa, num quadro (preparando, para enviar, enviada, entrevista, oferta, encerrada) ou numa lista com busca e ordem. Depois do envio, a IA lê o seu e-mail e move o cartão; você também pode arrastar.
- **Perfil:** os fatos com a origem de cada um, a confirmação e a importação guiada do export do LinkedIn.
- **Configurações:** abas Geral, O que buscar, IA, Fontes, E-mail e Diagnóstico.

## Enviar uma candidatura

1. No Início, **Envie a próxima vaga** mostra a candidatura pronta de maior nota. **Abrir preenchida** abre a vaga no seu navegador.
2. O **copiloto** (a extensão do navegador) preenche o formulário com o que o app preparou para aquela vaga e anexa o currículo dela. O campo que ele não preenche vai para a IA, que responde pelo seu perfil e nunca inventa; o que nem ela souber fica contornado em amarelo.
3. Você confere e clica em enviar no site. A página de confirmação marca a candidatura como enviada, a aba fecha e o Início mostra a próxima.
4. Se enviar por fora, **Já enviei** registra o envio.

Na página de cada candidatura fica o **kit de respostas**: o que escrever em cada campo do formulário, com botão de copiar, a carta de apresentação já escrita na língua da vaga e o currículo daquela vaga em PDF. Serve para o que o copiloto não preencher e para sites sem formulário reconhecível.

O app nunca clica em enviar. A caixa de privacidade ou termos de um formulário é marcada só no navegador em que o app prepara (que nunca envia); no site, quem marca é você.

### O copiloto

Na janela do app ele já vem ligado. No Chrome, no Edge ou no Brave: Configurações › Geral › Copiloto do navegador mostra os quatro passos (chrome://extensions, modo de desenvolvedor, "Carregar sem compactação" e a pasta da extensão). Quando sair uma versão nova, o Início avisa para recarregar a extensão em chrome://extensions. Sozinho, ele só preenche páginas que o app abriu nos últimos 15 minutos; numa vaga que você abriu por fora, clique no ícone dele. Não roda no LinkedIn e nunca clica em enviar.

## Perguntas dos formulários

Quando um formulário pergunta algo que só você sabe, a pergunta chega uma vez, agrupada pelo sentido: uma resposta vale para todo formulário que pergunta a mesma coisa, em qualquer língua. O Início mostra o passo **Responder perguntas dos formulários**, e um lembrete no canto abre a janela de respostas. As suas respostas ficam na máquina e nunca vão para um modelo de IA.

## Acompanhar as respostas

Conecte em Configurações › E-mail o mesmo endereço que vai nos formulários: é nele que chegam as confirmações e as respostas das empresas. Com ele conectado, a IA lê os e-mails das plataformas (Gupy, Greenhouse, Lever, Indeed e outras) e as respostas dos recrutadores, confirma o que você enviou e move cada candidatura para entrevista, oferta ou recusa. Se o endereço do perfil for outro, o Início avisa.

Em **Candidaturas**, uma candidatura enviada também tem **Preparar entrevista**: escolha a etapa, gere o guia e simule uma pergunta por vez. O feedback que você salvar entra no contexto da próxima etapa.

## Vagas por e-mail

Algumas vagas pedem o currículo por e-mail. Em Configurações › E-mail, conecte a conta com senha de app (Gmail, Outlook, iCloud, Zoho ou qualquer SMTP). A IA escreve o e-mail a partir dos fatos do seu currículo, com o currículo da vaga anexado, e ele sai pela sua conta quando você aprova. O modo automático de e-mail só liga com consentimento explícito e tem teto por dia próprio; **Revogar tudo**, em Configurações › Geral, desliga.

## O Explorador

Vai atrás de fonte nova sem você passar link, a cada 10 minutos. Lê os links de candidatura das vagas que chegam (o agregador aponta para o quadro da empresa), busca na Gupy e nas APIs públicas pelas suas palavras e cargos, acha repositórios do GitHub com vagas recentes, percorre listas de empresas e manda o coletor aos sites delas, respeitando o robots.txt de cada um. Entra só a fonte com vaga recente da sua área que ainda não chegava por outra, confirmada pelo modelo local, e as vagas dela chegam na mesma hora. Acompanhe em Configurações › Fontes e na Busca ao vivo.

## Mais vagas

Em Configurações › Fontes › **Mais formas de achar vagas**:

- **Chaves gratuitas:** Adzuna e Jooble trazem vagas do Brasil inteiro, de todas as áreas; o Brave Search deixa o Explorador procurar na web inteira pela API oficial, com consultas montadas do seu currículo; o token do GitHub sobe o limite de consultas. Cada chave tem um assistente que leva ao site, testa e guarda no chaveiro do sistema.
- **Alertas de vaga por e-mail:** crie alertas no LinkedIn, Indeed, Catho, InfoJobs, Glassdoor e Vagas.com; com o seu e-mail conectado, o app lê os alertas e traz as vagas, sem abrir esses sites por robô.
- **Vagas do LinkedIn:** salve a vaga pelo link ou, na página que você abriu, clique no ícone da extensão e em **Ler esta página**. O app nunca abre o LinkedIn por robô.
- **Firecrawl** (opcional): `scripts/firecrawl.sh` sobe o serviço em 127.0.0.1:3002 e o app o encontra sozinho para páginas que só montam com JavaScript; sem ele, o navegador de leitura (Chromium do app, ou o Obscura na versão normal, se instalado em `~/.local/opt/obscura`) faz esse trabalho.

## Configurações

- **Geral:** busca sozinha (intervalo da campanha e do Explorador), timers da noite com um botão, modo privado, retenção, exportar, revogar tudo, o conector MCP, o copiloto do navegador e apagar tudo.
- **O que buscar:** cargos, palavras-chave, locais e a nota mínima para preparar a candidatura, com "Buscar agora".
- **IA:** contas, ordem de gasto, uso, modelo local com instalador e os sensores ao vivo da máquina.
- **Fontes:** de onde vêm as vagas. "Adicionar fonte" guia por plataforma (Gupy, Greenhouse, Lever, Ashby e outras), API pública, site de carreiras ou um link colado, e testa antes de salvar.
- **E-mail:** a conta conectada, os e-mails para vagas que pedem currículo por e-mail e o modo de envio deles.
- **Diagnóstico:** o rastro local de 14 dias, com filtros.

## A noite

Ligue "Buscar vagas sozinho" e "Ligar os timers" em Configurações › Geral. Os timers pedem rodadas às 23h05, 1h35 e 4h05, geram o resumo às 7h45 (com aviso na área de trabalho) e fazem o backup às 7h55; quem atende é o painel em 127.0.0.1:7410. Para o painel subir sozinho com o computador, instale os serviços com `scripts/install-units.sh --enable`. `scripts/night-sim.sh` simula uma noite em minutos.

## O que o app nunca faz

- Clicar em enviar num formulário: toda candidatura sai do seu navegador, depois que você lê.
- Candidatar pelo LinkedIn por robô; o caminho é o export oficial dos seus dados, o link da vaga e o copiloto.
- Contornar robots.txt, CAPTCHA, login ou bloqueio (403); sem disfarce de navegador nem proxy.
- Inventar fato sobre você; frase sem fonte no currículo sai do texto.
- Escrever para e-mail que a vaga não publicou para candidatura.
