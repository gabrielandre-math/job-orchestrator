# v1.2.1

O primeiro contato agora pergunta os dados de contato e as preferências para formulários, em dois passos com as ilustrações do app. A configuração também pode ser aberta pelo Perfil.

Quando uma candidatura para por uma pergunta obrigatória sem resposta, o app abre um modal com as perguntas e a quantidade de candidaturas afetadas. Você responde uma vez, salva localmente e o preparo das vagas correspondentes é retomado. É possível adiar e abrir de novo pela barra lateral ou pelo detalhe da candidatura. Login, CAPTCHA e envio incerto não são retomados por esse fluxo. O modo da campanha, a autorização e os limites de envio continuam sendo conferidos no preparo normal.

Data de nascimento é opcional no onboarding. Quando fornecida, é validada no servidor e reutilizada em português, inglês e espanhol, inclusive em campos de data e formatos explícitos DD/MM/AAAA e MM/DD/YYYY. Ela não é inferida por IA, incluída nos prompts nem impressa no currículo. As respostas locais são gravadas atomicamente; em Unix, o arquivo fica acessível apenas ao próprio usuário.

O link do PDF passa a se chamar “Currículo preparado”: ter um currículo não é prova de envio.

Validação adicionada: regressões de formatos de data e rejeição de respostas de IA, API + PostgreSQL com retomada seletiva e idempotente, formulários controlados no Chromium e modal do build Angular em desktop e celular. Nenhuma candidatura ou mensagem é enviada a terceiros nesses testes. LinkedIn com sessão pessoal, CAPTCHA de produção e autonomia noturna completa continuam dependendo dos testes reais descritos na documentação; esta versão não os declara comprovados.
