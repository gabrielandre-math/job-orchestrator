#!/usr/bin/env python3
"""Baixa os logos do svgl (https://svgl.app, coleção MIT; cada logo é marca do seu dono) para o painel: um SVG por logo em
web/projects/app/public/logos/svgl/ (com a variante para fundo escuro quando existe) e o índice web/projects/ui/src/lib/svgl.ts,
com o nome normalizado de cada um. Uso: scripts/svgl.py. Roda de novo para atualizar; nada é baixado de novo se o arquivo já existe."""
import json, re, time, urllib.request
from pathlib import Path

root = Path(__file__).resolve().parent.parent
out = root / "web/projects/app/public/logos/svgl"; out.mkdir(parents=True, exist_ok=True)
ua = {"User-Agent": "JobOrchestrator/1.0 (painel local; logos do svgl)"}

def get(url: str) -> bytes:
    with urllib.request.urlopen(urllib.request.Request(url, headers=ua), timeout=30) as r: return r.read()

def norm(s: str) -> str:
    return re.sub(r"[^a-z0-9]", "", s.lower().replace("#", "sharp").replace("+", "plus"))

items = json.loads(get("https://api.svgl.app"))
index, used = {}, set()
for it in items:
    route = it["route"]
    light, dark = (route, None) if isinstance(route, str) else (route.get("light"), route.get("dark"))
    if not light: continue
    base = Path(light).stem.replace("_light", "")
    files = {}
    for kind, url in (("light", light), ("dark", dark)):
        if not url: continue
        name = f"{base}{'-dark' if kind == 'dark' else ''}.svg"
        target = out / name
        if not target.exists():
            data = get(url)
            # Só SVG de verdade: sem script nem evento (o painel mostra como <img>, mas o arquivo fica limpo de qualquer jeito).
            if b"<script" in data.lower() or re.search(rb"\son[a-z]+\s*=", data, re.I): continue
            target.write_bytes(data)
            time.sleep(0.05)
        files[kind] = name
    if "light" not in files: continue
    key = norm(it["title"])
    if key in used: continue
    used.add(key)
    cats = it["category"] if isinstance(it["category"], list) else [it["category"]]
    index[key] = {"t": it["title"], "f": files["light"], **({"d": files["dark"]} if "dark" in files else {}), "c": cats[0]}

(out / "NOTICE.txt").write_text("Logos do svgl (https://svgl.app, github.com/pheralb/svgl, coleção sob licença MIT).\n"
    "Cada logo é marca registrada do seu dono e aparece no app só para identificar a tecnologia, a empresa ou a plataforma.\n", encoding="utf-8")
ts = root / "web/projects/ui/src/lib/svgl.ts"
ts.write_text("/** Índice dos logos do svgl (scripts/svgl.py): nome normalizado para arquivo em /logos/svgl, variante escura e categoria. */\n"
    "export interface SvglLogo { t: string; f: string; d?: string; c: string }\n"
    f"export const SVGL: Record<string, SvglLogo> = {json.dumps(index, ensure_ascii=False, separators=(',', ':'))};\n", encoding="utf-8")
print(f"{len(index)} logos em {out}")
