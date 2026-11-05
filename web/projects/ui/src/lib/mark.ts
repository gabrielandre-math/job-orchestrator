import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * A marca: a rota. Um caminho único que desce, faz a curva e sobe de novo, como cada vaga passa pelas etapas
 * até a candidatura. Forma 57 do shapes.gallery (Monika Michalczyk), usada como veio, na cor do texto ao redor.
 */
@Component({
  selector: 'jo-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<svg viewBox="0 0 256 256" [attr.width]="size()" [attr.height]="size()" aria-hidden="true" focusable="false"><path fill="currentColor" d="M 180 0 C 221.974 0 256 34.026 256 76 L 256 256 L 208 256 L 208 76 C 208 60.536 195.464 48 180 48 C 164.536 48 152 60.536 152 76 L 152 180 C 152 221.974 117.974 256 76 256 C 34.026 256 0 221.974 0 180 L 0 0 L 48 0 L 48 180 C 48 195.464 60.536 208 76 208 C 91.464 208 104 195.464 104 180 L 104 76 C 104 34.026 138.026 0 180 0 Z" /></svg>`,
  styles: `:host { display: inline-flex; flex: none; } svg { display: block; }`,
})
export class Mark {
  readonly size = input(24);
}
