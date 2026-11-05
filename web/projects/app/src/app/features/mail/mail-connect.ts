import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { Company } from '@jo/ui/company';
import { Icon } from '@jo/ui/icon';
import { Iso } from '@jo/ui/iso';
import { MailPreset, MailSettings, get, send } from '../../core/api';
import { command } from '../../core/command';
import { Wizard } from '../../shared/wizard';

interface Provider { id: string; name: string; domain: string; logo: string | null }
const PROVIDERS: Provider[] = [
  { id: 'gmail', name: 'Gmail', domain: 'gmail.com', logo: '/logos/gmail.svg' }, { id: 'outlook', name: 'Outlook', domain: 'outlook.com', logo: null },
  { id: 'icloud', name: 'iCloud', domain: 'icloud.com', logo: null }, { id: 'zoho', name: 'Zoho Mail', domain: 'zoho.com', logo: '/logos/zoho.svg' },
  { id: 'smtp', name: 'Outro servidor', domain: '', logo: null },
];
const MODES = [
  { key: 'Draft', label: 'Só rascunho', hint: 'A IA escreve, você copia e envia por conta própria. Nada sai daqui.' },
  { key: 'Review', label: 'Revisão sua', hint: 'Cada e-mail espera o seu ok antes de sair, com o texto e o anexo à vista. Recomendado.', rec: true },
  { key: 'Auto', label: 'Automático', hint: 'Escreve e envia sozinho dentro dos limites abaixo, só para o e-mail que a vaga publica. Para quando a empresa responde.' },
];

/**
 * Conectar e-mail em cinco passos, na moldura dos assistentes: provedor, senha de app (com o link oficial para criá-la),
 * teste de envio e leitura, como os e-mails saem (rascunho, revisão ou automático com consentimento) e pronto.
 */
@Component({
  selector: 'app-mail-connect',
  imports: [Company, Icon, Iso, Wizard],
  templateUrl: './mail-connect.html',
  styleUrl: './mail-connect.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailConnect {
  readonly start = input(1);
  readonly settings = input<MailSettings | null>(null);
  readonly close = output<void>();
  readonly changed = output<void>();
  protected readonly providers = PROVIDERS;
  protected readonly modes = MODES;
  protected readonly step = signal(1);
  protected readonly provider = signal<Provider | null>(null);
  protected readonly presets = signal<MailPreset[]>([]);
  protected readonly consentText = signal('');
  protected readonly address = signal('');
  protected readonly name = signal('');
  protected readonly password = signal('');
  protected readonly show = signal(false);
  protected readonly smtpHost = signal(''); protected readonly smtpPort = signal(587); protected readonly smtpSsl = signal(false);
  protected readonly imapHost = signal(''); protected readonly imapPort = signal(993);
  protected readonly connected = signal<{ id: string; address: string } | null>(null);
  protected readonly mode = signal<'Draft' | 'Review' | 'Auto'>('Review');
  protected readonly consent = signal(false);
  protected readonly cap = signal(5);
  protected readonly followUp = signal(5);
  protected readonly windowStart = signal(8); protected readonly windowEnd = signal(19);
  protected readonly attach = signal(true);
  protected readonly connecting = command((body: object) => send<{ id: string; address: string }>('POST', '/mail/accounts', body), 'E-mail conectado: envio e leitura testados.');
  protected readonly testing = command((id: string) => send('POST', `/mail/accounts/${id}/test`), 'E-mail de teste enviado para você mesmo.');
  protected readonly saving = command((body: object) => send('PUT', '/mail/settings', body), 'Envio configurado.');
  protected readonly preset = computed(() => this.presets().find((p) => p.id === this.provider()?.id) ?? null);

  constructor() {
    void get<MailPreset[]>('/mail/presets').then((p) => this.presets.set(p)).catch(() => undefined);
    void get<{ text: string }>('/mail/consent').then((c) => this.consentText.set(c.text)).catch(() => undefined);
    queueMicrotask(() => {
      this.step.set(this.start());
      const s = this.settings();
      if (s) { this.mode.set(s.mode); this.cap.set(s.dailyCap); this.followUp.set(s.followUpDays); this.windowStart.set(s.windowStart); this.windowEnd.set(s.windowEnd); this.attach.set(s.attachResume); this.consent.set(!!s.autoConsentAt); }
    });
  }

  protected pick(p: Provider) { this.provider.set(p); this.step.set(2); }
  protected async connect() {
    const p = this.provider()!;
    const body = p.id === 'smtp'
      ? { provider: 'smtp', address: this.address(), displayName: this.name(), password: this.password(), smtpHost: this.smtpHost(), smtpPort: this.smtpPort(), smtpSsl: this.smtpSsl(), imapHost: this.imapHost(), imapPort: this.imapPort() }
      : { provider: p.id, address: this.address(), displayName: this.name(), password: this.password() };
    const r = await this.connecting.run(body);
    if (!r) return;
    this.password.set('');
    this.connected.set(r);
    this.changed.emit();
    this.step.set(3);
  }
  protected async save() {
    if ((await this.saving.run({ mode: this.mode(), dailyCap: this.cap(), followUpDays: this.followUp(), windowStart: this.windowStart(), windowEnd: this.windowEnd(), attachResume: this.attach(), consent: this.mode() === 'Auto' && this.consent() })) === null) return;
    this.changed.emit();
    this.step.set(5);
  }
  protected num(e: Event) { return +(e.target as HTMLInputElement).value; }
}
