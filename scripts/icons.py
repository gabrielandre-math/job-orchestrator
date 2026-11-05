#!/usr/bin/env python3
"""Ícones do app para o macOS (.icns) e o Windows (.ico), a partir de packaging/app-icon.svg: o rsvg-convert desenha cada tamanho em PNG
e este script monta os dois formatos (os dois aceitam PNG dentro). Uso: scripts/icons.py <pasta de saída>."""
import struct, subprocess, sys, tempfile
from pathlib import Path

root = Path(__file__).resolve().parent.parent
svg = root / "packaging" / "app-icon.svg"
out = Path(sys.argv[1]); out.mkdir(parents=True, exist_ok=True)

def png(size: int, tmp: Path) -> bytes:
    f = tmp / f"{size}.png"
    subprocess.run(["rsvg-convert", "-w", str(size), "-h", str(size), "-o", str(f), str(svg)], check=True)
    return f.read_bytes()

with tempfile.TemporaryDirectory() as t:
    tmp = Path(t)
    cache = {s: png(s, tmp) for s in (16, 24, 32, 48, 64, 128, 256, 512, 1024)}

    # .icns: 'icns' + tamanho total, e cada entrada com o tipo, o tamanho (com o cabeçalho de 8 bytes) e o PNG.
    types = [("icp4", 16), ("icp5", 32), ("icp6", 64), ("ic07", 128), ("ic08", 256), ("ic09", 512), ("ic10", 1024), ("ic11", 32), ("ic12", 64), ("ic13", 256), ("ic14", 512)]
    body = b"".join(k.encode() + struct.pack(">I", 8 + len(cache[s])) + cache[s] for k, s in types)
    (out / "AppIcon.icns").write_bytes(b"icns" + struct.pack(">I", 8 + len(body)) + body)

    # .ico: cabeçalho, uma entrada de 16 bytes por tamanho (256 vira 0) e os PNGs em seguida.
    sizes = [16, 24, 32, 48, 64, 128, 256]
    offset = 6 + 16 * len(sizes)
    entries, data = b"", b""
    for s in sizes:
        d = cache[s]
        entries += struct.pack("<BBBBHHII", 0 if s == 256 else s, 0 if s == 256 else s, 0, 0, 1, 32, len(d), offset + len(data))
        data += d
    (out / "job-orchestrator.ico").write_bytes(struct.pack("<HHH", 0, 1, len(sizes)) + entries + data)
    (out / "app-icon-512.png").write_bytes(cache[512])
print(f"ícones em {out}")
