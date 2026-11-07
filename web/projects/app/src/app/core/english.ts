import { ProfileFact } from './api';

/**
 * The English level the CV already states ("Inglês (avançado)", "English - C1", "fluent"), as the confirmation's value. The app never
 * asks again what the profile already says; null when the CV says nothing about English.
 */
export function englishFromFacts(facts: Pick<ProfileFact, 'kind' | 'statement' | 'level'>[]): string | null {
  const fact = facts.find((f) => f.kind === 'language' && /ingl|english/i.test(f.statement));
  if (!fact) return null;
  const t = `${fact.level ?? ''} ${fact.statement}`.toLowerCase();
  if (/nativ/.test(t)) return 'native';
  if (/flu|\bc2\b/.test(t)) return 'fluent';
  if (/avan|advanced|\bc1\b/.test(t)) return 'advanced';
  if (/inter|\bb[12]\b/.test(t)) return 'intermediate';
  if (/b[áa]sic|elementar|\ba[12]\b/.test(t)) return 'basic';
  return null;
}
