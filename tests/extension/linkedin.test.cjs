const { test } = require('node:test');
const assert = require('node:assert/strict');
const { JSDOM } = require('jsdom');
const capture = require('../../extension/linkedin-capture.js');

test('detail keeps description, public contact and canonical identity', () => {
  const dom = new JSDOM(`<h1>Backend .NET</h1><div class="job-details-jobs-unified-top-card__company-name">Acme</div><div class="jobs-description-content__text">APIs em .NET.\nEnvie currículo: jobs@empresa.example</div>`);
  const page = capture(dom.window.document, 'https://br.linkedin.com/jobs/view/backend-123/?trk=mail');
  assert.equal(page.jobs[0].url, 'https://www.linkedin.com/jobs/view/123/');
  assert.equal(page.jobs[0].company, 'Acme');
  assert.match(page.jobs[0].description, /jobs@empresa.example/);
  assert.equal(page.pageUrl.includes('trk='), false);
});
test('structured detail supplements cards without losing fields or duplicating', () => {
  const dom = new JSDOM(`<script type="application/ld+json">{"@type":"JobPosting","url":"https://www.linkedin.com/jobs/view/123","title":"Developer","description":"<p>APIs</p><p>Envie CV: jobs@empresa.example</p>","hiringOrganization":{"name":"Acme"},"baseSalary":{"currency":"BRL","value":{"minValue":6000,"maxValue":8000,"unitText":"MONTH"}}}</script><div class="base-search-card"><a class="base-card__full-link" href="/jobs/view/backend-123?trk=x"></a><h3 class="base-search-card__title">Duplicate</h3><span class="job-search-card__location">Brasil</span></div>`);
  const jobs = capture(dom.window.document, 'https://www.linkedin.com/jobs/search/?keywords=NET').jobs;
  assert.equal(jobs.length, 1); assert.equal(jobs[0].location, 'Brasil'); assert.match(jobs[0].description, /Envie CV/); assert.match(jobs[0].salary, /6000/);
});
test('changed card classes still capture an actual job link, and never invent a description', () => {
  const dom = new JSDOM(`<li><a href="https://www.linkedin.com/jobs/view/456"><strong>Developer</strong></a></li>`);
  const job = capture(dom.window.document, 'https://www.linkedin.com/jobs/search/').jobs[0];
  assert.equal(job.url, 'https://www.linkedin.com/jobs/view/456/'); assert.equal(job.description, '');
});
test('spoofed hosts cannot be captured', () => {
  const dom = new JSDOM('<h1>Developer</h1>');
  assert.throws(() => capture(dom.window.document, 'https://evil-linkedin.com/jobs/view/123'));
  assert.throws(() => capture(dom.window.document, 'https://linkedin.com:8443/jobs/view/123'));
});
test('caps one capture at twenty job records', () => {
  const dom = new JSDOM(Array.from({ length: 30 }, (_, i) => `<li><a href="https://www.linkedin.com/jobs/view/${i+1}">Developer ${i}</a></li>`).join(''));
  assert.equal(capture(dom.window.document, 'https://www.linkedin.com/jobs/search/').jobs.length, 20);
});
