"""Spider de páginas de carreira: lê a página da empresa, o sitemap e os links de vaga do mesmo domínio.

Entrega itens em JSON Lines para o Worker do Job Orchestrator:
- posting: vaga estruturada pelo padrão schema.org JobPosting (JSON-LD), que as páginas de carreira publicam para o Google;
- board: link de um ATS (Greenhouse, Lever, Ashby, SmartRecruiters, Recruitee, Breezy, Personio, Workable, BambooHR);
- email: e-mail de candidatura, só quando o texto em volta fala de enviar currículo ou se candidatar;
- candidate: página com cara de vaga e sem dado estruturado, para a IA do Job Orchestrator ler;
- progress: contagem de páginas lidas, a cada dez.
"""
import json
import re
from urllib.parse import urljoin, urlparse

import scrapy

JOB_WORDS = re.compile(r"vaga|vagas|job|jobs|career|careers|carreira|carreiras|oportunidade|position|opening|trabalhe|work-with-us|join", re.I)
SECTION_WORDS = re.compile(r"requisitos|responsabilidades|atribui[cç][oõ]es|qualifica[cç][oõ]es|requirements|responsibilities|qualifications|what you.ll do|o que voc[eê] vai fazer", re.I)
EMAIL = re.compile(r"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")
APPLY_CUES = re.compile(r"curr[ií]culo|\bcv\b|resume|candidat|apply|envie|send|vaga|recrut|talent|selec", re.I)
BOARDS = [
    (re.compile(r"boards(?:-api)?\.greenhouse\.io/(?:embed/job_board\?for=)?([\w-]+)", re.I), "greenhouse"),
    (re.compile(r"job-boards\.greenhouse\.io/([\w-]+)", re.I), "greenhouse"),
    (re.compile(r"jobs\.lever\.co/([\w-]+)", re.I), "lever"),
    (re.compile(r"jobs\.ashbyhq\.com/([\w.-]+)", re.I), "ashby"),
    (re.compile(r"(?:careers|jobs)\.smartrecruiters\.com/([\w-]+)", re.I), "smartrecruiters"),
    (re.compile(r"([\w-]+)\.recruitee\.com", re.I), "recruitee"),
    (re.compile(r"([\w-]+)\.breezy\.hr", re.I), "breezy"),
    (re.compile(r"([\w-]+)\.jobs\.personio\.(?:de|com)", re.I), "personio"),
    (re.compile(r"apply\.workable\.com/([\w-]+)", re.I), "workable"),
    (re.compile(r"([\w-]+)\.bamboohr\.com/careers", re.I), "bamboohr"),
    (re.compile(r"https?://([\w-]+)\.gupy\.io", re.I), "gupy"),
    (re.compile(r"https?://([\w-]+(?:\.[a-z]{2})?)\.teamtailor\.com", re.I), "teamtailor"),
]
PATHS = ["", "/careers", "/carreiras", "/vagas", "/jobs", "/trabalhe-conosco", "/work-with-us", "/join-us"]


