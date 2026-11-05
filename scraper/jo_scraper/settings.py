# Configuração do coletor. Educado por regra: obedece o robots.txt, uma conexão por domínio, atraso ajustado sozinho
# (AutoThrottle) e cache local para não buscar a mesma página de novo. Sem disfarce de navegador e sem rodízio de IP.
import os

BOT_NAME = "jo_scraper"
SPIDER_MODULES = ["jo_scraper.spiders"]
NEWSPIDER_MODULE = "jo_scraper.spiders"

USER_AGENT = "JobOrchestratorBot/1.0 (uso pessoal; procura vagas em paginas de carreira)"
ROBOTSTXT_OBEY = True
COOKIES_ENABLED = False
TELNETCONSOLE_ENABLED = False

CONCURRENT_REQUESTS = 8
CONCURRENT_REQUESTS_PER_DOMAIN = 1
DOWNLOAD_DELAY = 1.5
AUTOTHROTTLE_ENABLED = True
AUTOTHROTTLE_START_DELAY = 1.5
AUTOTHROTTLE_MAX_DELAY = 20
AUTOTHROTTLE_TARGET_CONCURRENCY = 1.0

DEPTH_LIMIT = 2
CLOSESPIDER_PAGECOUNT = int(os.environ.get("JO_MAX_PAGES", "60"))
CLOSESPIDER_TIMEOUT = int(os.environ.get("JO_MAX_SECONDS", "240"))
DOWNLOAD_TIMEOUT = 20
DOWNLOAD_MAXSIZE = 3_000_000
RETRY_TIMES = 1

HTTPCACHE_ENABLED = True
HTTPCACHE_EXPIRATION_SECS = 6 * 3600
HTTPCACHE_DIR = os.environ.get("JO_SCRAPER_CACHE", "httpcache")

FEED_EXPORT_ENCODING = "utf-8"
LOG_LEVEL = os.environ.get("JO_SCRAPER_LOG", "INFO")
REQUEST_FINGERPRINTER_IMPLEMENTATION = "2.7"
