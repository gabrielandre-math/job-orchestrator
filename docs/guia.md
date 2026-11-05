# Guia de uso

O Job Orchestrator procura vagas de qualquer área, monta um currículo por vaga (perfeito para ATS, só com fatos do seu currículo) e cuida das candidaturas com você no controle. Tudo roda na sua máquina, em `http://127.0.0.1:7410`.

## Instalar

Rode `./job-orchestrator-instalador.run` (o arquivo que `scripts/package.sh` gera em `dist/`). A janela de instalação tem cinco passos:

1. **Boas-vindas:** o que o app faz e o que nunca faz.
2. **Sua máquina:** confere Docker, navegador, Python, modelo local, placa de vídeo, disco e chaveiro. O que faltar vem com o comando para copiar; aviso não impede.
3. **Como instalar:** atalho no menu, abrir junto com o computador (a busca roda sozinha com a janela fechada) e trazer o que você já tem do modo de desenvolvimento.
4. **Instalando:** arquivos, dados, banco, coletor, modelo local, atalho e serviço, um de cada vez, ao vivo.
5. **Pronto:** "Abrir o Job Orchestrator", e a mesma janela vira o app.

Depois: o app abre pelo menu (ou `job-orchestrator`); enquanto os serviços sobem, aparece a abertura animada. `job-orchestrator --status` diz se está no ar, `--stop` para, `--setup` abre o instalador de novo (para atualizar ou mudar as opções).

## Primeiro contato

Na primeira vez, o assistente abre sozinho e passa por cinco passos:

1. **Boas-vindas.** O que o app faz e o que nunca faz sem o seu ok.
2. **Sua IA.** Conecte a assinatura que você já paga (Claude Pro ou Max, ChatGPT Plus ou Pro), uma chave de API ou o modelo local. Pode conectar mais de uma: a pergunta difícil sobe para o modelo mais forte.
3. **Seu currículo.** PDF, DOCX, TXT ou o ZIP do export do LinkedIn. Os agentes leem, tiram os fatos e conferem cada um no texto original. Nome, e-mail e telefone ficam só na máquina.
4. **Sua busca.** O assistente propõe área, cargos-alvo, palavras-chave, locais, nota mínima e fontes. Cada fonte é testada ali mesmo; a que não responde fica desmarcada. Ajuste o que quiser.
5. **Pronto.** A primeira busca começa e você acompanha na Busca ao vivo.

"Agora não" fecha o assistente até recarregar a página. O cartão do Início reabre quando quiser.

## Automatizar tudo

O botão **Automatizar tudo** (no Início e na Campanha) abre um assistente que mostra o que já está pronto e guia o resto, um passo por vez:

1. **O que falta:** IA, perfil, busca, envios, busca sozinha, noite e e-mail, cada um com o estado e um atalho.
2. **Seu perfil:** países, modalidade, pisos salariais, início, inglês e os fatos deduzidos do currículo para confirmar.
3. **Envio:** você aprova cada envio, automático dentro de limites (validade, teto por dia, nota mínima, com o consentimento escrito) ou só buscar.
4. **Busca sozinha:** a campanha de tempos em tempos, o Explorador e os timers da noite.
5. **E-mail:** aprovação sua ou automático com consentimento, se houver conta conectada.
6. **Pronto:** o resumo do que ficou ligado e "Começar agora", que dispara a primeira rodada e abre a Busca ao vivo.

## O Explorador

Vai atrás de fonte nova sem você passar link, a cada 10 minutos. Lê os links de candidatura das vagas que chegam (o agregador aponta para o quadro da empresa), busca na Gupy e nas APIs públicas pelas suas palavras e cargos, acha repositórios do GitHub com vagas recentes, percorre listas de empresas e manda o coletor aos sites delas, respeitando o robots.txt de cada um. Entra só a fonte com vaga recente da sua área que ainda não chegava por outra, confirmada pelo modelo local, e as vagas dela chegam na mesma hora. Acompanhe em Fontes (as fontes testadas, com o motivo) e na Busca ao vivo.

## Mais vagas

