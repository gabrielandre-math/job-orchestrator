# Laboratórios locais de CAPTCHA e vagas

> **Fora do app desde a 1.3.0.** Os laboratórios de CAPTCHA serviam à página de testes, que saiu: o app nunca resolve CAPTCHA, e quem envia é você, com o copiloto. Este documento fica como registro.

O Fake ATS serve páginas sintéticas em `127.0.0.1`. O laboratório de vagas combina dois extratores com um roteador de CAPTCHA. A primeira página entrega JSON-LD. A segunda pede o código de uma imagem e entrega cartões com uma vaga repetida. A terceira pede uma soma e entrega o último cartão.

## Referências comparadas

| Projeto | Abordagem observada | Decisão no Job Orc |
|---|---|---|
| [JobSpy](https://github.com/speedyapply/JobSpy) | Adaptadores por fonte, uma saída comum e deduplicação dos cartões por ID | Separar a coleta dos extratores e devolver a mesma estrutura para JSON-LD e cartões |
| [linkedin-jobs-api](https://github.com/VishwaGauravIn/linkedin-jobs-api) | Interface pequena voltada a vagas, com título, empresa, local e URL | Manter o contrato de vaga pequeno e normalizar a URL antes de deduplicar |
| [linkedin_scraper](https://github.com/joeyism/linkedin_scraper) | Playwright com sessão de navegador para vagas, perfis e empresas | Usar HTML local para testar os extratores com páginas representativas e dispensar sessão de usuário |
| [linkedin-mcp-server](https://github.com/stickerdaniel/linkedin-mcp-server) | Operações separadas para busca e detalhes, ligadas ao perfil do navegador | Limitar o laboratório à leitura de vagas e trabalhar com identificadores canônicos |

A amostra prioriza os projetos relevantes para vagas no [tópico do GitHub](https://github.com/topics/linkedin-scraper), com JobSpy como referência adicional. A automação do site real fica fora do experimento. O LinkedIn [proíbe a extração automatizada por software de terceiros](https://www.linkedin.com/help/linkedin/answer/a1340567?lang=pt-BR).

## Como rodar

Para rodar tudo de uma vez, sem `uv`, use `scripts/labs.sh`. Ele sobe um Fake ATS de laboratório na porta 5471 (ou `FAKEATS_LAB_PORT`), separado do Fake ATS de desenvolvimento na 5470, instala o lxml em `.dev/labvenv`, roda os dois resolvedores, a coleta paginada, o cenário sem resolvedor e os 11 testes, e desliga o servidor no fim.

Para rodar passo a passo:

Na raiz do repositório, suba o Fake ATS em um terminal. Use o SDK .NET 10 e Python 3.11 ou mais recente. O extrator HTML usa `lxml`. Os exemplos com `uv` instalam essa dependência no ambiente de execução.

```sh
FAKEATS_CAPTCHA_LAB=1 dotnet run --project backend/src/Tools/JobOrchestrator.FakeAts
```

No segundo terminal, exercite os resolvedores e depois a coleta paginada.

```sh
python3 scripts/captcha-lab.py --kind image
python3 scripts/captcha-lab.py --kind math
uv run --with 'lxml>=5,<7' python scripts/linkedin_lab.py
uv run --with 'lxml>=5,<7' python scripts/linkedin_lab.py --scenario unsupported
```

O fluxo paginado devolve três vagas únicas. O cenário `unsupported` devolve `needs_human` e mantém a primeira vaga no resultado. O processo termina com código 2 nesse cenário. Cada página registra quantas vagas vieram de cada extrator e qual desafio foi resolvido.

Os arquivos HTML locais, sintéticos ou obtidos com autorização, também podem ser passados diretamente ao extrator.

```sh
uv run --with 'lxml>=5,<7' python scripts/linkedin_lab.py --html pagina-1.html pagina-2.html
```

## Testes e limites

Com o Fake ATS ainda ativo, rode os testes por HTTP e os casos com HTML em arquivo.

```sh
PYTHONPATH=scripts PYTHONDONTWRITEBYTECODE=1 FAKEATS_LAB_TEST_PORT=5470 \
  uv run --with 'lxml>=5,<7' python -m unittest discover -s scripts/tests -v
```

O cliente HTTP usa apenas loopback, rejeita redirecionamentos e não usa proxies. A leitura de arquivos é offline. As vagas e os desafios ficam em memória, com resultado em stdout. Cada desafio vale por dois minutos e aceita uma tentativa na rota que o emitiu. O teste de arquivo cria o HTML sintético em um diretório temporário e o remove no encerramento.

Os resolvedores automáticos cobrem o código visual de cinco dígitos e a soma do Fake ATS. O desafio interativo valida o encaminhamento manual. O teste mede a aceitação da resposta pelo servidor local. A integração real com reCAPTCHA, hCaptcha e Turnstile exigiria um ambiente próprio de teste e uma rodada de validação para cada provedor.
