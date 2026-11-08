# v1.2.4

A varredura completa do app: perguntas agrupadas pelo sentido, currículos que vendem com fatos, avisos das plataformas acompanhando as candidaturas e um rastro local para caçar defeitos.

- A janela de perguntas agrupa pelo sentido. "3 vagas pedem o seu aceite dos termos" vira uma linha com Aceito ou Não aceito, e uma resposta vale para todo formulário que pergunta a mesma coisa, em qualquer língua. Sim e não viram a opção de cada formulário.
- Perguntas abertas como "conte sua experiência" são respondidas pela IA a partir do seu currículo. Resposta vazia, "text", "Não" de banco de respostas ou texto na língua errada não sai mais: a pergunta chega para você com o rascunho.
- A janela não abre mais sozinha por cima da tela. Um lembrete no canto mostra quantas candidaturas as suas respostas destravam, e "Aprovar envio" voltou a funcionar.
- O currículo vende com fatos: resultado primeiro, as palavras da vaga, os anos contados pelas datas e tudo na língua da vaga. Rascunho que mistura línguas ou copia o resumo antigo volta para outro modelo com a lista do que corrigir.
- O resumo do currículo não some mais: a frase sem fato sai e o resto fica, o nome da empresa da vaga pode aparecer, e sem resumo aproveitável o app escreve um com os fatos conferidos. Sem travessão, sem rebaixar a senioridade e sem anos a menos.
- Os e-mails de status da Gupy, do Greenhouse, do Lever, do Indeed e de outras plataformas confirmam a candidatura que você enviou por elas e marcam entrevista, oferta e recusa. A empresa é encontrada pelo nome; na dúvida entre duas vagas, nada muda.
- Rastro: requisições, comandos, passos da fila, mudanças de estado, chamadas de IA e erros da tela num log local de 14 dias, com a tela Diagnóstico para filtrar. Nenhuma resposta, currículo ou corpo de e-mail entra nele.
- Busca mais firme: um id de vaga comprido demais ou uma fonte que devolve algo que não é JSON não derrubam mais a rodada. O salário é lido pelo contexto (benefício e faturamento da empresa não contam), e lugares do Brasil no título e no texto contam para a elegibilidade.
- Feeds RSS: o cabeçalho que faltava fazia firewalls como o do Rio Vagas recusarem a consulta. Títulos com cargo, setor, salário e bairro na mesma linha, como os do Rio Vagas, viram cargo, empresa, local e salário, e posts de notícia ficam de fora.
- Site que não existe mais encerra a candidatura; site fora do ar tenta de novo depois.
- Confirmar o perfil retoma as candidaturas que esperavam por ele. Uma chamada de IA cancelada não conta como falha do provedor.
- O assistente sabe quantas candidaturas esperam aprovação, resposta ou envio seu.
- Sair do Assistente enquanto um modelo escreve não perde mais a conversa: ao voltar, a pergunta e a resposta estão lá.
- Com o grupo "Avançado" aberto, o menu rola por dentro; antes a página inteira rolava e sobrava uma faixa escura embaixo.
- O histórico diz quando foi o e-mail, e não você, que marcou a etapa.
- "Apagar tudo" agora leva também as suas respostas aos formulários e o rastro de diagnóstico; antes, as respostas ficavam num arquivo e voltavam nos próximos formulários.
- /instalar com o app instalado abre o painel sem erro.
