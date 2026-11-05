# Guia de uso

O Job Orchestrator procura vagas de qualquer área, monta um currículo por vaga (perfeito para ATS, só com fatos do seu currículo) e cuida das candidaturas com você no controle. Tudo roda na sua máquina, em `http://127.0.0.1:7410`.

## Primeiro contato

Na primeira vez, o assistente abre sozinho e passa por cinco passos:

1. **Boas-vindas.** O que o app faz e o que nunca faz sem o seu ok.
2. **Sua IA.** Conecte a assinatura que você já paga (Claude Pro ou Max, ChatGPT Plus ou Pro), uma chave de API ou o modelo local. Pode conectar mais de uma: a pergunta difícil sobe para o modelo mais forte.
3. **Seu currículo.** PDF, DOCX, TXT ou o ZIP do export do LinkedIn. Os agentes leem, tiram os fatos e conferem cada um no texto original. Nome, e-mail e telefone ficam só na máquina.
4. **Sua busca.** O assistente propõe área, cargos-alvo, palavras-chave, locais, nota mínima e fontes. Cada fonte é testada ali mesmo; a que não responde fica desmarcada. Ajuste o que quiser.
5. **Pronto.** A primeira busca começa e você acompanha na Busca ao vivo.

"Agora não" fecha o assistente até recarregar a página. O cartão do Início reabre quando quiser.

## O dia a dia

- **Início:** o que falta configurar, a busca em miniatura, os números e quem está trabalhando.
- **Assistente:** conversa com os seus modelos. Pergunta simples fica no modelo local; a complexa sobe de degrau. "Comparar modelos" mostra respostas de famílias diferentes lado a lado. O clipe anexa o currículo. Ações propostas (palavras, fontes, locais, nota) só acontecem quando você clica em Aplicar.
- **Busca ao vivo:** cada etapa da rodada em tempo real, fonte por fonte, a narração em frases, as notas e o que entrou na fila. Embaixo ficam os resumos da noite.
- **Atenção e Revisão:** o que só você resolve (CAPTCHA, login, MFA, pergunta nova, envio incerto) e o pacote exato de cada envio para aprovar.
- **Vagas e Candidaturas:** tudo que chegou, com a nota explicada, a logo da empresa, as palavras da sua busca na vaga e o currículo gerado ali mesmo. Vaga que vai pelo site original (LinkedIn, Gupy, site da empresa) ganha o **copiloto**: abre a vaga, baixa o currículo, copia as respostas aprovadas, escreve a carta e registra "Já enviei".
- **E-mail:** para as vagas que pedem currículo por e-mail. Conecte com senha de app (Gmail, Outlook, iCloud, Zoho ou qualquer SMTP), revise o rascunho da IA com o anexo e aprove. O modo automático só liga com consentimento explícito.
- **Perfil:** os fatos com a origem de cada um, a confirmação e a importação guiada do export do LinkedIn.
- **Fontes:** de onde vêm as vagas. "Adicionar fonte" guia por plataforma (Gupy, Greenhouse, Lever, Ashby e outras), API pública, site de carreiras ou um link colado, e testa antes de salvar. As abas mostram a Varredura da internet e os conectores.
- **Provedores de IA:** contas, ordem de gasto, uso, modelo local com instalador e os sensores ao vivo da máquina (temperatura, uso e clocks de GPU e CPU).
- **Campanha:** critérios, cargos-alvo, palavras-chave, locais, limites e o modo (só descobrir, só preparar, revisão sua ou automático com autorização).
- **Configurações:** busca da noite, modo privado, retenção, exportar, revogar tudo e apagar tudo.

## Envio automático

Nada é enviado sem o seu ok até você criar uma **autorização** em Campanha › Automático. A autorização tem validade (1 a 30 dias), teto por dia, nota mínima e quais formulários podem enviar. Fora disso a vaga volta para a sua revisão. CAPTCHA, login, MFA, declaração legal e pergunta nova sempre param para você. Revogar devolve tudo para a revisão.

## A noite

Ligue "Rodar a campanha durante a noite" em Configurações e instale os serviços de usuário com `scripts/install-units.sh --enable`. Os timers pedem rodadas às 23h05, 1h35 e 4h05, geram o resumo às 7h45 (com aviso na área de trabalho) e fazem o backup às 7h55. `scripts/night-sim.sh` simula uma noite em minutos.

## O que o app nunca faz

- Robô no LinkedIn ou em rede social; o caminho é o export oficial dos seus dados e o copiloto.
- Contornar robots.txt, CAPTCHA, login ou bloqueio (403); sem disfarce de navegador nem proxy.
- Inventar fato sobre você; frase sem fonte no currículo sai do texto.
- Escrever para e-mail que a vaga não publicou para candidatura.