- **Chaves gratuitas** (Fontes › Mais vagas com chave gratuita): Adzuna e Jooble trazem vagas do Brasil inteiro, de todas as áreas; o Brave Search deixa o Explorador procurar na web inteira pela API oficial, com consultas montadas do seu currículo; o token do GitHub sobe o limite de consultas. Cada chave tem um assistente que leva ao site, testa e guarda no chaveiro do sistema.
- **Alertas de vaga por e-mail** (Fontes › Alertas de vaga por e-mail): crie alertas no LinkedIn, Indeed, Catho, InfoJobs, Glassdoor e Vagas.com; com o seu e-mail conectado, o app lê os alertas e traz as vagas, sem abrir esses sites por robô.
- **Firecrawl** (opcional): `scripts/firecrawl.sh` sobe o serviço em 127.0.0.1:3002 e o app o encontra sozinho para páginas que só montam com JavaScript; sem ele, o navegador de leitura (Chromium do app, ou o Obscura na versão normal, se instalado em `~/.local/opt/obscura`) faz esse trabalho.

## O dia a dia

- **Início:** o que falta configurar, a busca em miniatura, os números e quem está trabalhando.
- **Assistente:** conversa com os seus modelos. Pergunta simples fica no modelo local; a complexa sobe de degrau. "Comparar modelos" mostra respostas de famílias diferentes lado a lado. O clipe anexa o currículo. Ações propostas (palavras, fontes, locais, nota) só acontecem quando você clica em Aplicar.
- **Busca ao vivo:** cada etapa da rodada em tempo real, fonte por fonte, a narração em frases, as notas e o que entrou na fila. Embaixo ficam os resumos da noite.
- **Atenção e Revisão:** o que só você resolve (CAPTCHA, login, MFA, pergunta nova, envio incerto) e o pacote exato de cada envio para aprovar.
- **Vagas e Candidaturas:** tudo que chegou, com a nota explicada, a logo da empresa, as palavras da sua busca na vaga e o currículo gerado ali mesmo. Vaga que vai pelo site original (LinkedIn, Gupy, site da empresa) ganha o **copiloto**: abre a vaga, baixa o currículo, copia as respostas aprovadas, escreve a carta e registra "Já enviei". Com a **extensão do copiloto** no Chromium (chrome://extensions, modo de desenvolvedor, "Carregar sem compactação", pasta `~/.local/opt/job-orchestrator/extension`), um clique na página da vaga preenche os campos vazios e anexa o currículo; você confere e envia. Ela não roda no LinkedIn e nunca clica em enviar.
- **E-mail:** para as vagas que pedem currículo por e-mail. Conecte com senha de app (Gmail, Outlook, iCloud, Zoho ou qualquer SMTP), revise o rascunho da IA com o anexo e aprove. O modo automático só liga com consentimento explícito.
- **Perfil:** os fatos com a origem de cada um, a confirmação e a importação guiada do export do LinkedIn.
- **Fontes:** de onde vêm as vagas. "Adicionar fonte" guia por plataforma (Gupy, Greenhouse, Lever, Ashby e outras), API pública, site de carreiras ou um link colado, e testa antes de salvar. As abas mostram a Varredura da internet e os conectores.
- **Provedores de IA:** contas, ordem de gasto, uso, modelo local com instalador e os sensores ao vivo da máquina (temperatura, uso e clocks de GPU e CPU).
- **Campanha:** critérios, cargos-alvo, palavras-chave, locais, limites e o modo (só descobrir, só preparar, revisão sua ou automático com autorização).
- **Configurações:** busca sozinha (intervalo da campanha e do Explorador), timers da noite com um botão, modo privado, retenção, exportar, revogar tudo e apagar tudo.

## Envio automático

Nada é enviado sem o seu ok até você criar uma **autorização** em Campanha › Automático. A autorização tem validade (1 a 30 dias), teto por dia, nota mínima e quais formulários podem enviar. Fora disso a vaga volta para a sua revisão. CAPTCHA, login, MFA, declaração legal e pergunta nova sempre param para você. Revogar devolve tudo para a revisão.

## A noite

Ligue "Buscar vagas sozinho" e "Ligar os timers" em Configurações (ou no Automatizar tudo). Os timers pedem rodadas às 23h05, 1h35 e 4h05, geram o resumo às 7h45 (com aviso na área de trabalho) e fazem o backup às 7h55; quem atende é o painel em 127.0.0.1:7410. Para o painel subir sozinho com o computador, instale os serviços com `scripts/install-units.sh --enable`. `scripts/night-sim.sh` simula uma noite em minutos.

## O que o app nunca faz

- Robô no LinkedIn ou em rede social; o caminho é o export oficial dos seus dados e o copiloto.
- Contornar robots.txt, CAPTCHA, login ou bloqueio (403); sem disfarce de navegador nem proxy.
- Inventar fato sobre você; frase sem fonte no currículo sai do texto.
- Escrever para e-mail que a vaga não publicou para candidatura.
