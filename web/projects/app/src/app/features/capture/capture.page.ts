import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { CaptureResult, send } from '../../core/api';
import { command } from '../../core/command';

/**
 * "Salvar vaga": aberta pelo botão da barra de favoritos com o link, o título e o texto que você selecionou.
 * Nada aqui navega em outro site: o que chega é só o que você mandou.
 */
@Component({
  selector: 'app-capture',
  imports: [RouterLink],
  template: `
    <section class="page narrow">
      <header class="page-head"><div><h1>Salvar vaga</h1><p>Link de Greenhouse, Lever, Ashby ou SmartRecruiters vira empresa vigiada. Link de LinkedIn ou Gupy vira vaga salva, com a candidatura pelo site.</p></div></header>
      <section class="panel form">
        <label class="field">Link<input [value]="link()" (input)="link.set($any($event.target).value)" placeholder="https://" /></label>
        <label class="field">Título da página<input [value]="name()" (input)="name.set($any($event.target).value)" /></label>
        <label class="field">Descrição (o texto que você selecionou; sem ele a vaga fica sem nota)<textarea rows="8" [value]="body()" (input)="body.set($any($event.target).value)"></textarea></label>
        @if (save.problem(); as p) { <p class="problem">{{ p.detail }}</p> }
        @if (result(); as r) { <p class="notice">{{ r.message }} <a routerLink="/vagas">Ver vagas</a></p> }
        <div class="actions"><button class="btn primary" type="button" [disabled]="save.running() || !link()" (click)="submit()">{{ save.running() ? 'Salvando…' : 'Salvar' }}</button></div>
      </section>
    </section>`,
  styles: `.narrow { max-width: 820px; } .form { display: grid; gap: 14px; padding: 18px; } .actions { display: flex; justify-content: flex-end; } .notice a { color: var(--accent); }`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CapturePage {
  private readonly router = inject(Router);
  readonly url = input<string>('');
  readonly title = input<string>('');
  readonly text = input<string>('');
  protected readonly link = signal('');
  protected readonly name = signal('');
  protected readonly body = signal('');
  protected readonly result = signal<CaptureResult | null>(null);
  protected readonly save = command(() => send<CaptureResult>('POST', '/inbox', { url: this.link(), title: this.name(), text: this.body() }));

  ngOnInit() {
    this.link.set(this.url());
    this.name.set(this.title());
    this.body.set(this.text());
  }

  protected async submit() {
    const r = await this.save.run(undefined);
    if (r) { this.result.set(r); void this.router.navigate([], { queryParams: {} }); }
  }
}
