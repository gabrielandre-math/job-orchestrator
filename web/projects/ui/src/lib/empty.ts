import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Estado vazio: diz o que falta e o próximo passo, sem ilustração. */
@Component({
  selector: 'jo-empty',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<p class="title">{{ title() }}</p>@if (hint()) {<p class="hint">{{ hint() }}</p>}<ng-content />`,
  styles: `
    :host { display: grid; justify-items: start; gap: 4px; padding: 28px 16px; }
    .title { margin: 0; font-weight: 500; }
    .hint { margin: 0 0 8px; color: var(--text-2); max-width: 52ch; }
  `,
})
export class Empty {
  readonly title = input.required<string>();
  readonly hint = input<string>('');
}
