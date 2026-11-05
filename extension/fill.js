// Preenchimento do formulário com os dados aprovados no Job Orchestrator. Só campos vazios; nunca clica em enviar; nada sai da página.
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

  const fold = (s) => (s || '').toString().normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/\s+/g, ' ').trim();

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

  function setValue(el, value) {
    const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, value);
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.dispatchEvent(new Event('change', { bubbles: true }));
  }

  function mark(el) { el.style.outline = '2px solid #ff90ac'; el.style.outlineOffset = '2px'; el.dataset.joFilled = '1'; }

  function pickOption(select, value) {
    const v = fold(value);
    const options = [...select.options].filter((o) => o.value !== '' && !o.disabled);
    return options.find((o) => fold(o.text) === v) || options.find((o) => fold(o.text).startsWith(v) || v.startsWith(fold(o.text))) || options.find((o) => fold(o.text).includes(v));
  }

  window.__joFill = (values, resume) => {
    const v = { ...values };
    if (!v.full_name && (v.first_name || v.last_name)) v.full_name = `${v.first_name || ''} ${v.last_name || ''}`.trim();
    let count = 0;
    const fields = [...document.querySelectorAll('input, textarea, select')].filter((el) => !el.disabled && !el.readOnly && el.type !== 'hidden' && el.offsetParent !== null);
    const radios = new Map();
    for (const el of fields) {
      const type = (el.type || '').toLowerCase();
      if (type === 'radio') { if (el.name) radios.set(el.name, [...(radios.get(el.name) || []), el]); continue; }
      if (type === 'checkbox' || type === 'submit' || type === 'button' || type === 'password') continue;
      const label = labelOf(el);
      if (type === 'file') {
        if (resume && !el.files?.length && (RESUME.test(label) || /pdf/i.test(el.accept || ''))) {
          const bytes = Uint8Array.from(atob(resume.data), (c) => c.charCodeAt(0));
          const dt = new DataTransfer();
          dt.items.add(new File([bytes], resume.name, { type: 'application/pdf' }));
          el.files = dt.files;
          el.dispatchEvent(new Event('change', { bubbles: true }));
          mark(el.parentElement || el);
          count++;
        }
        continue;
      }
      const key = keyFor(label);
      const value = key && v[key];
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
    // Perguntas de escolha (sim ou não, nível de inglês): marca a opção com o mesmo texto da resposta aprovada, se nenhuma estiver marcada.
    for (const group of radios.values()) {
      if (group.some((r) => r.checked)) continue;
      const question = labelOf(group[0].closest('fieldset') || group[0]);
      const key = keyFor(question);
      const value = key && v[key];
      if (!value) continue;
      const target = group.find((r) => fold(labelOf(r)).split(' ').includes(fold(value)) || fold(r.value) === fold(value));
      if (!target) continue;
      target.click();
      mark(target.parentElement || target);
      count++;
    }
    if (count > 0 && window.top === window) banner(count);
    return count;
  };

  function banner(count) {
    document.getElementById('jo-fill-banner')?.remove();
    const b = document.createElement('div');
    b.id = 'jo-fill-banner';
    b.setAttribute('role', 'status');
    b.style.cssText = 'position:fixed;top:16px;right:16px;z-index:2147483647;max-width:320px;padding:12px 14px;border-radius:8px;background:#141414;color:#f1f1f1;font:13px/1.45 system-ui,sans-serif;box-shadow:0 12px 30px rgba(0,0,0,.35);border:1px solid #2a2a2a';
    b.innerHTML = `<strong style="color:#ff90ac">Job Orchestrator</strong><br>${count} ${count === 1 ? 'campo preenchido' : 'campos preenchidos'} (contornados em rosa). Confira e envie você mesmo.<br><button type="button" style="margin-top:8px;border:0;border-radius:5px;padding:4px 10px;background:#2a2a2a;color:#f1f1f1;cursor:pointer">Fechar</button>`;
    b.querySelector('button').addEventListener('click', () => b.remove());
    document.documentElement.appendChild(b);
  }
})();
