import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Ícone Phosphor (regular) como máscara: pega a cor do texto ao redor, como no Grifo. */
@Component({
  selector: 'jo-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '',
  styles: `:host { display: inline-block; flex: none; background: currentColor; mask: var(--icon) center / contain no-repeat; -webkit-mask: var(--icon) center / contain no-repeat; }`,
  host: { 'aria-hidden': 'true', '[style.width.px]': 'size()', '[style.height.px]': 'size()', '[style.--icon]': 'url()' },
})
export class Icon {
  readonly name = input.required<string>();
  readonly size = input(18);
  protected readonly url = computed(() => `url(/icons/${this.name()}.svg)`);
}
