import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', title: 'Visão geral', loadComponent: () => import('./features/overview/overview.page').then((m) => m.OverviewPage) },
  { path: 'fluxo', title: 'Fluxo', loadComponent: () => import('./features/flow/flow.page').then((m) => m.FlowPage) },
  { path: 'atencao', title: 'Atenção', loadComponent: () => import('./features/attention/attention.page').then((m) => m.AttentionPage) },
  { path: 'revisao', title: 'Revisão', loadComponent: () => import('./features/review/review.page').then((m) => m.ReviewPage) },
  { path: 'perfil', title: 'Perfil', loadComponent: () => import('./features/profile/profile.page').then((m) => m.ProfilePage) },
  { path: 'vagas', title: 'Vagas', loadComponent: () => import('./features/jobs/jobs.page').then((m) => m.JobsPage) },
  { path: 'candidaturas', title: 'Candidaturas', loadComponent: () => import('./features/applications/applications.page').then((m) => m.ApplicationsPage) },
  { path: 'candidaturas/:id', title: 'Candidatura', loadComponent: () => import('./features/applications/application.page').then((m) => m.ApplicationPage) },
  { path: 'adapters', title: 'Adapters', loadComponent: () => import('./features/adapters/adapters.page').then((m) => m.AdaptersPage) },
  { path: 'salvar', title: 'Salvar vaga', loadComponent: () => import('./features/capture/capture.page').then((m) => m.CapturePage) },
  { path: 'provedores', title: 'Provedores de IA', loadComponent: () => import('./features/providers/providers.page').then((m) => m.ProvidersPage) },
  { path: 'campanha', title: 'Campanha', loadComponent: () => import('./features/campaign/campaign.page').then((m) => m.CampaignPage) },
  { path: '**', redirectTo: '' },
];
