const $ = (id) => document.getElementById(id);
let data = null;
let tab = null;
let captured = null;

(async () => {
  [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  const url = tab?.url ?? '';
  const status = await chrome.runtime.sendMessage({ type: 'status' });
  if (!status?.up) { $('state').textContent = 'O app não está no ar. Abra o Job Orchestrator.'; return; }
  $('state').textContent = 'No ar';
  if (/^https:\/\/([^/]+\.)?linkedin\.com\//i.test(url)) { $('blocked').hidden = false; $('fill').hidden = true; document.querySelector('.note').hidden = true; return; }
  data = await chrome.runtime.sendMessage({ type: 'match', url });
  if (data?.found) { $('job').hidden = false; $('title').textContent = data.title; $('company').textContent = data.company; }
  else { $('none').hidden = false; }
  $('fill').disabled = !/^https?:/i.test(url);
})();

$('capture').addEventListener('click', async () => {
  try {
    $('capture').disabled = true;
    await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ['linkedin-capture.js'] });
    const [result] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, func: () => globalThis.__joCaptureLinkedIn() });
    captured = result.result;
    const list = $('capture-list'); list.replaceChildren();
    for (const [index, job] of captured.jobs.entries()) {
      const label = document.createElement('label'); const input = document.createElement('input'); input.type = 'checkbox'; input.checked = true; input.value = String(index);
      const span = document.createElement('span'); span.textContent = `${job.title} · ${job.company || 'empresa não capturada'}${job.description ? ' · com descrição' : ' · só cartão'}`;
      label.append(input, span); list.append(label);
    }
    $('capture-note').textContent = captured.jobs.length ? 'Confira as vagas selecionadas. Somente contatos publicados na descrição para candidatura serão considerados.' : 'Nenhuma vaga reconhecida. Abra os detalhes de uma vaga e tente novamente.';
    $('capture-preview').hidden = false; $('capture-save').disabled = !captured.jobs.length;
  } catch (error) { $('result').hidden = false; $('result').textContent = error.message; }
  finally { $('capture').disabled = false; }
});
$('capture-save').addEventListener('click', async () => {
  try {
    $('capture-save').disabled = true;
    const jobs = [...$('capture-list').querySelectorAll('input:checked')].map(x => captured.jobs[Number(x.value)]);
    if (!jobs.length) throw new Error('Selecione ao menos uma vaga.');
    const result = await chrome.runtime.sendMessage({ type: 'capture-linkedin', page: { ...captured, jobs } });
    if (!result?.ok) throw new Error(result?.error || 'O app não respondeu.');
    $('result').hidden = false;
    $('result').textContent = `${result.jobs.length} vaga(s) salva(s); ${result.jobs.filter(j => j.updated).length} atualizada(s); ${result.jobs.filter(j => j.applyEmail).length} com contato publicado para candidatura.`;
  } catch (error) { $('result').hidden = false; $('result').textContent = error.message; }
  finally { $('capture-save').disabled = false; }
});

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
