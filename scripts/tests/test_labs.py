import os
import re
import tempfile
import unittest
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from unittest.mock import Mock, patch
from urllib.parse import urlencode

from captcha_lab import CaptchaRouter, ChallengePending, LocalLab, solve
from linkedin_lab import collect_page, make_job, route_files, route_local


class RouterTests(unittest.TestCase):
    def test_only_local_http_paths_and_no_redirects(self):
        lab = LocalLab(5470)
        with patch.object(lab.client, "open") as call:
            for path in ("https://www.linkedin.com/jobs", "//example.org/lab/linkedin", "/lab/../secrets", "/lab/%2e%2e"):
                with self.subTest(path=path), self.assertRaises(ValueError):
                    lab.request(path)
            call.assert_not_called()
        redirect = next(handler for handler in lab.client.handlers if hasattr(handler, "redirect_request"))
        self.assertIsNone(redirect.redirect_request(None, None, 302, "", {}, "https://www.linkedin.com"))

    def test_unknown_and_misdirected_challenges_never_post(self):
        lab = Mock()
        router = CaptchaRouter(lab)
        samples = (
            (b'<div data-captcha-kind="interactive"></div>', "interactive"),
            (b'<form data-captcha-kind="math" action="https://www.linkedin.com"><input name="challenge" value="id"></form>', "math"),
        )
        for document, kind in samples:
            with self.subTest(kind=kind), self.assertRaises(ChallengePending) as raised:
                router.resolve("/lab/captcha", document)
            self.assertEqual(raised.exception.kind, kind)
        lab.request.assert_not_called()

    def test_only_strict_arithmetic_is_evaluated(self):
        router = CaptchaRouter(Mock())
        template = b"""<form data-captcha-kind="math" action="/lab/captcha">
            <input name="challenge" value="00000000-0000-4000-8000-000000000000">
            <p data-captcha-expression>%s</p></form>"""
        with self.assertRaises(ChallengePending):
            router.resolve("/lab/captcha", template % b"1 + 2 * 3")
        router.lab.request.assert_not_called()

    def test_url_deduplication_keys_and_mismatched_ids(self):
        job = make_job("https://br.linkedin.com/jobs/view/backend-103?trk=demo", " Dev ", urn="urn:li:jobPosting:103")
        self.assertEqual(job["url"], "https://www.linkedin.com/jobs/view/103")
        for url in ("https://www.linkedin.com.evil.example/jobs/view/103", "https://www.linkedin.com@evil.example/jobs/view/103", "/in/someone"):
            self.assertIsNone(make_job(url, "Dev"))
        self.assertIsNone(make_job(job["url"], "Dev", urn="urn:li:jobPosting:104"))

    def test_local_files_merge_structured_data_and_card_fields_without_http(self):
        page = b"""
        <script type="application/ld+json">
        {"@type":"JobPosting","url":"/jobs/view/103","title":"Title from JSON-LD"}
        </script>
        <div class="base-search-card" data-entity-urn="urn:li:jobPosting:103">
          <h3 class="base-search-card__title">Duplicate title</h3>
          <h4 class="base-search-card__subtitle">Fake company</h4>
          <a class="base-card__full-link" href="/jobs/view/backend-103?trk=demo"></a>
        </div>
        <a rel="next" href="https://www.linkedin.com/jobs/search">Ignored offline</a>
        """
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "synthetic.html"
            path.write_bytes(page)
            with patch.object(LocalLab, "request") as request:
                result = route_files([path])
                request.assert_not_called()
        self.assertFalse(path.exists())
        self.assertEqual(result["pages"][0]["extracted"], {"jsonld": 1, "cards": 1})
        self.assertEqual([(job["title"], job["company"]) for job in result["jobs"]], [("Title from JSON-LD", "Fake company")])

    def test_empty_and_unrecognized_pages_are_distinct(self):
        self.assertEqual(collect_page(b"<main data-lab-empty></main>", {})[0], {"jsonld": 0, "cards": 0})
        with self.assertRaisesRegex(ValueError, "sem vagas reconhecíveis"):
            collect_page(b"<h1>Sign in</h1>", {})

    def test_pagination_loops_and_external_links_stop(self):
        html = b'<main data-lab-empty><a rel="next" href="%s">Next</a></main>'
        for following, expected in (
            (b"/lab/linkedin", "pagination_loop"),
            (b"https://www.linkedin.com/jobs", "invalid_pagination"),
        ):
            with patch.object(LocalLab, "request", return_value=(200, html % following)) as request:
                self.assertEqual(route_local(5470)["status"], expected)
                request.assert_called_once_with("/lab/linkedin")


@unittest.skipUnless(os.getenv("FAKEATS_LAB_TEST_PORT"), "Defina FAKEATS_LAB_TEST_PORT com o Fake ATS ativo")
class HttpTests(unittest.TestCase):
    port = int(os.getenv("FAKEATS_LAB_TEST_PORT", "0"))

    def test_solves_image_and_math_end_to_end(self):
        for kind in ("image", "math"):
            for _ in range(25):
                self.assertEqual(solve(self.port, kind)["jobs"][0]["id"], "lab-1")

    def test_paginated_collection_crosses_both_gates(self):
        result = route_local(self.port)
        self.assertEqual(result["status"], "ok")
        self.assertEqual([job["id"] for job in result["jobs"]], ["101", "102", "103"])
        self.assertEqual([page["captcha"] for page in result["pages"]], [None, "image", "math"])
        self.assertEqual(result["pages"][1]["extracted"], {"jsonld": 0, "cards": 2})

    def test_manual_fallback_preserves_already_collected_jobs(self):
        result = route_local(self.port, "/lab/linkedin/manual")
        self.assertEqual(result["status"], "needs_human")
        self.assertEqual(result["blocked"]["challenge"], "interactive")
        self.assertEqual([job["id"] for job in result["jobs"]], ["101"])

    def test_math_challenge_is_bound_to_its_route_and_single_use(self):
        lab = LocalLab(self.port)
        _, body = lab.request("/lab/linkedin/math")
        id_ = re.search(rb'name="challenge" value="([^"]+)"', body)[1].decode()
        left, right = re.search(rb'data-captcha-expression>([0-9]+) \+ ([0-9]+)', body).groups()
        payload = urlencode({"challenge": id_, "answer": int(left) + int(right)}).encode()
        self.assertEqual(lab.request("/lab/captcha", payload)[0], 400)
        with ThreadPoolExecutor(6) as executor:
            statuses = list(executor.map(lambda _: LocalLab(self.port).request("/lab/linkedin/math", payload)[0], range(6)))
        self.assertEqual(sorted(statuses), [200, 400, 400, 400, 400, 400])


if __name__ == "__main__":
    unittest.main()
