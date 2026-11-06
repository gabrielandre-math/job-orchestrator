"""Extrai vagas de páginas de teste do Fake ATS ou de arquivos HTML locais."""

import argparse
import json
import re
from pathlib import Path
from urllib.parse import urlsplit

from lxml import etree, html

from captcha_lab import CaptchaRouter, ChallengePending, LocalLab


def text(value):
    return " ".join(value.split()) if isinstance(value, str) else ""


def make_job(url, title, company="", location="", urn=""):
    title = text(title)
    if not title or not isinstance(url, str):
        return None
    try:
        parsed = urlsplit(url)
    except ValueError:
        return None
    if not parsed.scheme and not parsed.netloc:
        host = "www.linkedin.com"
    elif parsed.scheme in ("http", "https") and parsed.netloc.lower() == parsed.hostname:
        host = parsed.hostname
    else:
        return None

    if host == "linkedin.com" or host.endswith(".linkedin.com"):
        host = "www.linkedin.com"
    elif host != "linkedin.example":
        return None

    match = re.fullmatch(r"/jobs/view/(?:[^/]+-)?([0-9]{1,24})/?", parsed.path)
    if match is None or urn and urn != f"urn:li:jobPosting:{match[1]}":
        return None
    job_id = match[1]
    return {
        "id": job_id, "title": title, "company": text(company), "location": text(location),
        "url": f"https://{host}/jobs/view/{job_id}",
    }


def structured_jobs(document):
    for script in document.xpath("//script[@type='application/ld+json']"):
        try:
            pending = [json.loads(script.text or "")]
        except (ValueError, RecursionError):
            continue
        while pending:
            item = pending.pop()
            if isinstance(item, list):
                pending.extend(reversed(item))
            elif isinstance(item, dict):
                types = item.get("@type")
                if types in ("JobPosting", "https://schema.org/JobPosting") or (
                    isinstance(types, list) and any(t in types for t in ("JobPosting", "https://schema.org/JobPosting"))
                ):
                    organization = item.get("hiringOrganization")
                    organization = organization if isinstance(organization, dict) else {}
                    location = item.get("jobLocation")
                    if isinstance(location, list):
                        location = next((entry for entry in location if isinstance(entry, dict)), {})
                    address = location.get("address") if isinstance(location, dict) else {}
                    address = address if isinstance(address, dict) else {}
                    label = ", ".join(filter(None, (text(address.get(key)) for key in ("addressLocality", "addressRegion", "addressCountry"))))
                    job = make_job(item.get("url"), item.get("title"), organization.get("name"), label)
                    if job:
                        yield job
                else:
                    pending.extend(item[key] for key in ("item", "itemListElement", "@graph") if key in item)


def card_jobs(document):
    for card in document.find_class("base-search-card"):
        title = card.find_class("base-search-card__title")
        company = card.find_class("base-search-card__subtitle")
        location = card.find_class("job-search-card__location")
        link = card.find_class("base-card__full-link")
        if not title or not link:
            continue
        job = make_job(
            link[0].get("href"), title[0].text_content(),
            company[0].text_content() if company else "",
            location[0].text_content() if location else "",
            card.get("data-entity-urn", ""),
        )
        if job:
            yield job


def collect_page(content, jobs):
    document = html.fromstring(content, parser=html.HTMLParser(encoding="utf-8", no_network=True))
    extracted = {}
    for name, adapter in (("jsonld", structured_jobs), ("cards", card_jobs)):
        extracted[name] = 0
        for job in adapter(document):
            extracted[name] += 1
            current = jobs.setdefault(job["url"], job)
            for field in ("company", "location"):
                if not current[field]:
                    current[field] = job[field]
    if not any(extracted.values()) and not document.xpath("//*[@data-lab-empty]"):
        raise ValueError("Página sem vagas reconhecíveis nem marcador de lista vazia")

    links = document.xpath("//a[contains(concat(' ', normalize-space(@rel), ' '), ' next ')]/@href")
    return extracted, links[0] if links else None


def route_local(port, start="/lab/linkedin", max_pages=10):
    lab = LocalLab(port)
    captcha = CaptchaRouter(lab)
    jobs, pages, visited = {}, [], set()
    path = start
    while path:
        if path in visited:
            return {"status": "pagination_loop", "jobs": list(jobs.values()), "pages": pages}
        if len(visited) == max_pages:
            return {"status": "page_limit", "jobs": list(jobs.values()), "pages": pages}
        visited.add(path)
        status, body = lab.request(path)
        challenge = None
        if status == 403:
            try:
                challenge, body = captcha.resolve(path, body)
            except ChallengePending as error:
                return {
                    "status": "needs_human", "jobs": list(jobs.values()), "pages": pages,
                    "blocked": {"page": path, "challenge": error.kind, "reason": str(error)},
                }
        elif status != 200:
            raise ValueError(f"O laboratório respondeu HTTP {status} em {path}")
        extracted, following = collect_page(body, jobs)
        pages.append({"page": path, "captcha": challenge, "extracted": extracted})
        if following and not re.fullmatch(r"/lab/linkedin(?:/[a-z0-9-]+)?", following):
            return {"status": "invalid_pagination", "jobs": list(jobs.values()), "pages": pages}
        path = following
    return {"status": "ok", "jobs": list(jobs.values()), "pages": pages}


def route_files(files):
    jobs, pages = {}, []
    for index, path in enumerate(files, 1):
        with path.open("rb") as stream:
            content = stream.read(2_000_001)
        if len(content) > 2_000_000:
            raise ValueError(f"O arquivo HTML {index} ultrapassa 2 MB")
        extracted, _ = collect_page(content, jobs)
        pages.append({"file": index, "extracted": extracted})
    return {"status": "ok", "jobs": list(jobs.values()), "pages": pages}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=5470)
    parser.add_argument("--html", nargs="+", type=Path, metavar="ARQUIVO")
    parser.add_argument("--scenario", choices=("default", "unsupported"), default="default")
    args = parser.parse_args()
    try:
        start = "/lab/linkedin" if args.scenario == "default" else "/lab/linkedin/manual"
        result = route_files(args.html) if args.html else route_local(args.port, start)
        print(json.dumps(result, indent=2, ensure_ascii=False))
        if result["status"] != "ok":
            parser.exit(2)
    except (OSError, ValueError, etree.LxmlError) as error:
        parser.exit(1, f"Falha no laboratório de vagas. {error}\n")


if __name__ == "__main__":
    main()
