# syntax=docker/dockerfile:1.7
# Imagens do Job Orchestrator. api: o painel compilado mais a API. worker: Chromium (o do próprio Playwright, na versão certa),
# poppler para o Inspetor ATS, o coletor (Scrapy) num ambiente Python próprio e as CLIs do Claude e do Codex para usar as assinaturas.

FROM node:22-bookworm-slim AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx ng build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ENV DOTNET_SYSTEM_NET_DISABLEIPV6=1
WORKDIR /src
COPY backend/ backend/
RUN dotnet publish backend/src/Bootstrappers/JobOrchestrator.Api -c Release -o /out/api /p:UseAppHost=false \
 && dotnet publish backend/src/Bootstrappers/JobOrchestrator.Worker -c Release -o /out/worker /p:UseAppHost=false

# Base comum de execução: a API também roda IA (assistente, proposta da busca), o coletor (teste da varredura) e o Chromium
# (prévia do currículo), então API e Worker partem da mesma base.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
COPY --from=web /usr/local/bin/node /usr/local/bin/node
COPY --from=web /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx \
 && apt-get update && apt-get install -y --no-install-recommends poppler-utils libsecret-tools python3 python3-venv git ca-certificates fonts-liberation fonts-noto-core \
 && python3 -m venv /opt/scraper-venv && /opt/scraper-venv/bin/pip install --no-cache-dir "scrapy>=2.19,<2.20" "extruct==0.18.0" "trafilatura==2.3.1" "dateparser==1.5.0" "price-parser==0.5.1" \
 && npm install -g @anthropic-ai/claude-code @openai/codex && npm cache clean --force
COPY --from=build /out/worker/.playwright /opt/playwright-driver/.playwright
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
RUN /opt/playwright-driver/.playwright/node/linux-x64/node /opt/playwright-driver/.playwright/package/cli.js install --with-deps chromium && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /home/jo && chown 1000:1000 /home/jo
COPY scraper/ /app/scraper/
ENV Scraper__Venv=/opt/scraper-venv Scraper__Project=/app/scraper DOTNET_SYSTEM_NET_DISABLEIPV6=1 JO_DATA_DIR=/data JO_CONFIG_DIR=/config HOME=/home/jo

FROM runtime AS api
WORKDIR /app
COPY --from=build /out/api ./
COPY --from=web /src/web/dist/app/browser ./wwwroot
ENV ASPNETCORE_URLS=http://127.0.0.1:7410 Web__Root=/app/wwwroot
USER 1000:1000
ENTRYPOINT ["dotnet", "JobOrchestrator.Api.dll"]

FROM runtime AS worker
WORKDIR /app
COPY --from=build /out/worker ./
USER 1000:1000
ENTRYPOINT ["dotnet", "JobOrchestrator.Worker.dll"]
