Correções no preenchimento e na confirmação de candidaturas, mantendo a execução e os dados locais.

- Perguntas obrigatórias sem resposta chegam ao Respondente, com suas opções e tipos. Antes, o adaptador descartava essa informação.
- Texto de sucesso que já estava na página ou permanecia oculto não confirma um novo envio. A confirmação deve aparecer visivelmente depois da ação; recibos novos são preservados.
- O formulário genérico só aciona um botão dentro do formulário selecionado.
- A conferência de opções exige que a resposta escolhida esteja marcada no DOM atual. Uma outra opção já marcada não conta como sucesso de preenchimento.
- Campos salariais com máscara aceitam o valor numérico preservado. Essa tolerância não aceita apagar palavras de respostas em outros campos só porque seus dígitos coincidem.
- Uma página pública do LinkedIn sem cookie de autenticação válido não é anunciada como sessão conectada.
- `job-orchestrator --diagnose` lê a instalação que está rodando e agrupa os bloqueios da fila, sem incluir currículo, nomes, contatos ou credenciais. O comando não tenta nem anuncia um envio externo.
- A escalada de perguntas entre modelos preserva as alterações concorrentes da main e verifica novamente as preferências de privacidade e contas no momento da chamada. Modo somente local e conta recém-desligada impedem chamadas externas mesmo com status antigo em cache.
- No servidor privado de CAPTCHA, o desafio e o recibo ficam ligados à sessão e à identidade enviada. Sessão errada, resposta inválida, expiração e reutilização com outro conteúdo são recusadas. O backend continua chamando o verificador oficial nos modos de provedor.
- Pacotes incluem versão, arquitetura e SHA em `build-info.json`; `/api/health` do app informa a revisão de origem. Publicação usa os scripts existentes de empacotamento e assinatura ad hoc, depois do CI da main, com SHA-256 dos artefatos.
- O empacotamento Linux/Windows para imediatamente se a compilação ou a cópia dos recursos falhar.

**Limites:** esta versão não é um robô universal de candidatura. Easy Apply e desafios externos de login/CAPTCHA continuam assistidos. Chaves oficiais de teste, respostas sintéticas e o ATS controlado não comprovam LinkedIn autenticado, envio a recrutadores ou resolução automática de CAPTCHA de produção. A homologação com sessão própria e domínio/chaves reais do usuário permanece necessária. Não foram enviadas candidaturas nem mensagens a terceiros durante a auditoria.
