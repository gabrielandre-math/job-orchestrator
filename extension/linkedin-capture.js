// Runs only after the user clicks Capture. Reads the current document; no navigation, requests, cookies or profile collection.
(() => {
  function capture(doc, href) {
    const page = new URL(href);
    if (page.protocol !== 'https:' || page.port || !(page.hostname === 'linkedin.com' || page.hostname.endsWith('.linkedin.com'))) throw new Error('Abra o LinkedIn em HTTPS.');
    const clean = (s, max = 20000) => String(s || '').replace(/\r/g, '').replace(/[\t ]+/g, ' ').replace(/\n{3,}/g, '\n\n').trim().slice(0, max);
    const read = (root, selectors, max = 20000) => {
      for (const selector of selectors) {
        const el = root.querySelector(selector);
        if (el) { const value = clean(el.innerText || el.textContent, max); if (value) return value; }
      }
      return '';
    };
    const canonical = (raw) => {
      try {
        const u = new URL(raw, href);
        if (u.protocol !== 'https:' || u.port || u.username || u.password || !(u.hostname === 'linkedin.com' || u.hostname.endsWith('.linkedin.com'))) return null;
        const id = u.pathname.match(/^\/jobs\/view\/(?:[^/]+-)?(\d{1,24})\/?$/)?.[1];
        return id ? `https://www.linkedin.com/jobs/view/${id}/` : null;
      } catch { return null; }
    };
    const htmlText = (s) => {
      const el = doc.createElement('textarea');
      el.innerHTML = String(s || '').replace(/<(script|style|template)\b[^>]*>[\s\S]*?<\/\1>/gi, '').replace(/<\/(?:p|div|li)>|<br\s*\/?\s*>/gi, '\n').replace(/<[^>]+>/g, '');
      return clean(el.value || el.textContent);
    };
    const records = new Map();
    function add(job) {
      const url = canonical(job.url); const title = clean(job.title, 300);
      if (!url || !title) return;
      const old = records.get(url) || {};
      const next = { url, title, company: clean(job.company, 200), location: clean(job.location, 200), description: clean(job.description), salary: clean(job.salary, 120), postedAt: clean(job.postedAt, 120), externalApplyUrl: job.externalApplyUrl || '', closed: Boolean(job.closed) };
      for (const key of Object.keys(next)) if (!next[key] && old[key]) next[key] = old[key];
      records.set(url, next);
    }
    for (const script of doc.querySelectorAll('script[type="application/ld+json"]')) {
      try {
        const pending = [JSON.parse(script.textContent)]; let scanned = 0;
        while (pending.length && scanned++ < 1000) {
          const node = pending.pop();
          if (Array.isArray(node)) { pending.push(...node); continue; }
          if (!node || typeof node !== 'object') continue;
          const types = Array.isArray(node['@type']) ? node['@type'] : [node['@type']];
          if (types.some(t => t === 'JobPosting' || t === 'https://schema.org/JobPosting')) {
            const loc = (Array.isArray(node.jobLocation) ? node.jobLocation[0] : node.jobLocation)?.address || {};
            const sal = node.baseSalary?.value;
            add({ url: node.url || canonical(href), title: node.title, company: node.hiringOrganization?.name,
              location: [loc.addressLocality, loc.addressRegion, typeof loc.addressCountry === 'string' ? loc.addressCountry : loc.addressCountry?.name].filter(Boolean).join(', '),
              description: htmlText(node.description), postedAt: node.datePosted,
              salary: sal && typeof sal === 'object' ? [node.baseSalary.currency, sal.minValue || sal.value, sal.maxValue ? `– ${sal.maxValue}` : '', sal.unitText].filter(Boolean).join(' ') : '' });
          }
          for (const key of ['@graph', 'itemListElement', 'item']) if (node[key]) pending.push(node[key]);
        }
      } catch { /* A bad JSON-LD block does not hide the visible cards. */ }
    }
    for (const card of doc.querySelectorAll('.job-card-container, .base-search-card, .job-card-list')) {
      const link = card.querySelector('a[href*="/jobs/view/"]'); if (!link) continue;
      add({ url: link.href || link.getAttribute('href'), title: read(card, ['.job-card-list__title--link strong', '.job-card-list__title', '.base-search-card__title', 'a[href*="/jobs/view/"]'], 300),
        company: read(card, ['.artdeco-entity-lockup__subtitle', '.job-card-container__primary-description', '.base-search-card__subtitle'], 200),
        location: read(card, ['.job-card-container__metadata-wrapper', '.job-search-card__location', '.artdeco-entity-lockup__caption'], 200) });
    }
    // Authenticated layouts change frequently; retain readable job links even if a card class has changed.
    for (const link of doc.querySelectorAll('a[href*="/jobs/view/"]')) {
      const url = canonical(link.href || link.getAttribute('href')); if (!url || records.has(url)) continue;
      const card = link.closest('[data-job-id], li, article'); if (!card) continue;
      add({ url, title: read(card, ['a[href*="/jobs/view/"] strong', 'a[href*="/jobs/view/"] h3', 'a[href*="/jobs/view/"]'], 300),
        company: read(card, ['.artdeco-entity-lockup__subtitle', '.job-card-container__primary-description'], 200),
        location: read(card, ['.artdeco-entity-lockup__caption', '.job-card-container__metadata-wrapper'], 200) });
    }
    const detailUrl = canonical(href) || (page.searchParams.get('currentJobId')?.match(/^\d{1,24}$/) ? `https://www.linkedin.com/jobs/view/${page.searchParams.get('currentJobId')}/` : null);
    if (detailUrl) {
      const title = read(doc, ['.job-details-jobs-unified-top-card__job-title h1', '.jobs-unified-top-card__job-title', '.top-card-layout__title', 'h1'], 300);
      const existing = records.get(detailUrl) || {};
      const apply = doc.querySelector('a[data-tracking-control-name*="apply-link-offsite"], a.jobs-apply-button, .jobs-apply-button a[href]');
      let externalApplyUrl = existing.externalApplyUrl || '';
      try { const u = new URL(apply?.getAttribute('href'), href); if (u.protocol === 'https:' && !u.username && !(u.hostname === 'linkedin.com' || u.hostname.endsWith('.linkedin.com'))) externalApplyUrl = u.href.slice(0, 1000); } catch { }
      const closed = /no longer accepting|n[aã]o aceita mais|vaga encerrada/i.test(read(doc, ['.artdeco-inline-feedback__message', '.topcard__flavor--closed'], 500));
      if (title) add({ ...existing, url: detailUrl, title, externalApplyUrl, closed,
        company: read(doc, ['.job-details-jobs-unified-top-card__company-name', '.jobs-unified-top-card__company-name', '.topcard__org-name-link'], 200) || existing.company,
        location: read(doc, ['.job-details-jobs-unified-top-card__primary-description-container .tvm__text', '.jobs-unified-top-card__bullet', '.topcard__flavor--bullet'], 200) || existing.location,
        description: read(doc, ['.jobs-description-content__text', '.jobs-description__content', '.description__text .show-more-less-html__markup', '.show-more-less-html__markup']) || existing.description });
    }
    const jobs = [...records.values()].sort((a, b) => Number(b.url === detailUrl) - Number(a.url === detailUrl)).slice(0, 20);
    // The current tab URL is kept as provenance; tracking parameters are not needed.
    for (const key of [...page.searchParams.keys()]) if (key !== 'currentJobId' && key !== 'keywords' && key !== 'location') page.searchParams.delete(key);
    return { pageUrl: page.href, capturedAt: new Date().toISOString(), jobs };
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = capture;
  else globalThis.__joCaptureLinkedIn = () => capture(document, location.href);
})();
