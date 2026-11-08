// Preenchimento do formulário com o que o Job Orchestrator preparou: os seus dados, as respostas desta vaga e o currículo dela.
// Só campos vazios; nunca clica em enviar nem em avançar. Da página só sai um aviso: o de que a confirmação apareceu.
(() => {
  if (window.__joFill) return;

  const RULES = [
    ['full_name', /nome completo|full name|your name|seu nome/i],
    ['last_name', /sobrenome|[úu]ltimo nome|last name|family name|surname/i],
    ['first_name', /primeiro nome|first name|given name|^\s*nome\b|\bname\b/i],
    ['email', /e-?mail/i],
    ['phone', /telefone|celular|whats ?app|phone|mobile/i],
    ['linkedin_url', /linkedin/i],
    ['github_url', /github/i],
    ['portfolio_url', /portf[oó]lio|website|site pessoal|personal (site|website)|blog/i],
    ['salary_expectation', /pretens[aã]o|expectativa salarial|sal[aá]rio|salary|compensation/i],
    ['start_date', /disponibilidade|quando (voc[eê] )?(pode )?come[cç]ar|data de in[ií]cio|start date|notice period|availability/i],
    ['english_level', /ingl[eê]s|english/i],
    ['work_authorization', /autoriza[cç][aã]o (para|de) trabalh|authori[sz]ed to work|work authori[sz]ation|right to work|legally (authori|able)/i],
    ['needs_sponsorship', /patroc[ií]nio|sponsorship|\bvisa\b/i],
    ['relocation', /mudar de cidade|realoca|reloca/i],
    ['work_modality', /modalidade|work (model|arrangement)|remote or/i],
    ['how_heard', /como (voc[eê] )?(soube|conheceu|ficou sabendo)|how did you (hear|find)|where did you hear/i],
    ['location', /cidade|localiza[cç][aã]o|onde (voc[eê] )?mora|\bcity\b|location|address|endere[cç]o/i],
    ['education', /forma[cç][aã]o|escolaridade|education|degree|universidade|university/i],
    ['self_id', /g[eê]nero|ra[cç]a|etnia|\bcor\b|defici[eê]ncia|orienta[cç][aã]o sexual|gender|race|ethnicity|disability|veteran|pronoun/i],
  ];
  // Campos que não são da pessoa (empresa atual, referência, contato de emergência) ou que nunca se preenchem sozinhos.
  const AVOID = /empresa atual|current (company|employer)|refer[eê]ncia|reference|emerg[eê]ncia|emergency|captcha|senha|password|c[oó]digo|verification/i;
  const RESUME = /curr[ií]culo|\bcv\b|resume|anexo|attach/i;
  // O mesmo texto de confirmação que o app reconhece quando ele mesmo envia.
  const CONFIRM = /thank(s| you)( so much)? for (applying|your (application|interest))|application (has been |was )?(received|submitted|sent|completed?)|we('ve| have) received your application|successfully (applied|submitted)|obrigad[oa] (por (se candidatar|sua candidatura|seu interesse|participar))|candidatura (foi )?(recebida|enviada|realizada|conclu[ií]da)|recebemos (a |sua )?candidatura|inscri[cç][aã]o (foi )?(realizada|conclu[ií]da|enviada)|gracias por (postularte|tu postulaci[oó]n|aplicar)|postulaci[oó]n (enviada|recibida)/i;
  const SEND = /enviar|submit|apply|candidat|finalizar|concluir|send|postular/i;
  // The button that opens the form on a job's description page (never a send button).
  const OPEN = /^\s*(apply( now| for this job)?|candidat(ar|e)-se|quero me candidatar|aplicar|inscrever-se|i'?m interested|postularme)\s*$/i;

  const fold = (s) => (s || '').toString().normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/[^a-z0-9 ]+/g, ' ').replace(/\s+/g, ' ').trim();

  function labelOf(el) {
    const parts = [];
    if (el.id) { const l = document.querySelector(`label[for="${CSS.escape(el.id)}"]`); if (l) parts.push(l.innerText); }
    const wrap = el.closest('label'); if (wrap) parts.push(wrap.innerText);
    (el.getAttribute('aria-labelledby') || '').split(/\s+/).forEach((id) => { const n = id && document.getElementById(id); if (n) parts.push(n.innerText); });
    parts.push(el.getAttribute('aria-label') || '', el.getAttribute('placeholder') || '', el.name || '', el.id || '');
    const box = el.closest('.field, .form-group, .application-question, fieldset, [class*="question"], [class*="field"]');
    if (box) { const l = box.querySelector('label, legend, .label, [class*="label"]'); if (l && l !== wrap) parts.push(l.innerText); }
    return parts.join(' ').replace(/[\s*:]+/g, ' ').trim();
  }

  function keyFor(label) {
    if (!label || AVOID.test(label)) return null;
    for (const [key, re] of RULES) if (re.test(label)) return key;
    return null;
  }

  // What was prepared for this job, question by question: what the robot filled on this form, then the AI's answers and yours.
  let prepared = [];
  function answerFor(label) {
    const l = fold(label);
    if (l.length < 2 || AVOID.test(label)) return null;
    let near = null;
    for (const q of prepared) {
      const f = fold(q.question);
      if (!f || !q.answer) continue;
      if (f === l) return q.answer;
      if (!near && ((f.length >= 12 && l.includes(f)) || (l.length >= 12 && f.includes(l)))) near = q.answer;
    }
    return near;
  }
  const valueFor = (v, label) => { const key = keyFor(label); return (key && v[key]) || answerFor(label); };

  function setValue(el, value) {
    const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.dispatchEvent(new Event('blur', { bubbles: true }));
  }

  function mark(el) { el.style.outline = '2px solid #ff90ac'; el.style.outlineOffset = '2px'; el.dataset.joFilled = '1'; delete el.dataset.joMissing; }
  // Yours to answer: outlined in yellow, and still open to a later pass (a step that brings the answer, or you).
  function flag(el) { el.style.outline = '2px solid #e6b450'; el.style.outlineOffset = '2px'; el.dataset.joMissing = '1'; }

  function pickOption(select, value) {
    const v = fold(value);
    const options = [...select.options].filter((o) => o.value !== '' && !o.disabled);
    return options.find((o) => fold(o.text) === v) || options.find((o) => fold(o.text).startsWith(v) || v.startsWith(fold(o.text))) || options.find((o) => fold(o.text).includes(v));
  }

  const visible = (el) => !el.disabled && !el.readOnly && el.type !== 'hidden' && el.offsetParent !== null;

  function fillAll(v, resume) {
    let count = 0;
    const fields = [...document.querySelectorAll('input, textarea, select')].filter(visible);
    const radios = new Map();
    const boxes = new Map();
    for (const el of fields) {
      const type = (el.type || '').toLowerCase();
      if (type === 'radio') { if (el.name) radios.set(el.name, [...(radios.get(el.name) || []), el]); continue; }
      if (type === 'checkbox') {
        // A group of boxes under one legend is one question ("Como você conheceu a empresa?"); a box alone is a declaration and stays with you.
        const fs = el.closest('fieldset');
        if (fs && fs.querySelectorAll('input[type=checkbox]').length > 1) boxes.set(fs, [...(boxes.get(fs) || []), el]);
        continue;
      }
      if (type === 'submit' || type === 'button' || type === 'password') continue;
      const label = labelOf(el);
      if (type === 'file') {
        if (resume && !window.__joResumeDone && !el.files?.length && (RESUME.test(label) || /pdf/i.test(el.accept || ''))) {
          const bytes = Uint8Array.from(atob(resume.data), (c) => c.charCodeAt(0));
          const dt = new DataTransfer();
          dt.items.add(new File([bytes], resume.name, { type: 'application/pdf' }));
          el.files = dt.files;
          el.dispatchEvent(new Event('input', { bubbles: true }));
          el.dispatchEvent(new Event('change', { bubbles: true }));
          window.__joResumeDone = true;
          mark(el.parentElement || el);
          count++;
        }
        continue;
      }
      if (el.dataset.joFilled) continue;
      const value = valueFor(v, label);
      if (!value) continue;
      if (el instanceof HTMLSelectElement) {
        if (el.value && el.selectedIndex > 0) continue;
        const option = pickOption(el, value);
        if (!option) continue;
        el.value = option.value;
        el.dispatchEvent(new Event('change', { bubbles: true }));
      } else {
        if (el.value) continue;
        setValue(el, value);
      }
      mark(el);
      count++;
    }
    // Escolhas (sim ou não, nível de inglês): marca a opção com o mesmo texto da resposta, se nenhuma estiver marcada.
    for (const group of radios.values()) {
      if (group.some((r) => r.checked)) continue;
      const value = valueFor(v, labelOf(group[0].closest('fieldset, [role=radiogroup]') || group[0]));
      if (!value) continue;
      const want = fold(value);
      const target = group.find((r) => fold(labelOf(r)).split(' ').includes(want) || fold(r.value) === want || fold(labelOf(r)) === want);
      if (!target) continue;
      target.click();
      mark(target.parentElement || target);
      count++;
    }
    for (const [fs, group] of boxes) {
      if (group.some((b) => b.checked)) continue;
      const value = valueFor(v, (fs.querySelector('legend')?.innerText || labelOf(fs)));
      if (!value) continue;
      const answer = fold(value);
      for (const b of group) {
        const text = fold(document.querySelector(`label[for="${CSS.escape(b.id)}"]`)?.innerText || b.value);
        if (text && answer.includes(text)) { b.click(); mark(b.parentElement || b); count++; }
      }
    }
    return count;
  }

  // Required fields still empty after the fill: outlined in yellow, so you see at once what is yours to answer.
  function missing() {
    let n = 0;
    for (const el of document.querySelectorAll('input, textarea, select')) {
      if (!visible(el) || el.dataset.joFilled) continue;
      const required = el.required || el.getAttribute('aria-required') === 'true';
      const type = (el.type || '').toLowerCase();
      if (!required || type === 'checkbox' || type === 'radio' || type === 'file' && el.files?.length || el.value) continue;
      flag(el);
      n++;
    }
    return n;
  }

  const confirmation = () => (document.body?.innerText || '').match(CONFIRM)?.[0] ?? null;
  const formless = () => [...document.querySelectorAll('input, textarea, select')].filter((el) => visible(el) && el.type !== 'search').length < 3;

  let context = {};
  function report() {
    if (window.__joReported) return;
    window.__joReported = true;
    chrome.runtime.sendMessage({ type: 'confirmed', applicationId: context.applicationId, url: location.href, text: confirmation() || '' }, (r) => {
      if (window.top === window) banner(r?.ok ? 'Enviada e registrada no Job Orchestrator. Esta aba fecha sozinha em alguns segundos.' : 'A confirmação apareceu, mas o app não respondeu. Marque como enviada no Job Orchestrator.', false);
    });
  }

  // After you click send, the confirmation page marks the application as sent. A confirmation text that was already on the page
  // before (an instruction, a template) does not count; a new page that only shows the confirmation does.
  function watch(v, resume) {
    if (window.__joWatching) return;
    window.__joWatching = true;
    let armed = false;
    let timer = null;
    const started = Date.now();
    const before = confirmation();
    document.addEventListener('submit', () => { armed = true; }, true);
    document.addEventListener('click', (e) => {
      const b = e.target.closest?.('button, input[type=submit], [role=button], a');
      if (b && SEND.test(`${b.innerText || b.value || ''} ${b.getAttribute('aria-label') || ''}`)) armed = true;
    }, true);
    const observer = new MutationObserver(() => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        if (Date.now() - started > 30 * 60 * 1000) { observer.disconnect(); return; }
        if (armed && confirmation() && (!before || formless())) { observer.disconnect(); report(); return; }
        // A new step of the form: fill what appeared.
        const n = fillAll(v, resume);
        if (n > 0 && window.top === window) banner(`${n} ${n === 1 ? 'campo preenchido' : 'campos preenchidos'} nesta etapa. Confira e siga.`, true);
      }, 700);
    });
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  }

  window.__joFill = (values, resume, extra = {}) => {
    context = extra;
    prepared = [...(extra.prepared || []), ...(extra.questions || [])];
    const v = { ...values };
    if (!v.full_name && (v.first_name || v.last_name)) v.full_name = `${v.first_name || ''} ${v.last_name || ''}`.trim();
    if (extra.watch && extra.navigated && formless() && confirmation()) { report(); return 0; }
    const count = fillAll(v, resume);
    const left = missing();
    if (extra.watch) watch(v, resume);
    // The description page of the job, before the form: point at the button that opens it; the fill comes when the form appears.
    if (window.top === window && count === 0 && formless()) {
      const open = [...document.querySelectorAll('a, button, [role=button]')].find((b) => b.offsetParent && OPEN.test(b.innerText || b.getAttribute('aria-label') || ''));
      if (open) { open.style.outline = '3px solid #ff90ac'; open.style.outlineOffset = '3px'; }
      banner(open ? `Clique em "${(open.innerText || '').trim()}" (contornado em rosa) para abrir o formulário: o copiloto preenche quando ele aparecer.` : 'Abra o formulário de candidatura no site: o copiloto preenche quando ele aparecer.', !!extra.applicationId);
    } else if (window.top === window && (count > 0 || extra.watch))
      banner(`${count} ${count === 1 ? 'campo preenchido' : 'campos preenchidos'}${window.__joResumeDone ? ', com o currículo anexado' : ''} (contornados em rosa)${left ? `; ${left} ${left === 1 ? 'obrigatório falta' : 'obrigatórios faltam'} (em amarelo)` : ''}. Confira e envie você mesmo.`, !!extra.applicationId);
    return count;
  };

  function banner(text, offerDeclare) {
    document.getElementById('jo-fill-banner')?.remove();
    const b = document.createElement('div');
    b.id = 'jo-fill-banner';
    b.setAttribute('role', 'status');
    b.style.cssText = 'position:fixed;top:16px;right:16px;z-index:2147483647;max-width:340px;padding:12px 14px;border-radius:8px;background:#141414;color:#f1f1f1;font:13px/1.45 system-ui,sans-serif;box-shadow:0 12px 30px rgba(0,0,0,.35);border:1px solid #2a2a2a';
    const title = document.createElement('strong'); title.style.color = '#ff90ac'; title.textContent = 'Job Orchestrator';
    const body = document.createElement('div'); body.textContent = text;
    const row = document.createElement('div'); row.style.cssText = 'margin-top:8px;display:flex;gap:6px';
    const button = (label, onClick) => { const x = document.createElement('button'); x.type = 'button'; x.textContent = label; x.style.cssText = 'border:0;border-radius:5px;padding:4px 10px;background:#2a2a2a;color:#f1f1f1;cursor:pointer;font:inherit'; x.addEventListener('click', onClick); row.append(x); };
    if (offerDeclare && context.applicationId)
      button('Já enviei', () => chrome.runtime.sendMessage({ type: 'declare', applicationId: context.applicationId }, (r) => { window.__joReported = true; body.textContent = r?.ok ? 'Marcada como enviada no Job Orchestrator.' : 'O app não respondeu.'; }));
    button('Fechar', () => b.remove());
    b.append(title, body, row);
    document.documentElement.appendChild(b);
  }
})();
