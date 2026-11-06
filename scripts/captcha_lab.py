"""Roteamento de desafios do Fake ATS local."""

import argparse
import json
import re
import struct
from html.parser import HTMLParser
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, ProxyHandler, build_opener
from uuid import UUID


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class LocalLab:
    def __init__(self, port):
        if not 1 <= port <= 65535:
            raise ValueError("A porta precisa estar entre 1 e 65535")
        self.origin = f"http://127.0.0.1:{port}"
        self.client = build_opener(ProxyHandler({}), NoRedirect())

    def request(self, path, data=None):
        if not re.fullmatch(r"/lab/[a-z0-9/-]+(?:\?kind=[a-z]+)?", path) or "//" in path:
            raise ValueError("O laboratório aceita somente caminhos locais sob /lab/")
        try:
            response = self.client.open(self.origin + path, data=data, timeout=5)
        except HTTPError as error:
            response = error
        with response:
            body = response.read(512_001)
            if len(body) > 512_000:
                raise ValueError("Resposta maior que a esperada do laboratório")
            return response.code, body

    def read(self, path):
        status, body = self.request(path)
        if status != 200:
            raise ValueError(f"O laboratório respondeu HTTP {status} em {path}")
        return body


class ChallengeForm(HTMLParser):
    def __init__(self):
        super().__init__()
        self.kind = None
        self.action = None
        self.challenge = None
        self.expression = []
        self.in_expression = False

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        if "data-captcha-kind" in attributes:
            self.kind = attributes["data-captcha-kind"]
            self.action = attributes.get("action")
        if tag == "input" and attributes.get("name") == "challenge":
            self.challenge = attributes.get("value")
        if tag == "p" and "data-captcha-expression" in attributes:
            self.in_expression = True

    def handle_data(self, data):
        if self.in_expression:
            self.expression.append(data)

    def handle_endtag(self, tag):
        if tag == "p":
            self.in_expression = False


class ChallengePending(ValueError):
    def __init__(self, kind, reason):
        super().__init__(reason)
        self.kind = kind


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


class CaptchaRouter:
    def __init__(self, lab):
        self.lab = lab
        self.digits = None
        self.solvers = {"image": self.image, "math": self.math}

    def image(self, form, challenge):
        if self.digits is None:
            alphabet = glyphs(self.lab.read("/lab/captcha/alphabet"))
            if len(alphabet) != 10 or len(set(alphabet)) != 10:
                raise ValueError("Alfabeto de referência inválido")
            self.digits = dict(zip(alphabet, "0123456789"))

        shapes = glyphs(self.lab.read(f"/lab/captcha/{challenge}/image"))
        if len(shapes) != 5 or any(shape not in self.digits for shape in shapes):
            raise ValueError("Não foi possível reconhecer os cinco dígitos")
        return "".join(self.digits[shape] for shape in shapes)

    @staticmethod
    def math(form, _challenge):
        expression = "".join(form.expression).strip()
        match = re.fullmatch(r"([0-9]{1,2})\s*\+\s*([0-9]{1,2})", expression)
        if match is None:
            raise ValueError("Expressão fora do formato aritmético do laboratório")
        return str(int(match[1]) + int(match[2]))

    def resolve(self, path, document):
        form = ChallengeForm()
        form.feed(document.decode("utf-8"))
        solver = self.solvers.get(form.kind)
        if solver is None:
            raise ChallengePending(form.kind or "unknown", "Desafio sem resolvedor local")
        if form.action != urlsplit(path).path or form.challenge is None:
            raise ChallengePending(form.kind, "Formulário do desafio inválido")

        try:
            challenge = UUID(form.challenge)
            answer = solver(form, challenge)
        except ValueError as error:
            raise ChallengePending(form.kind, str(error)) from error

        payload = urlencode({"challenge": challenge, "answer": answer}).encode()
        status, body = self.lab.request(form.action, payload)
        if status != 200:
            raise ChallengePending(form.kind, f"Resposta não aceita pelo servidor (HTTP {status})")
        return form.kind, body


def solve(port, kind="image"):
    lab = LocalLab(port)
    path = f"/lab/captcha?kind={kind}"
    _, body = CaptchaRouter(lab).resolve(path, lab.read(path))
    return json.loads(body)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, metavar="PORTA", default=5470)
    parser.add_argument("--kind", choices=("image", "math", "unsupported"), default="image")
    args = parser.parse_args()
    try:
        print(json.dumps(solve(args.port, args.kind), indent=2, ensure_ascii=False))
    except ChallengePending as error:
        parser.exit(2, f"Desafio requer atenção manual ({error.kind}). {error}\n")
    except (URLError, TimeoutError, ValueError) as error:
        parser.exit(1, f"Falha no laboratório de CAPTCHA. {error}\n")


if __name__ == "__main__":
    main()
