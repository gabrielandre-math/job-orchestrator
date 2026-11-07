import { ChangeDetectionStrategy, Component, computed, output, signal } from '@angular/core';
import { Iso } from '@jo/ui/iso';
import { Icon } from '@jo/ui/icon';
import { Confirmations, Identity, Profile, get, send } from '../../core/api';
import { command } from '../../core/command';

/** The same local form setup is available during onboarding and later from Profile. */
@Component({
  selector: 'app-form-profile', imports: [Iso, Icon], templateUrl: './form-profile.html', styleUrl: './form-profile.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormProfile {
  readonly saved = output<void>();
  protected readonly step = signal(1);
  protected readonly loaded = signal(false);
  protected readonly loadError = signal('');
  protected readonly identity = signal<Identity>({ fullName: '', email: null, phone: null, location: null, linkedIn: null, gitHub: null, website: null });
  protected readonly preferences = signal<Confirmations | null>(null);
  protected readonly birthDate = signal('');
  protected readonly today = new Date().toLocaleDateString('en-CA');
  protected readonly contactReady = computed(() => !!this.identity().fullName.trim() && !!this.identity().email?.trim() && !!this.identity().phone?.trim() && !!this.identity().location?.trim());
  protected readonly saving = command(async () => {
    await send('PUT', '/profile/identity', this.identity());
    await send('PUT', '/profile/form-preferences', { confirmations: this.preferences() });
    return send<{ resumed: number }>('PUT', '/applications/answers', { question: 'Data de nascimento', answer: this.birthDate() || null });
  }, 'Dados para formulários salvos nesta máquina.');
  constructor() { void this.load(); }
  protected async load() {
    this.loadError.set('');
    try {
      const [p, answers] = await Promise.all([get<Profile>('/profile'), get<{ key: string; question: string; answer: string }[]>('/applications/answers')]);
      this.identity.set(p.identity); this.preferences.set(p.confirmations);
      this.birthDate.set(answers.find(a => a.key === 'custom:birth_date')?.answer ?? '');
      this.loaded.set(true);
    } catch { this.loadError.set('Não foi possível carregar seus dados. Tente de novo.'); }
  }
  protected id(key: keyof Identity, value: string) { this.identity.update(v => ({ ...v, [key]: key === 'fullName' ? value : value || null } as Identity)); }
  protected pref(key: keyof Confirmations, value: unknown) { this.preferences.update(v => v ? { ...v, [key]: value } : v); }
  protected amount(key: keyof Confirmations, value: string) { this.pref(key, value === '' ? null : Number(value)); }
  protected countries(value: string) { this.pref('authorizedCountries', value.toUpperCase().split(/[,;\s]+/).filter(Boolean)); }
  protected modality(value: string, checked: boolean) { this.preferences.update(v => v ? { ...v, modalities: [...v.modalities.filter(x => x !== value), ...(checked ? [value] : [])] } : v); }
  protected next() { if (this.contactReady()) this.step.set(2); }
  protected async save() { if (this.contactReady() && (await this.saving.run(undefined)) !== null) this.saved.emit(); }
}
