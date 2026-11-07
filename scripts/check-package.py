#!/usr/bin/env python3
"""Start the distributed Linux app on a disposable database at 5462. No email/application is sent.

The database must already exist (jo_release / jo / jo-test). --live-source is opt-in and
uses the real connector, worker and persistence in DiscoveryOnly; no candidate data is required.
"""
import argparse
import json
import os
from pathlib import Path
import socket
import subprocess
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, ProxyHandler, build_opener

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('package', type=Path)
parser.add_argument('--version', required=True)
parser.add_argument('--sha', required=True)
parser.add_argument('--live-source')
parser.add_argument('--evidence', type=Path)
options = parser.parse_args()
package = options.package.resolve()
metadata = json.loads((package / 'build-info.json').read_text())
assert metadata['version'] == options.version and metadata['source_sha'] == options.sha
assert options.version in subprocess.check_output([str(package / 'job-orchestrator'), '--version'], text=True)
with socket.socket() as check:
    assert check.connect_ex(('127.0.0.1', 7410)) != 0, 'Port 7410 is occupied; refusing to touch an existing app.'

client = build_opener(ProxyHandler({}))
base = 'http://127.0.0.1:7410'
def request(path, method='GET', data=None, status=200):
    encoded = None if data is None else json.dumps(data).encode()
    req = Request(base + path, data=encoded, method=method, headers={'Content-Type': 'application/json'})
    try:
        response = client.open(req, timeout=30)
    except HTTPError as error:
        response = error
    with response:
        body = response.read().decode()
        assert response.status == status, (path, response.status, body[:200])
        return json.loads(body) if response.headers.get_content_type() == 'application/json' else body

with tempfile.TemporaryDirectory(prefix='jo-package-check-') as folder:
    env = dict(os.environ, JO_DATA_DIR=folder + '/data', JO_CONFIG_DIR=folder + '/config',
        DOTNET_ENVIRONMENT='Testing', ASPNETCORE_ENVIRONMENT='Testing',
        ConnectionStrings__Default='Host=127.0.0.1;Port=5462;Username=jo;Password=jo-test;Database=jo_release',
        Llm__Disabled='true', Explorer__Enabled='false', Search__IntervalMinutes='1440',
        Seed__Sources__0='disabled:package-check', Logging__LogLevel__Default='Warning')
    log_path = Path(folder) / 'app.log'
    with log_path.open('w') as log:
        process = subprocess.Popen([str(package / 'job-orchestrator'), '--serve'], env=env, stdout=log, stderr=log)
        try:
            for _ in range(300):
                assert process.poll() is None, log_path.read_text()[-4000:]
                try:
                    health = request('/api/health')
                    break
                except (URLError, TimeoutError):
                    time.sleep(.2)
            else:
                raise AssertionError('App startup timed out: ' + log_path.read_text()[-4000:])
            assert health['version'] == options.version and health['sourceRevision'].startswith(options.sha), health
            assert '<app-root' in request('/'), 'Packaged Angular shell is missing'
            assert request('/api/applications') == [], 'Disposable database must have no applications'
            assert not request('/api/automation/linkedin-session')['settings']['enabled']
            diagnostic = json.loads(subprocess.check_output([str(package / 'job-orchestrator'), '--diagnose'], env=env, text=True))
            assert diagnostic['health']['version'] == options.version and diagnostic['sampledApplications'] == 0
            evidence = dict(version=health['version'], source_sha=options.sha, packaged_api=True, packaged_frontend=True,
                applications_sent=0, model='disabled', candidate='none', discovery=None)
            if options.live_source:
                campaign = request('/api/campaigns')[0]
                request('/api/campaigns/' + campaign['id'], 'PUT', dict(mode='DiscoveryOnly', minFitScore=0,
                    maxSubmissionsPerDay=0, technologies=[], titles=[], locations=[], sources=[options.live_source], night=False), status=204)
                request('/api/campaigns/' + campaign['id'] + '/runs', 'POST', {}, status=202)
                for _ in range(180):
                    jobs = request('/api/jobs')
                    if jobs:
                        break
                    time.sleep(1)
                assert jobs, 'The real discovery run did not persist a job; inspect connector/network availability.'
                first = request('/api/jobs/' + jobs[0]['id'])
                assert first['description'], 'Persisted source description is empty'
                assert request('/api/applications') == [], 'DiscoveryOnly must not create applications'
                evidence['discovery'] = dict(source=options.live_source, persisted_jobs=len(jobs), description_read=True,
                    worker_pipeline=True, authenticated_linkedin=False)
            request('/api/settings/wipe', 'POST', {'confirm': 'APAGAR TUDO'}, status=204)
            assert request('/api/jobs') == []
            evidence['wipe'] = True
            if options.evidence:
                options.evidence.write_text(json.dumps(evidence, indent=2) + '\n')
            print(json.dumps(evidence, indent=2), flush=True)
        finally:
            process.terminate()
            try:
                process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                process.kill(); process.wait()
