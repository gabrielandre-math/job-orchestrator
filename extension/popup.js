const $ = (id) => document.getElementById(id);
let data = null;
let tab = null;

(async () => {
  [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  const url = tab?.url ?? '';
  if (/^https?:\/\/([^/]+\.)?linkedin\.com\//i.test(url)) { $('state').textContent = 'No ar'; $('blocked').hidden = false; return; }
  const status = await chrome.runtime.sendMessage({ type: 'status' });
  if (!status?.up) { $('state').textContent = 'O app não está no ar. Abra o Job Orchestrator.'; return; }
  $('state').textContent = 'No ar';
  data = await chrome.runtime.sendMessage({ type: 'match', url });
  if (data?.found) { $('job').hidden = false; $('title').textContent = data.title; $('company').textContent = data.company; }
  else { $('none').hidden = false; }
  $('fill').disabled = !/^https?:/i.test(url);
})();

$('fill').addEventListener('click', async () => {
  $('fill').disabled = true;
  let resume = null;
  if (data?.found && data.copilot?.hasResume) resume = await chrome.runtime.sendMessage({ type: 'resume', id: data.applicationId, name: `curriculo-${(data.company || 'vaga').toLowerCase().replace(/[^a-z0-9]+/g, '-')}.pdf` });
  const values = {};
  if (data?.found) {
    for (const a of data.copilot.answers ?? []) values[a.key] = a.value;
    Object.assign(values, { first_name: data.copilot.firstName, last_name: data.copilot.lastName, email: data.copilot.email, phone: data.copilot.phone, linkedin_url: data.copilot.linkedIn });
  } else Object.assign(values, data?.answers ?? {});
  await chrome.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, files: ['fill.js'] });
  const results = await chrome.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, func: (v, r) => window.__joFill?.(v, r) ?? 0, args: [values, resume] });
  const filled = results.reduce((n, x) => n + (x.result || 0), 0);
  $('result').hidden = false;
  $('result').textContent = filled > 0 ? `${filled} ${filled === 1 ? 'campo preenchido' : 'campos preenchidos'}${resume ? ', com o currículo anexado' : ''}. Confira e envie você mesmo.` : 'Nenhum campo vazio reconhecido nesta página.';
  $('fill').disabled = false;
});
