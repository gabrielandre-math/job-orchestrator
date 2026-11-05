// Copia os ícones usados no painel para public/: Phosphor (MIT) como máscara na cor do texto,
// e logos oficiais do Simple Icons (CC0) com a cor da marca. Rode: node tools/icons.mjs
import { copyFileSync, existsSync, writeFileSync } from 'node:fs';
import * as si from 'simple-icons';

const phosphor = [
  'house', 'flow-arrow', 'bell-simple', 'check-square-offset', 'briefcase', 'paper-plane-tilt', 'plugs-connected', 'sliders-horizontal',
  'sidebar-simple', 'sun', 'moon', 'bookmark-simple', 'arrow-square-out', 'x', 'plus', 'minus', 'corners-out', 'play', 'globe',
  'binoculars', 'fingerprint', 'gavel', 'chart-line-up', 'pen-nib', 'seal-check', 'clipboard-text', 'shield-check', 'paper-plane-right',
  'receipt', 'envelope-simple-open', 'hand-palm', 'magnifying-glass', 'user', 'briefcase-metal', 'files', 'warning', 'check', 'clock', 'cpu', 'chat-circle-text', 'arrows-clockwise',
  'upload-simple', 'file-text', 'identification-card', 'student', 'translate', 'certificate', 'quotes', 'download-simple', 'trash', 'pencil-simple', 'lightning', 'stack', 'arrow-up', 'arrow-down',
];
const missing = [];
for (const name of phosphor) {
  const from = `node_modules/@phosphor-icons/core/assets/regular/${name}.svg`;
  if (existsSync(from)) copyFileSync(from, `projects/app/public/icons/${name}.svg`); else missing.push(name);
}

const brands = { greenhouse: 'siGreenhouse', github: 'siGithub', hackernews: 'siYcombinator', postgresql: 'siPostgresql', gmail: 'siGmail', dotnet: 'siDotnet', chrome: 'siGooglechrome' };
for (const [file, key] of Object.entries(brands)) {
  const icon = si[key];
  writeFileSync(`projects/app/public/logos/${file}.svg`, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" role="img"><title>${icon.title}</title><path fill="#${icon.hex}" d="${icon.path}"/></svg>\n`);
}
// Marcas de IA oficiais (lobehub/icons, MIT): Claude na cor da marca; OpenAI e Ollama são monocromáticas por regra das marcas.
const official = { claude: 'claude-color', openai: 'openai', ollama: 'ollama', gemini: 'gemini-color' };
for (const [file, from] of Object.entries(official)) copyFileSync(`node_modules/@lobehub/icons-static-svg/icons/${from}.svg`, `projects/app/public/logos/${file}.svg`);
writeFileSync('projects/app/public/icons/NOTICE.txt', 'Ícones: Phosphor Icons (MIT, phosphoricons.com). Logos: Simple Icons (CC0, simpleicons.org) e lobehub/icons (MIT) para Claude, OpenAI, Gemini e Ollama; as marcas pertencem aos donos e aparecem só para identificar o serviço. Marca do Job Orchestrator: forma 57 do shapes.gallery, de Monika Michalczyk, oferecida livre para projetos.\n');
console.log(`ícones: ${phosphor.length - missing.length}, logos: ${Object.keys(brands).length + Object.keys(official).length}${missing.length ? `, faltando: ${missing.join(', ')}` : ''}`);
