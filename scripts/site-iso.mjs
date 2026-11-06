// Regenerates site/js/iso-art.js from the panel's iso-art.ts, so the site draws the same stations as the app.
import { stripTypeScriptTypes } from 'node:module';
import { readFileSync, writeFileSync } from 'node:fs';

const root = new URL('..', import.meta.url);
const ts = readFileSync(new URL('web/projects/ui/src/lib/iso-art.ts', root), 'utf8');
const js = stripTypeScriptTypes(ts, { mode: 'strip' });
writeFileSync(new URL('site/js/iso-art.js', root), '// Generated from web/projects/ui/src/lib/iso-art.ts (types stripped). Do not edit by hand.\n' + js + '\nexport { ART, P };\n');
