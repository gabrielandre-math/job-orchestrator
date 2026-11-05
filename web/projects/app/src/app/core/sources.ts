/** Como mostrar uma fonte da campanha: plataforma (com o domínio para a logo oficial), alvo e a empresa, quando há. */
export interface SourceInfo { source: string; platform: string; platformName: string; platformDomain: string; target: string; label: string; company: string | null; companyDomain: string | null; kind: 'ats' | 'api' | 'site' | 'community' }

export const PLATFORMS: Record<string, { name: string; domain: string; kind: SourceInfo['kind'] }> = {
  greenhouse: { name: 'Greenhouse', domain: 'greenhouse.io', kind: 'ats' }, lever: { name: 'Lever', domain: 'lever.co', kind: 'ats' },
  ashby: { name: 'Ashby', domain: 'ashbyhq.com', kind: 'ats' }, smartrecruiters: { name: 'SmartRecruiters', domain: 'smartrecruiters.com', kind: 'ats' },
  recruitee: { name: 'Recruitee', domain: 'recruitee.com', kind: 'ats' }, breezy: { name: 'Breezy HR', domain: 'breezy.hr', kind: 'ats' },
  personio: { name: 'Personio', domain: 'personio.com', kind: 'ats' }, workable: { name: 'Workable', domain: 'workable.com', kind: 'ats' },
  gupy: { name: 'Gupy', domain: 'gupy.io', kind: 'ats' }, getonbrd: { name: 'GetOnBoard', domain: 'getonbrd.com', kind: 'api' }, teamtailor: { name: 'Teamtailor', domain: 'teamtailor.com', kind: 'ats' }, solides: { name: 'Sólides', domain: 'solides.com.br', kind: 'ats' }, inhire: { name: 'InHire', domain: 'inhire.app', kind: 'ats' }, quickin: { name: 'Quickin', domain: 'quickin.io', kind: 'ats' }, pandape: { name: 'Pandapé', domain: 'pandape.com.br', kind: 'ats' },
  remotive: { name: 'Remotive', domain: 'remotive.com', kind: 'api' }, arbeitnow: { name: 'Arbeitnow', domain: 'arbeitnow.com', kind: 'api' },
  himalayas: { name: 'Himalayas', domain: 'himalayas.app', kind: 'api' }, jobicy: { name: 'Jobicy', domain: 'jobicy.com', kind: 'api' },
  themuse: { name: 'The Muse', domain: 'themuse.com', kind: 'api' }, remoteok: { name: 'RemoteOK', domain: 'remoteok.com', kind: 'api' },
  wwr: { name: 'We Work Remotely', domain: 'weworkremotely.com', kind: 'api' }, github: { name: 'GitHub', domain: 'github.com', kind: 'community' },
  hn: { name: 'Hacker News', domain: 'news.ycombinator.com', kind: 'community' }, web: { name: 'Site de carreiras', domain: '', kind: 'site' },
};

export function describeSource(source: string): SourceInfo {
  const i = source.indexOf(':');
  const platform = i < 0 ? source : source.slice(0, i);
  const target = (i < 0 ? '' : source.slice(i + 1)).replace(/^https?:\/\//, '').replace(/\/$/, '');
  const p = PLATFORMS[platform] ?? { name: platform, domain: '', kind: 'api' as const };
  const site = platform === 'web';
  const company = p.kind === 'ats' ? (target.split('/').filter(Boolean).at(-1) ?? target) : site ? target.split('.')[0] : null;
  return {
    source, platform, platformName: p.name, platformDomain: site ? target : p.domain, target, kind: p.kind, company: company ? cap(company) : null,
    companyDomain: site ? target : null, label: site ? target : p.kind === 'ats' ? `${cap(company ?? target)} no ${p.name}` : `${p.name}${target ? ` · ${target}` : ''}`,
  };
}

function cap(s: string) { return s.length ? s.charAt(0).toUpperCase() + s.slice(1) : s; }
