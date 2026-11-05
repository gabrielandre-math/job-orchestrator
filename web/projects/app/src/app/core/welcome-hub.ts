import { Injectable, signal } from '@angular/core';
import { OnboardingView, get } from './api';

/** Abre o primeiro contato de qualquer tela e guarda o estado da configuração (IA, currículo, busca) para o Início. */
@Injectable({ providedIn: 'root' })
export class WelcomeHub {
  readonly open = signal(false);
  readonly state = signal<OnboardingView | null>(null);
  async refresh() { try { this.state.set(await get<OnboardingView>('/onboarding')); } catch { /* sem API */ } return this.state(); }
}