class CareersSpider(scrapy.Spider):
    name = "careers"

    def __init__(self, seeds="", **kwargs):
        super().__init__(**kwargs)
        self.seeds = [s.strip() for s in seeds.split(",") if s.strip()]
        self.pages = 0
        self.seen_postings = set()
        self.seen_boards = set()
        self.seen_emails = set()

    async def start(self):
        for seed in self.seeds:
            base = seed if seed.startswith("http") else f"https://{seed}"
            parsed = urlparse(base)
            root = f"{parsed.scheme}://{parsed.netloc}"
            self.allowed = getattr(self, "allowed", set()) | {parsed.netloc.removeprefix("www.")}
            # A página dada e as rotas de carreira mais comuns; o sitemap traz o resto.
            paths = [parsed.path] if parsed.path not in ("", "/") else PATHS
            for path in paths:
                yield scrapy.Request(urljoin(root, path or "/"), callback=self.parse, errback=self.failed, dont_filter=False)
            yield scrapy.Request(urljoin(root, "/sitemap.xml"), callback=self.sitemap, errback=self.failed)

    def failed(self, failure):
        return None

    def sitemap(self, response):
        for loc in re.findall(r"<loc>\s*([^<\s]+)\s*</loc>", response.text):
            if loc.endswith(".xml") and JOB_WORDS.search(loc):
                yield scrapy.Request(loc, callback=self.sitemap, errback=self.failed)
            elif JOB_WORDS.search(urlparse(loc).path):
                yield scrapy.Request(loc, callback=self.parse, errback=self.failed)

    def parse(self, response):
        if not hasattr(response, "text") or "html" not in response.headers.get("Content-Type", b"").decode().lower():
            return
        self.pages += 1
        if self.pages % 10 == 0:
            yield {"type": "progress", "pages": self.pages}

        found = False
        for data in self.json_ld(response):
            for job in self.job_postings(data):
                item = self.posting(job, response)
                if item and item["key"] not in self.seen_postings:
                    self.seen_postings.add(item["key"])
                    found = True
                    yield item

        html = response.text
        for pattern, kind in BOARDS:
            for m in pattern.finditer(html):
                token = m.group(1).lower()
                if token in ("www", "app", "api", "jobs", "careers", "embed", "portal", "login", "static", "cdn", "blog", "help", "suporte-candidatos", "j", "apply", "resources", "support", "docs"):
                    continue
                source = f"{kind}:{token}"
                if source not in self.seen_boards:
                    self.seen_boards.add(source)
                    yield {"type": "board", "source": source, "url": response.url}

        text = self.text(response)
        for m in EMAIL.finditer(text):
            email = m.group(0).strip(".").lower()
            around = text[max(0, m.start() - 220): m.end() + 120]
            if email in self.seen_emails or not APPLY_CUES.search(around) or email.endswith((".png", ".jpg", ".svg", ".webp")):
                continue
            self.seen_emails.add(email)
            yield {"type": "email", "email": email, "url": response.url, "context": " ".join(around.split())[:300]}

        title = " ".join(response.css("title::text, h1::text").getall())[:300]
        if not found and JOB_WORDS.search(title + " " + response.url) and len(SECTION_WORDS.findall(text)) >= 2:
            yield {"type": "candidate", "url": response.url, "title": " ".join(title.split()), "text": text[:12000]}

        for href in response.css("a::attr(href)").getall():
            url = response.urljoin(href)
            host = urlparse(url).netloc.removeprefix("www.")
            if host in self.allowed and JOB_WORDS.search(url) and not re.search(r"\.(pdf|jpg|png|zip|mp4)$", url, re.I):
                yield scrapy.Request(url, callback=self.parse, errback=self.failed)

    @staticmethod
    def json_ld(response):
        for raw in response.xpath('//script[@type="application/ld+json"]/text()').getall():
            try:
                yield json.loads(raw.strip())
            except ValueError:
                continue

    def job_postings(self, data):
        if isinstance(data, list):
            for d in data:
                yield from self.job_postings(d)
        elif isinstance(data, dict):
            kind = data.get("@type")
            if kind == "JobPosting" or (isinstance(kind, list) and "JobPosting" in kind):
                yield data
            for d in data.get("@graph", []) or []:
                yield from self.job_postings(d)

    @staticmethod
    def text(response):
        parts = response.xpath("//body//text()[not(ancestor::script) and not(ancestor::style) and not(ancestor::noscript)]").getall()
        return re.sub(r"\s+\n", "\n", re.sub(r"[ \t]+", " ", "\n".join(p.strip() for p in parts if p.strip())))

    @staticmethod
    def posting(job, response):
        title = (job.get("title") or "").strip()
        if not title:
            return None
        org = job.get("hiringOrganization") or {}
        company = org.get("name") if isinstance(org, dict) else str(org)
        locations = job.get("jobLocation") or []
        if isinstance(locations, dict):
            locations = [locations]
        place = []
        for loc in locations:
            addr = (loc or {}).get("address") or {}
            if isinstance(addr, dict):
                place.append(", ".join(x for x in [addr.get("addressLocality"), addr.get("addressRegion"), addr.get("addressCountry") if isinstance(addr.get("addressCountry"), str) else None] if x))
        salary = job.get("baseSalary") or {}
        value = salary.get("value") if isinstance(salary, dict) else None
        money = None
        if isinstance(value, dict) and (value.get("minValue") or value.get("value")):
            money = f"{salary.get('currency', '')} {value.get('minValue') or value.get('value')}-{value.get('maxValue') or ''} {value.get('unitText', '')}".strip()
        description = re.sub(r"<[^>]+>", " ", job.get("description") or "")
        url = job.get("url") or response.url
        return {
            "type": "posting", "key": f"{url}|{title}", "url": url, "page": response.url, "title": title, "company": company or "",
            "location": "; ".join(p for p in place if p), "remote": job.get("jobLocationType") == "TELECOMMUTE",
            "employment": job.get("employmentType") if isinstance(job.get("employmentType"), str) else ", ".join(job.get("employmentType") or []),
            "salary": money, "posted": job.get("datePosted"), "valid": job.get("validThrough"), "description": " ".join(description.split())[:20000],
        }
