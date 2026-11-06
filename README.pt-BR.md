<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset=".github/assets/banner-pt-dark.svg">
    <img alt="Job Orchestrator. Encontre vagas usando a sua GPU. Busca de vagas self-hosted, com modelo de IA local." src=".github/assets/banner-pt-light.svg" width="100%">
  </picture>
</p>

<p align="center">
  <a href="#download"><b>Download</b></a> &nbsp;|&nbsp;
  <a href="#como-funciona">Como funciona</a> &nbsp;|&nbsp;
  <a href="#self-hosted-de-verdade">Self-hosted</a> &nbsp;|&nbsp;
  <a href="#compilar-a-partir-do-código">Compilar</a> &nbsp;|&nbsp;
  <a href="README.md">English</a>
</p>

<p align="center">
  <a href="https://github.com/gabrielandre-math/job-orchestrator/releases/latest"><img alt="Última versão" src="https://img.shields.io/github/v/release/gabrielandre-math/job-orchestrator?style=flat-square&label=vers%C3%A3o&labelColor=141414&color=ff90ac"></a>
  <img alt="Linux, macOS e Windows" src="https://img.shields.io/badge/roda_em-Linux_%7C_macOS_%7C_Windows-ff90ac?style=flat-square&labelColor=141414">
  <img alt="IA local com Ollama" src="https://img.shields.io/badge/IA-Ollama_na_sua_GPU-ff90ac?style=flat-square&labelColor=141414&logo=ollama&logoColor=white">
  <a href="LICENSE.md"><img alt="Licença: PolyForm Noncommercial 1.0.0" src="https://img.shields.io/badge/licen%C3%A7a-PolyForm_Noncommercial-ff90ac?style=flat-square&labelColor=141414"></a>
</p>

O Job Orchestrator faz a sua busca de emprego no seu próprio computador. Ele lê quadros de vagas e páginas de carreira das empresas, separa as vagas em que você pode mesmo se candidatar, dá a cada uma uma nota contra o seu currículo com um modelo de linguagem rodando na sua GPU, escreve um currículo para cada vaga só com fatos dos seus documentos e preenche a candidatura quando você aprova.

Não existe conta, servidor nem assinatura do Job Orchestrator. A instalação é a de qualquer app de desktop, e tudo fica na sua pasta de usuário.

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset=".github/assets/screen-jobs-dark.webp">
    <img alt="A tela de Vagas: vagas abertas, o funil de encontradas até a fila, vagas por fonte, distribuição das notas e as ferramentas mais pedidas." src=".github/assets/screen-jobs-light.webp" width="100%">
  </picture>
</p>

A interface é em português do Brasil. Currículos e respostas de formulário saem em português para vagas no Brasil e em inglês para as demais.

## Self-hosted de verdade

