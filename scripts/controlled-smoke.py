"""HTTP integration with a disposable database. --external also exercises Cloudflare's public test credentials."""
import argparse
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, ProxyHandler, build_opener

args = argparse.ArgumentParser(description=__doc__)
args.add_argument('--external', action='store_true')
options = args.parse_args()
repo = Path(__file__).resolve().parents[1]
work = Path(os.environ['JO_TEST_TEMP'])
dotnet = os.environ.get('JO_DOTNET', 'dotnet')
client = build_opener(ProxyHandler({}))
processes = []
logs = []

def free_port():
    with socket.socket() as s:
        s.bind(('127.0.0.1', 0))
        return s.getsockname()[1]

def request(url, method='GET', data=None, status=200, form=False):
    encoded = None if data is None else (urlencode(data).encode() if form else json.dumps(data).encode())
    req = Request(url, data=encoded, method=method, headers={'Content-Type': 'application/x-www-form-urlencoded' if form else 'application/json'})
    try:
        response = client.open(req, timeout=40)
    except HTTPError as error:
        response = error
    with response:
        raw = response.read().decode()
        assert response.status == status, (method, url, response.status, raw[:1000])
        return json.loads(raw) if response.headers.get_content_type() == 'application/json' else raw

def start(relative, env, ready):
    assembly = repo / 'backend/src' / relative
    path = work / f'process-{len(logs)}.log'
    logs.append(path)
    handle = path.open('wb')
    p = subprocess.Popen([dotnet, str(assembly)], env=dict(os.environ, **env), stdout=handle, stderr=handle)
    processes.append((p, handle))
    for _ in range(160):
        if p.poll() is not None:
            raise AssertionError(path.read_text())
        try:
            request(ready)
            return
        except (URLError, TimeoutError):
            time.sleep(.2)
    raise AssertionError('Startup timed out: ' + path.read_text())

