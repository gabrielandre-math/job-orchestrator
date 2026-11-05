// Fala com o Job Orchestrator local (127.0.0.1:7410). A página da vaga nunca fala com o app: só esta extensão, com a permissão do host.
const API = 'http://127.0.0.1:7410';

chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  (async () => {
    try {
      if (msg.type === 'status') {
        const r = await fetch(`${API}/api/health`);
        reply({ up: r.ok });
      } else if (msg.type === 'match') {
        const r = await fetch(`${API}/api/copilot/match?url=${encodeURIComponent(msg.url)}`);
        if (r.ok) { reply({ found: true, ...(await r.json()) }); return; }
        // Sem candidatura desta página: só os dados pessoais e as respostas gerais do perfil.
        const a = await fetch(`${API}/api/profile/answers`);
        reply({ found: false, answers: a.ok ? await a.json() : {} });
      } else if (msg.type === 'resume') {
        const r = await fetch(`${API}/api/applications/${msg.id}/resume`);
        if (!r.ok) { reply(null); return; }
        const bytes = new Uint8Array(await r.arrayBuffer());
        let binary = '';
        for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
        reply({ name: msg.name, data: btoa(binary) });
      }
    } catch {
      reply(null);
    }
  })();
  return true;
});
