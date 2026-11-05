// Copia os ícones usados no painel para public/: Phosphor (MIT) como máscara na cor do texto,
// e logos oficiais do Simple Icons (CC0) com a cor da marca. Rode: node tools/icons.mjs
import { copyFileSync, existsSync, mkdirSync, writeFileSync } from 'node:fs';
import * as si from 'simple-icons';

const phosphor = [
  'house', 'flow-arrow', 'bell-simple', 'check-square-offset', 'briefcase', 'paper-plane-tilt', 'plugs-connected', 'sliders-horizontal',
  'sidebar-simple', 'sun', 'moon', 'bookmark-simple', 'arrow-square-out', 'x', 'plus', 'minus', 'corners-out', 'play', 'globe',
  'binoculars', 'fingerprint', 'gavel', 'chart-line-up', 'pen-nib', 'seal-check', 'clipboard-text', 'shield-check', 'paper-plane-right',
  'receipt', 'envelope-simple-open', 'hand-palm', 'magnifying-glass', 'user', 'briefcase-metal', 'files', 'warning', 'check', 'clock', 'cpu', 'chat-circle-text', 'arrows-clockwise',
  'upload-simple', 'file-text', 'identification-card', 'student', 'translate', 'certificate', 'quotes', 'download-simple', 'trash', 'pencil-simple', 'lightning', 'stack', 'arrow-up', 'arrow-down', 'eye', 'eye-slash', 'arrow-left', 'key',
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
const official = { claude: 'claude-color', openai: 'openai', ollama: 'ollama', gemini: 'gemini-color', openrouter: 'openrouter-color', deepseek: 'deepseek-color', mistral: 'mistral-color', groq: 'groq', grok: 'grok' };
for (const [file, from] of Object.entries(official)) copyFileSync(`node_modules/@lobehub/icons-static-svg/icons/${from}.svg`, `projects/app/public/logos/${file}.svg`);
// Tecnologias com logo oficial no Simple Icons (CC0). As que não têm (C#, AWS, Azure, SQL Server) ficam com monograma na tela.
const tech = {
  '.NET': 'siDotnet', 'ASP.NET Core': 'siDotnet', Blazor: 'siBlazor', Java: 'siOpenjdk', Kotlin: 'siKotlin', Spring: 'siSpring', Go: 'siGo', Python: 'siPython',
  Django: 'siDjango', Ruby: 'siRuby', Rails: 'siRubyonrails', PHP: 'siPhp', Laravel: 'siLaravel', 'Node.js': 'siNodedotjs', TypeScript: 'siTypescript',
  JavaScript: 'siJavascript', React: 'siReact', 'Next.js': 'siNextdotjs', Angular: 'siAngular', Vue: 'siVuedotjs', Svelte: 'siSvelte', Flutter: 'siFlutter',
  Dart: 'siDart', Swift: 'siSwift', Android: 'siAndroid', iOS: 'siIos', Rust: 'siRust', Scala: 'siScala', Elixir: 'siElixir', GCP: 'siGooglecloud',
  Kubernetes: 'siKubernetes', Docker: 'siDocker', Terraform: 'siTerraform', Kafka: 'siApachekafka', RabbitMQ: 'siRabbitmq', PostgreSQL: 'siPostgresql',
  MySQL: 'siMysql', MongoDB: 'siMongodb', Redis: 'siRedis', Elasticsearch: 'siElasticsearch', Snowflake: 'siSnowflake', Databricks: 'siDatabricks',
  Spark: 'siApachespark', Airflow: 'siApacheairflow', GraphQL: 'siGraphql', Linux: 'siLinux', Nginx: 'siNginx', Git: 'siGit', 'GitHub Actions': 'siGithubactions',
  Jenkins: 'siJenkins', Datadog: 'siDatadog', Grafana: 'siGrafana', Prometheus: 'siPrometheus', Firebase: 'siFirebase', Supabase: 'siSupabase', Tailwind: 'siTailwindcss', Figma: 'siFigma',
};
// Empresas que contratam com frequência e têm logo oficial no Simple Icons. As outras usam o ícone do site ou o monograma.
const companies = ['Nubank', 'iFood', 'VTEX', 'MercadoPago', 'Mercadolibre', 'PicPay', 'Stripe', 'Shopify', 'Spotify', 'Uber', 'Airbnb', 'Netflix', 'Google', 'Meta', 'Apple', 'Totvs',
  'GitLab', 'GitHub', 'Atlassian', 'Datadog', 'Cloudflare', 'Vercel', 'Twilio', 'Coinbase', 'Revolut', 'Wise', 'Klarna', 'Hotmart', 'Olist', 'QuintoAndar', 'Creditas', 'Wellhub',
  'Ambev', 'Natura', 'MagazineLuiza', 'Rappi', 'Pipefy', 'RDStation', 'Ebanx', 'Oracle', 'SAP', 'IBM', 'Salesforce', 'Adobe', 'Intel', 'Nvidia', 'Samsung', 'Accenture',
  'Thoughtworks', 'Globant', 'Notion', 'Linear', 'Discord', 'Slack', 'Zoom', 'Dropbox', 'Twitch', 'Reddit', 'Pinterest', 'Duolingo', 'Canva', 'Miro', 'Asana', 'HubSpot',
  'Zendesk', 'Intercom', 'Algolia', 'Elastic', 'MongoDB', 'Snowflake', 'Databricks', 'HashiCorp', 'PostHog', 'Supabase', 'Netlify', 'Heroku', 'DigitalOcean', 'RedHat',
  'Canonical', 'Mozilla', 'JetBrains', 'Unity', 'EpicGames', 'RiotGames', 'Valve', 'Booking', 'Expedia', 'Ifood', 'Wildlifestudios', 'Picnic', 'Deliveroo', 'Glovo', 'Bolt', 'Grab'];
const fold = (s) => s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9]/g, '');
const luminance = (hex) => { const [r, g, b] = [0, 2, 4].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255); return 0.2126 * r + 0.7152 * g + 0.0722 * b; };
mkdirSync('projects/app/public/logos/tech', { recursive: true });
mkdirSync('projects/app/public/logos/brands', { recursive: true });
const techMap = {}, brandMap = {};
for (const [name, key] of Object.entries(tech)) {
  const icon = si[key];
  if (!icon) continue;
  const file = icon.slug;
  writeFileSync(`projects/app/public/logos/tech/${file}.svg`, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" role="img"><title>${icon.title}</title><path fill="#${icon.hex}" d="${icon.path}"/></svg>\n`);
  techMap[name] = { file, dark: luminance(icon.hex) < 0.22 };
}
for (const name of companies) {
  const icon = si['si' + name.charAt(0).toUpperCase() + name.slice(1).toLowerCase()] ?? si['si' + name];
  if (!icon) continue;
  writeFileSync(`projects/app/public/logos/brands/${icon.slug}.svg`, `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" role="img"><title>${icon.title}</title><path fill="#${icon.hex}" d="${icon.path}"/></svg>\n`);
  brandMap[fold(icon.title)] = { file: icon.slug, dark: luminance(icon.hex) < 0.22 };
}
writeFileSync('projects/ui/src/lib/brands.ts', `// Gerado por tools/icons.mjs. Não edite à mão.\nexport const TECH: Record<string, { file: string; dark: boolean }> = ${JSON.stringify(techMap)};\nexport const BRANDS: Record<string, { file: string; dark: boolean }> = ${JSON.stringify(brandMap)};\n`);
writeFileSync('projects/app/public/icons/NOTICE.txt', 'Ícones: Phosphor Icons (MIT, phosphoricons.com). Logos: Simple Icons (CC0, simpleicons.org) e lobehub/icons (MIT) para os provedores de IA; as marcas pertencem aos donos e aparecem só para identificar o serviço. Marca do Job Orchestrator: forma 57 do shapes.gallery, de Monika Michalczyk, oferecida livre para projetos.\n');
console.log(`ícones: ${phosphor.length - missing.length}, logos: ${Object.keys(brands).length + Object.keys(official).length}, tecnologias: ${Object.keys(techMap).length}, empresas: ${Object.keys(brandMap).length}${missing.length ? `, faltando: ${missing.join(', ')}` : ''}`);
