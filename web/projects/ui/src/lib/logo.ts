import { SVGL, SvglLogo } from './svgl';

/** Nomes comuns em vaga que o svgl guarda com outro título (AWS é "Amazon Web Services", Kafka é "Apache Kafka"...). */
const ALIAS: Record<string, string> = {
  aws: 'amazonwebservices', amazonaws: 'amazonwebservices', azure: 'microsoftazure', golang: 'go', kafka: 'apachekafka', vuejs: 'vue', gcp: 'googlecloud',
  googlecloudplatform: 'googlecloud', k8s: 'kubernetes', postgres: 'postgresql', node: 'nodejs', reactjs: 'react', reactnative: 'react', sqlserver: 'microsoftsqlserver',
  mssql: 'microsoftsqlserver', js: 'javascript', ts: 'typescript', html: 'html5', angularjs: 'angular', springboot: 'spring', vscode: 'visualstudiocode',
  nest: 'nestjs', next: 'nextjs', tailwind: 'tailwindcss', microsoftteams: 'microsoftteams', teams: 'microsoftteams',
};

export function norm(s: string): string { return s.toLowerCase().replace(/#/g, 'sharp').replace(/\+/g, 'plus').replace(/[^a-z0-9]/g, ''); }

/** O logo do svgl para um nome de tecnologia, empresa ou plataforma, ou null. */
export function svgl(name: string): SvglLogo | null {
  const k = norm(name);
  return SVGL[ALIAS[k] ?? k] ?? null;
}
