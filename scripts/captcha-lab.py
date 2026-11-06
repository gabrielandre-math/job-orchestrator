#!/usr/bin/env python3
"""Resolve o CAPTCHA visual do Fake ATS local por comparação com imagens de referência."""

import argparse
import json
import struct
from html.parser import HTMLParser
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import HTTPRedirectHandler, ProxyHandler, build_opener
from uuid import UUID


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class ChallengeForm(HTMLParser):
    challenge = None

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        if tag == "input" and attributes.get("name") == "challenge":
            self.challenge = UUID(attributes.get("value") or "")


def glyphs(image):
    """Lê o BMP do laboratório e extrai os dígitos, removendo o ruído e as margens."""
    if len(image) < 54 or image[:2] != b"BM":
        raise ValueError("Imagem BMP inválida")
    offset, header, width, height, planes, depth, compression = struct.unpack_from("<IIiiHHI", image, 10)
    stride = (width * 3 + 3) & ~3
    if not (
        offset == 54 and header == 40 and 0 < width <= 400 and height == -72
        and planes == 1 and depth == 24 and compression == 0 and len(image) == offset + stride * -height
    ):
        raise ValueError("Formato diferente do BMP do laboratório")

    # Cada coluna é uma máscara binária. Só os pixels escuros contam como parte do dígito.
    columns = [0] * width
    for y in range(-height):
        start = offset + y * stride
        for x in range(width):
            if image[start + x * 3] < 96:
                columns[x] |= 1 << y

    shapes, run, occupied = [], [], 0
    for column in [*columns, 0]:
        if column:
            run.append(column)
            occupied |= column
        elif run:
            # O bit ativo mais baixo dá a primeira linha do dígito, independentemente da posição na imagem.
            top_bit = occupied & -occupied
            shapes.append(tuple(value // top_bit for value in run))
            run, occupied = [], 0
    return shapes


def solve(port):
    base = f"http://127.0.0.1:{port}/lab/captcha"
    client = build_opener(ProxyHandler({}), NoRedirect())

    def read(path="", data=None):
        with client.open(base + path, data=data, timeout=5) as response:
            body = response.read(128_001)
            if len(body) > 128_000:
                raise ValueError("Resposta maior que a esperada do laboratório")
            return body

    alphabet = glyphs(read("/alphabet"))
    if len(set(alphabet)) != 10 or len(alphabet) != 10:
        raise ValueError("Alfabeto de referência inválido")
    digits = dict(zip(alphabet, "0123456789"))

    form = ChallengeForm()
    form.feed(read().decode("utf-8"))
    if form.challenge is None:
        raise ValueError("Desafio ausente do formulário")
    shapes = glyphs(read(f"/{form.challenge}/image"))
    if len(shapes) != 5 or any(shape not in digits for shape in shapes):
        raise ValueError("Não foi possível reconhecer os cinco dígitos")
    answer = "".join(digits[shape] for shape in shapes)
    payload = urlencode({"challenge": form.challenge, "answer": answer}).encode()
    return json.loads(read(data=payload))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, metavar="PORTA", default=5470)
    args = parser.parse_args()
    if not 1 <= args.port <= 65535:
        parser.error("--port deve estar entre 1 e 65535")
    try:
        print(json.dumps(solve(args.port), indent=2, ensure_ascii=False))
    except (HTTPError, URLError, TimeoutError, ValueError) as error:
        parser.exit(1, f"Falha no laboratório de CAPTCHA: {error}\n")


if __name__ == "__main__":
    main()
