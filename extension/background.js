// Fala com o Job Orchestrator local (127.0.0.1:7410). A página da vaga nunca fala com o app: só esta extensão, com a permissão do host.
// O app só aceita os pedidos desta extensão: o id dela é fixo (a chave no manifest), e o navegador manda esse id em cada pedido.
const API = 'http://127.0.0.1:7410';

// Every request says it comes from this extension: Chrome leaves the Origin out of an extension's GET, and the app checks this header.
async function api(path, init = {}) {
  try { return await fetch(`${API}${path}`, { ...init, headers: { ...(init.headers || {}), 'X-Jo-Copilot': chrome.runtime.id } }); } catch { return null; }
}

// Sinal de vida: o app mostra "Copiloto conectado" enquanto ele chega.
async function hello() {
  await api('/api/copilot/hello', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ version: chrome.runtime.getManifest().version }) });
}
chrome.runtime.onInstalled.addListener(() => { chrome.alarms.create('hello', { periodInMinutes: 1 }); hello(); });
chrome.runtime.onStartup.addListener(() => { chrome.alarms.create('hello', { periodInMinutes: 1 }); hello(); });
chrome.alarms.onAlarm.addListener((a) => { if (a.name === 'hello') hello(); });

// As abas abertas pelo app, com a candidatura de cada uma. Fica na sessão do navegador: o service worker dorme e acorda.
async function trackedId(tabId) { return (await chrome.storage.session.get(`tab:${tabId}`))[`tab:${tabId}`] ?? null; }
async function track(tabId, id) { await chrome.storage.session.set({ [`tab:${tabId}`]: id }); }
async function untrack(tabId) { await chrome.storage.session.remove(`tab:${tabId}`); }
chrome.tabs.onRemoved.addListener((tabId) => { untrack(tabId); });

async function resumeOf(id, company) {
  const r = await api(`/api/applications/${id}/resume`);
  if (!r?.ok) return null;
  const bytes = new Uint8Array(await r.arrayBuffer());
  let binary = '';
  for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  return { name: `curriculo-${(company || 'vaga').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]+/g, '-')}.pdf`, data: btoa(binary) };
}

// Preenche a aba com tudo o que o app preparou para a candidatura e fica de olho na página de confirmação.
async function run(tabId, id, navigated = false) {
  const r = await api(`/api/copilot/fill/${id}`);
  if (!r?.ok) return 0;
  const data = await r.json();
  const resume = data.hasResume ? await resumeOf(id, data.company) : null;
  const values = { ...data.answers, first_name: data.firstName, last_name: data.lastName, email: data.email, phone: data.phone, linkedin_url: data.linkedIn };
  const extra = { questions: data.questions, prepared: data.prepared, applicationId: id, company: data.company, title: data.title, watch: true, navigated };
  try {
    await chrome.scripting.executeScript({ target: { tabId, allFrames: true }, files: ['fill.js'] });
    const results = await chrome.scripting.executeScript({ target: { tabId, allFrames: true }, func: (v, res, x) => window.__joFill?.(v, res, x) ?? 0, args: [values, resume, extra] });
    return results.reduce((n, x) => n + (x.result || 0), 0);
  } catch { return 0; }
}

// Uma página carregou: se ela é a vaga que o app acabou de abrir (ou a continuação dela na mesma aba), preenche sozinha.
chrome.tabs.onUpdated.addListener(async (tabId, info, tab) => {
  const url = tab.url || '';
  if (info.status !== 'complete' || !/^https?:/i.test(url) || url.startsWith(API)) return;
  let id = await trackedId(tabId);
  // A page after the first in a tab the app opened: the next step of the form, or the confirmation after you sent it.
  const navigated = !!id;
  if (!id) {
    const r = await api(`/api/copilot/expected?url=${encodeURIComponent(url)}`);
    if (!r?.ok) return;
    id = (await r.json()).applicationId;
    await track(tabId, id);
  }
  await run(tabId, id, navigated);
});

async function confirmed(tabId, msg) {
  const r = await api('/api/copilot/sent', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ applicationId: msg.applicationId, url: msg.url, text: msg.text }) });
  await untrack(tabId);
  // Registrada: a aba fecha sozinha depois de alguns segundos, para você ver a confirmação.
  if (r?.ok) setTimeout(() => chrome.tabs.remove(tabId).catch(() => {}), 4000);
  return !!r?.ok;
}

chrome.runtime.onMessage.addListener((msg, sender, reply) => {
  (async () => {
    try {
      if (msg.type === 'status') {
        const r = await api('/api/health');
        reply({ up: !!r?.ok });
      } else if (msg.type === 'capture-linkedin') {
        const r = await api('/api/inbox/linkedin', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(msg.page) });
        const body = r ? await r.json() : {};
        reply(r?.ok ? { ok: true, jobs: body } : { ok: false, error: body.detail || body.title || 'Não foi possível salvar as vagas.' });
      } else if (msg.type === 'match') {
        const r = await api(`/api/copilot/match?url=${encodeURIComponent(msg.url)}`);
        if (r?.ok) { reply({ found: true, ...(await r.json()) }); return; }
        // Sem candidatura desta página: só os dados pessoais e as respostas gerais do perfil.
        const a = await api('/api/profile/answers');
        reply({ found: false, answers: a?.ok ? await a.json() : {} });
      } else if (msg.type === 'resume') {
        reply(await resumeOf(msg.id, msg.company));
      } else if (msg.type === 'fill-tab') {
        await track(msg.tabId, msg.id);
        reply({ filled: await run(msg.tabId, msg.id) });
      } else if (msg.type === 'confirmed' && sender.tab?.id) {
        reply({ ok: await confirmed(sender.tab.id, msg) });
      } else if (msg.type === 'answer') {
        // The fields left empty on the page: the app's AI answers them from your profile (it can take a minute).
        const r = await api(`/api/copilot/answer/${msg.applicationId}`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ fields: msg.fields }) });
        reply(r?.ok ? await r.json() : []);
      } else if (msg.type === 'declare') {
        const r = await api(`/api/copilot/declare/${msg.applicationId}`, { method: 'POST' });
        if (r?.ok && sender.tab?.id) await untrack(sender.tab.id);
        reply({ ok: !!r?.ok });
      }
    } catch {
      reply(null);
    }
  })();
  return true;
});

// The app's own page (and only it) asks this extension to open the jobs that go by the site, each in a tab of its own: a browser
// lets a page open one window per click, the extension opens as many as you asked.
chrome.runtime.onMessageExternal.addListener((msg, sender, reply) => {
  if (!(sender.url || '').startsWith(`${API}/`)) { reply(null); return false; }
  (async () => {
    if (msg.type === 'ping') { reply({ ok: true, version: chrome.runtime.getManifest().version }); return; }
    if (msg.type === 'open') {
      let opened = 0;
      for (const item of (msg.items || []).slice(0, 10)) {
        const r = await api(`/api/copilot/open/${item.id}`, { method: 'POST' });
        if (!r?.ok) continue;
        const tab = await chrome.tabs.create({ url: (await r.json()).url, active: opened === 0 });
        await track(tab.id, item.id);
        opened++;
      }
      reply({ opened });
    }
  })();
  return true;
});