- **Tudo roda na sua máquina.** O app, o banco (um PostgreSQL próprio que o instalador baixa, com uns 60 MB, sem Docker), a automação do navegador, o coletor e o modelo de IA. O painel só atende em `127.0.0.1:7410`.
- **Os seus dados ficam na sua pasta de usuário.** Currículo, perfil, currículos gerados, histórico das candidaturas e dados dos e-mails ficam em `~/.local/share/job-orchestrator` no Linux, em `~/Library/Application Support/JobOrchestrator` no macOS e em `%LOCALAPPDATA%\JobOrchestrator` no Windows. Sai do computador apenas o que uma fonte precisa para responder e as candidaturas que você aprova.
- **Sem telemetria.** Nada informa uso para lugar nenhum.
- **O modelo local vem primeiro.** Ler currículo, dar nota às vagas e escrever textos fica com um modelo servido pelo [Ollama](https://ollama.com). Ele usa a sua GPU quando o Ollama tem suporte a ela (NVIDIA, AMD ou Apple Silicon) e o processador nos outros casos. A tela de Provedores instala o modelo para você.
- **A sua assinatura de IA é opcional.** Se você já usa o Claude Code ou o Codex CLI, o app pode chamá-los para os textos mais pesados, na ordem que você escolher e com uma reserva, para a cota do seu trabalho continuar intacta. Nenhuma chave de API paga é necessária.

## Como funciona

<table>
  <tr>
    <td width="112" align="center"><picture><source media="(prefers-color-scheme: dark)" srcset=".github/assets/step-radar-dark.svg"><img alt="" src=".github/assets/step-radar-light.svg" width="96"></picture></td>
    <td><b>1. Encontrar.</b> O Batedor consulta 52 fontes registradas pela API oficial ou pela página pública de cada uma: sites de carreira de empresas no Greenhouse, Lever, Ashby, SmartRecruiters, Workday, Workable e Teamtailor, plataformas brasileiras como Gupy, Sólides, InHire, Quickin e Pandapé, APIs de vagas como Himalayas, Remotive, Remote OK e Get on Board, serviços públicos de emprego da Suécia, da Noruega, da República Tcheca, da Letônia e dos Estados Unidos, as threads de contratação do Hacker News e do GitHub e os alertas de vaga que chegam ao seu e-mail. A cada 10 minutos, o Explorador procura fontes novas a partir do seu currículo e fica com as que trazem vagas inéditas.</td>
  </tr>
  <tr>
    <td align="center"><picture><source media="(prefers-color-scheme: dark)" srcset=".github/assets/step-chart-dark.svg"><img alt="" src=".github/assets/step-chart-light.svg" width="96"></picture></td>
    <td><b>2. Separar.</b> A mesma vaga em quadros diferentes vira uma só. Vagas restritas a um país onde você não pode trabalhar saem da fila. Cada vaga que sobra ganha uma nota de 0 a 100 com os motivos por escrito, e as que passam do seu corte seguem adiante. A nota mede a aderência à sua busca, não a chance de contratação.</td>
  </tr>
  <tr>
    <td align="center"><picture><source media="(prefers-color-scheme: dark)" srcset=".github/assets/step-writer-dark.svg"><img alt="" src=".github/assets/step-writer-light.svg" width="96"></picture></td>
    <td><b>3. Escrever.</b> O Redator monta um currículo para cada vaga apenas com fatos do seu currículo, da sua exportação do LinkedIn e do seu perfil. O Conferente compara cada linha com a origem, um Revisor de outra família de modelos lê de novo e o Inspetor ATS lê o PDF final do jeito que um sistema de recrutamento leria. A carta de apresentação é opcional e segue a língua da vaga.</td>
  </tr>
  <tr>
    <td align="center"><picture><source media="(prefers-color-scheme: dark)" srcset=".github/assets/step-guardian-dark.svg"><img alt="" src=".github/assets/step-guardian-light.svg" width="96"></picture></td>
    <td><b>4. Candidatar, com o seu ok.</b> O formulário é preenchido num navegador de verdade, e um ensaio para antes do botão de enviar. Nada sai sem a sua aprovação ou sem uma autorização que você deu para aquele tipo de vaga, sempre dentro de um teto por dia e de uma janela de horário. CAPTCHA e login ficam com você.</td>
  </tr>
  <tr>
    <td align="center"><picture><source media="(prefers-color-scheme: dark)" srcset=".github/assets/step-inbox-dark.svg"><img alt="" src=".github/assets/step-inbox-light.svg" width="96"></picture></td>
    <td><b>5. Acompanhar.</b> Com acesso só de leitura ao Gmail, as respostas movem cada candidatura no funil. Toda candidatura tem um histórico que só cresce e uma prova de envio.</td>
  </tr>
</table>

O Job Orchestrator se candidata em seu nome. Leia o que ele prepara antes de deixar que envie sozinho.

## Dentro das regras

- O LinkedIn nunca é automatizado, porque os termos dele proíbem. Importe a exportação de dados do LinkedIn para preencher o perfil e salve vagas do LinkedIn pelo link para acompanhar as candidaturas que você mesmo fizer.
- Nada de resolvedor de CAPTCHA nem de truque para esconder o robô. Quando um site bloqueia acesso automático, a fonte fica de fora.
- O coletor segue o robots.txt, espera entre as requisições e abre uma conexão por site.
- Fontes cujos termos não permitem esse uso ficam no registro como bloqueadas (LinkedIn Jobs, France Travail) ou apenas com parceria (a API de empregador da Gupy e o MyCareersFuture de Singapura), com o motivo à vista.

Das 52 fontes registradas, 24 são estáveis, 20 são experimentais, 4 precisam de uma chave de API gratuita sua, 2 dependem de parceria e 2 estão bloqueadas. O catálogo do Batedor tem 34.880 quadros de vagas de empresas.

## De perto

<picture>
  <source media="(prefers-color-scheme: dark)" srcset=".github/assets/screen-live-map-dark.webp">
  <img alt="O mapa ao vivo: as fontes à esquerda e depois descoberta, candidatura e os agentes de IA, com números em cada aresta." src=".github/assets/screen-live-map-light.webp" width="100%">
</picture>

**Mapa ao vivo.** Os agentes e o que passa entre eles, atualizados enquanto a busca roda.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset=".github/assets/screen-coverage-dark.webp">
  <img alt="A tela de cobertura: países integrados, países com vaga aberta vista, pistas sem teste e países ainda não pesquisados." src=".github/assets/screen-coverage-light.webp" width="100%">
</picture>

**Cobertura.** Para 249 países e territórios: onde uma fonte funciona, onde aparecem vagas por quadros globais e onde nada foi conferido ainda.

## Download

Cada download é um executável único e autocontido, com o painel, o coletor e a extensão do navegador dentro. Não precisa ter o .NET instalado.

| Sistema | Download | Observações |
| --- | --- | --- |
| **Linux** x64 | [Instalador (.run)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-linux-x64.run) &nbsp; [Pacote (.tar.gz)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-linux-x64.tar.gz) | `chmod +x` no instalador e rode. Sem sudo. |
| **Linux** arm64 | [Instalador (.run)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-linux-arm64.run) &nbsp; [Pacote (.tar.gz)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-linux-arm64.tar.gz) | |
| **macOS** Apple Silicon | [Imagem de disco (.dmg)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-macos-arm64.dmg) | macOS 15 ou mais novo. |
| **macOS** Intel | [Imagem de disco (.dmg)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-macos-x64.dmg) | macOS 15 ou mais novo. |
| **Windows** x64 | [Instalador (.exe)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-windows-x64-setup.exe) &nbsp; [Pacote (.zip)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-windows-x64.zip) | |
| **Windows** arm64 | [Instalador (.exe)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-windows-arm64-setup.exe) &nbsp; [Pacote (.zip)](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/job-orchestrator-windows-arm64.zip) | Experimental. |

O SHA-256 de cada arquivo está em [SHA256SUMS.txt](https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download/SHA256SUMS.txt).

Os pacotes ainda não têm assinatura com certificado pago, então a primeira abertura pede um passo a mais:

- **macOS:** o app tem assinatura ad hoc. Se o macOS recusar a abertura, vá em Ajustes do Sistema, depois Privacidade e Segurança, e clique em Abrir Mesmo Assim. Ou rode `xattr -dr com.apple.quarantine "/Applications/Job Orchestrator.app"`.
- **Windows:** o SmartScreen mostra "O Windows protegeu o computador". Clique em Mais informações e depois em Executar assim mesmo.

## Primeira vez

1. Rode o instalador. Ele confere a máquina (navegador, banco, coletor, modelo local, placa de vídeo, espaço em disco e chaveiro), copia o app para a sua pasta de usuário, liga o PostgreSQL próprio, prepara o coletor com um Python só dele e cria o atalho no menu. Nada pede senha de administrador, e o que faltar na máquina vira um comando ou um link para você.
2. Quando o app abre, uma configuração curta pede para ligar uma IA (o modelo local, o seu CLI ou os dois), enviar o currículo em PDF e confirmar uma tela com o que ele não conseguiu deduzir: autorização de trabalho, piso salarial, modalidade, data de início e nível de inglês. Conectar o Gmail é opcional.
3. No Início, **Automatizar tudo** liga de uma vez a busca programada, o Explorador, o modo de envio e o e-mail. Se preferir, fique no modo de revisão e aprove cada candidatura.

```sh
job-orchestrator               # abre o app; na primeira vez, o instalador
job-orchestrator --status      # mostra se está no ar
job-orchestrator --stop        # para os serviços
job-orchestrator --backup      # copia o banco agora
job-orchestrator --restore ZIP # restaura o banco de um backup
job-orchestrator --mcp         # servidor MCP para o Claude Code e o Claude Desktop
job-orchestrator --uninstall   # tira o app e mantém os seus dados (com --purge, eles saem também)
```

## Por dentro

<picture>
  <source media="(prefers-color-scheme: dark)" srcset=".github/assets/stack-dark.svg">
  <img alt=".NET 10, C#, PostgreSQL, Angular, TypeScript, Python, Scrapy, Playwright, Ollama, Qwen, Chromium, Docker, Linux, macOS, Windows e Gmail." src=".github/assets/stack-light.svg" width="100%">
</picture>

- **Backend:** .NET 10, um monólito modular em fatias verticais com DDD e CQRS, outbox e inbox para os eventos, endpoints com Carter e Dapper sobre o PostgreSQL. Um processo Worker roda os agendamentos, o Explorador e a sincronização do e-mail.
- **Painel:** Angular 22 com signals. O mapa ao vivo é um canvas em SVG desenhado à mão, e os ícones isométricos são desenhados por código a partir de uma geometria única.
- **Coleta:** APIs oficiais primeiro, spiders do Scrapy para páginas de carreira (robots.txt, AutoThrottle, JobPosting em JSON-LD) e um pool de navegadores do Playwright para páginas que precisam de JavaScript e para preencher formulários.
- **IA:** modelos locais pelo Ollama, com o Claude Code e o Codex como provedores opcionais. Cada tarefa começa no menor modelo que passa na validação e só sobe quando a saída falha, e o revisor nunca é da mesma família de modelos do redator.
- **Pacotes:** um binário autocontido por plataforma, um instalador para cada sistema e um servidor MCP embutido.
- **Testes:** suítes de unidade e de ponta a ponta contra um ATS falso com 10 cenários, além de testes de caos que derrubam o processo logo antes e logo depois do clique de enviar e conferem que a candidatura sai uma vez só ou volta para você.

Mais detalhes: [guia de uso](docs/guia.md), [arquitetura](docs/architecture.md), [manual de operação](docs/runbook.md) e [modelo de ameaças](docs/threat-model.md).

## Compilar a partir do código

Você precisa do SDK do .NET 10, do Node.js 22 ou mais novo, do Python 3.11 ou mais novo e do Docker para o banco de desenvolvimento.

```sh
git clone https://github.com/gabrielandre-math/job-orchestrator.git
cd job-orchestrator
scripts/dev.sh                    # PostgreSQL no Docker, o ATS falso, a API e o Worker; dados em .dev/
docker compose -f infra/compose.test.yaml up -d --wait
dotnet test backend/tests/JobOrchestrator.Tests --filter "Category!=E2E"
scripts/package.sh all            # os pacotes das seis plataformas, em dist/
```

## Licença

O Job Orchestrator tem código aberto para leitura sob a [PolyForm Noncommercial License 1.0.0](LICENSE.md). Você pode usar, estudar, modificar e compartilhar para qualquer fim não comercial, e isso inclui fazer a sua própria busca de emprego. Vender, oferecer como serviço pago ou embutir num produto comercial não é permitido. Para uso comercial, fale com o autor antes.

## Autor

Feito por **Gabriel André de Lima Silva** ([@gabrielandre-math](https://github.com/gabrielandre-math)). Bugs e ideias são bem-vindos nas [issues](https://github.com/gabrielandre-math/job-orchestrator/issues).

A marca é a forma 57 do [shapes.gallery](https://www.shapes.gallery/), de Monika Michalczyk, usada como veio. As logos são das respectivas marcas. Os binários do release levam as licenças do runtime e das bibliotecas que embutem.
