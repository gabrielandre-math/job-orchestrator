import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', title: 'Início', loadComponent: () => import('./features/overview/overview.page').then((m) => m.OverviewPage) },
  { path: 'vagas', title: 'Vagas', loadComponent: () => import('./features/jobs/jobs.page').then((m) => m.JobsPage) },
  { path: 'busca', title: 'Busca ao vivo', loadComponent: () => import('./features/search/search.page').then((m) => m.SearchPage) },
  { path: 'salvar', title: 'Salvar vaga', loadComponent: () => import('./features/capture/capture.page').then((m) => m.CapturePage) },
  { path: 'candidaturas', title: 'Candidaturas', loadComponent: () => import('./features/applications/applications.page').then((m) => m.ApplicationsPage) },
  { path: 'candidaturas/:id', title: 'Candidatura', loadComponent: () => import('./features/applications/application.page').then((m) => m.ApplicationPage) },
  { path: 'perfil', title: 'Perfil', loadComponent: () => import('./features/profile/profile.page').then((m) => m.ProfilePage) },
  // Configurações e as abas dela.
  { path: 'configuracoes', title: 'Configurações', loadComponent: () => import('./features/settings/settings.page').then((m) => m.SettingsPage) },
  { path: 'campanha', title: 'O que buscar', loadComponent: () => import('./features/campaign/campaign.page').then((m) => m.CampaignPage) },
  { path: 'provedores', title: 'IA', loadComponent: () => import('./features/providers/providers.page').then((m) => m.ProvidersPage) },
  { path: 'fontes', title: 'Fontes', loadComponent: () => import('./features/sources/sources.page').then((m) => m.SourcesPage) },
  { path: 'email', title: 'E-mail', loadComponent: () => import('./features/mail/mail.page').then((m) => m.MailPage) },
  { path: 'diagnostico', title: 'Diagnóstico', loadComponent: () => import('./features/diagnostics/diagnostics.page').then((m) => m.DiagnosticsPage) },
  { path: 'assistente', title: 'Assistente', loadComponent: () => import('./features/assistant/assistant.page').then((m) => m.AssistantPage) },
  { path: 'instalar', title: 'Instalar', loadComponent: () => import('./features/install/install.page').then((m) => m.InstallPage) },
  // Old addresses of the pages that left the app (approval, live map, technical source tools) land where their job is now.
  { path: 'atencao', redirectTo: '' },
  { path: 'revisao', redirectTo: '' },
  { path: 'fluxo', redirectTo: '' },
  { path: 'testes', redirectTo: 'fontes' },
  { path: 'fontes/:tool', redirectTo: 'fontes' },
  { path: 'varredura', redirectTo: 'fontes' },
  { path: 'adapters', redirectTo: 'fontes' },
  { path: '**', redirectTo: '' },
];