try:
    port = free_port()
    base = f'http://127.0.0.1:{port}/api'
    start('Bootstrappers/JobOrchestrator.Api/bin/Debug/net10.0/JobOrchestrator.Api.dll', {
        'ASPNETCORE_ENVIRONMENT': 'Testing', 'DOTNET_ENVIRONMENT': 'Testing', 'Urls': f'http://127.0.0.1:{port}',
        'ConnectionStrings__Default': 'Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test',
        'JO_DATA_DIR': str(work / 'app-data'), 'JO_CONFIG_DIR': str(work / 'app-config'), 'Llm__Disabled': 'true',
        'Profile__Seed': str(repo / 'config/profile.example.json'), 'Logging__LogLevel__Default': 'Warning'
    }, base + '/health')
    campaign = request(base + '/campaigns')[0]
    request(base + '/campaigns/' + campaign['id'], 'PUT', {
        'mode': 'DiscoveryOnly', 'minFitScore': 0, 'maxSubmissionsPerDay': 0,
        'technologies': ['.NET'], 'titles': ['Backend'], 'locations': ['Brasil', 'Remoto'], 'sources': []
    }, status=204)
    def capture(description='', title='Backend .NET'):
        return request(base + '/inbox/linkedin', 'POST', {
            'pageUrl': 'https://www.linkedin.com/jobs/search/?keywords=.NET', 'capturedAt': '2026-11-07T00:00:00Z',
            'jobs': [{'url': 'https://br.linkedin.com/jobs/view/backend-123?trk=test', 'title': title,
                      'company': 'Empresa Sintética', 'location': 'Remoto, Brasil', 'description': description}]
        })[0]
    first = capture()
    description = '.NET APIs. Trabalho remoto no Brasil. ' * 8 + '\nPara se candidatar, envie seu currículo para jobs@empresa.example'
    detail = capture(description)
    again = capture()
    assert first['jobId'] == detail['jobId'] == again['jobId']
    assert detail['updated'] and again['updated'] and detail['applyEmail'] == 'jobs@empresa.example'
    job = request(base + '/jobs/' + first['jobId'])
    assert job['job']['applyEmail'] == 'jobs@empresa.example' and 'envie seu currículo' in job['description']
    evidence = request(base + '/jobs/' + first['jobId'] + '/source-evidence')
    assert evidence['contact']['status'] == 'published_for_application' and evidence['contact']['evidence']
    coverage = request(base + '/jobs/linkedin-coverage')
    assert coverage[0]['jobs'] == 1 and coverage[0]['applyEmail'] == 1
    request(base + '/inbox/linkedin', 'POST', {'pageUrl': 'https://evil-linkedin.com/jobs/view/123', 'capturedAt': '2026-11-07T00:00:00Z', 'jobs': [{'url': 'https://www.linkedin.com/jobs/view/123', 'title': 'Dev'}]}, status=400)
    print('PASS: capture -> persisted job -> detail update without duplicate -> published-contact evidence -> coverage; spoof rejected', flush=True)
    session = request(base + '/automation/linkedin-session')
    assert not session['settings']['enabled']
    request(base + '/automation/linkedin-session/search', 'POST', {'query': '.NET'}, status=409)
    settings = {'enabled': False, 'location': 'Brasil', 'remoteOnly': True, 'maxJobs': 10, 'maxDetails': 5, 'intervalMinutes': 60, 'dailySearches': 12}
    request(base + '/automation/linkedin-session', 'PUT', settings)
    request(base + '/automation/controlled-tests/settings', 'PUT', {'enabled': True, 'origins': ['https://www.linkedin.com'], 'headless': True, 'lifetimeMinutes': 10}, status=400)
    print('PASS: session search disabled until configured; private-server policy refuses LinkedIn', flush=True)

    server = free_port()
    server_base = f'http://127.0.0.1:{server}'
    start('Tools/JobOrchestrator.FakeAts/bin/Debug/net10.0/JobOrchestrator.FakeAts.dll', {
        'FAKEATS_URL': server_base, 'FAKEATS_STATE': str(work / 'ats-state.json'), 'JO_CONTROLLED_TESTS': '1', 'JO_CONTROLLED_CAPTCHA': 'math',
        'Logging__LogLevel__Default': 'Warning'
    }, server_base + '/controlled-test')
    page = request(server_base + '/controlled-test')
    test_id = re.search(r'name="testId" value="([^"]+)"', page)[1]
    left, right = map(int, re.search(r'data-jo-captcha-expression>(\d+) \+ (\d+)', page).groups())
    values = {'testId': test_id, 'name': 'Synthetic test', 'email': 'candidate@example.test', 'answer': '-999'}
    assert 'data-jo-error' in request(server_base + '/controlled-test/submit', 'POST', values, status=400, form=True)
    values['answer'] = str(left + right)
    received = request(server_base + '/controlled-test/submit', 'POST', values, form=True)
    receipt = re.search(r'data-jo-receipt="([^"]+)"', received)[1]
    assert receipt in request(server_base + '/controlled-test/submit', 'POST', values, form=True)
    print('PASS: private server rejects wrong CAPTCHA, validates correct answer and returns one stable receipt on repeated submit', flush=True)

    if options.external:
        external_port = free_port()
        external_base = f'http://127.0.0.1:{external_port}'
        start('Tools/JobOrchestrator.FakeAts/bin/Debug/net10.0/JobOrchestrator.FakeAts.dll', {
            'FAKEATS_URL': external_base, 'FAKEATS_STATE': str(work / 'ats-external.json'), 'JO_CONTROLLED_TESTS': '1',
            'JO_CONTROLLED_CAPTCHA': 'turnstile', 'JO_CAPTCHA_TEST_KEYS': '1', 'Logging__LogLevel__Default': 'Warning'
        }, external_base + '/controlled-test')
        page = request(external_base + '/controlled-test')
        test_id = re.search(r'name="testId" value="([^"]+)"', page)[1]
        values = {'testId': test_id, 'name': 'Synthetic test', 'email': 'candidate@example.test'}
        request(external_base + '/controlled-test/submit', 'POST', values, status=400, form=True)
        values['cf-turnstile-response'] = 'XXXX.DUMMY.TOKEN.XXXX'
        assert 'data-jo-receipt' in request(external_base + '/controlled-test/submit', 'POST', values, form=True)
        print('PASS: actual Cloudflare Siteverify accepts official dummy token through the private server; missing token rejected', flush=True)
    request(base + '/settings/wipe', 'POST', {'confirm': 'APAGAR TUDO'}, status=204)
    assert request(base + '/jobs') == []
    assert not request(base + '/automation/linkedin-session')['settings']['enabled']
    assert not request(base + '/automation/controlled-tests/settings')['enabled']
    print('PASS: wipe removes captured data and disables/clears local browser sessions', flush=True)
    print('Browser interaction and an authenticated LinkedIn account are separate live checks; not claimed by this HTTP smoke.', flush=True)
finally:
    for p, handle in reversed(processes):
        p.terminate()
        try:
            p.wait(timeout=10)
        except subprocess.TimeoutExpired:
            p.kill(); p.wait()
        handle.close()
