import { Directive, ElementRef, OnDestroy, inject, input } from '@angular/core';

let box: HTMLDivElement | null = null;
let owner: Tip | null = null;

/** Uma caixa só para a tela inteira, presa ao body: some ao rolar, para nunca ficar solta fora do lugar. */
function tipBox(): HTMLDivElement {
  if (box) return box;
  box = document.createElement('div');
  box.className = 'jo-tip';
  box.id = 'jo-tip';
  box.setAttribute('role', 'tooltip');
  document.body.appendChild(box);
  addEventListener('scroll', () => owner?.hide(), { passive: true, capture: true });
  return box;
}

/**
 * Dica ao passar o mouse ou focar: caixa do tema perto do elemento, que aparece na hora, com a primeira linha em destaque.
 * "Remoto: 316" vira "Remoto" em cima e "316" embaixo; texto com quebra de linha mostra uma linha por vez. Troca o title nativo,
 * que demora, sai no cinza do sistema e corta texto longo.
 */
@Directive({
  selector: '[joTip]',
  host: { '(mouseenter)': 'show()', '(mouseleave)': 'hide()', '(focusin)': 'show()', '(focusout)': 'hide()' },
})
export class Tip implements OnDestroy {
  readonly joTip = input<string | null | undefined>();
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  show() {
    const text = (this.joTip() ?? '').trim();
    if (!text) return;
    const b = tipBox();
    owner = this;
    const lines = text.includes('\n') ? text.split('\n') : text.includes(': ') ? [text.slice(0, text.indexOf(': ')), text.slice(text.indexOf(': ') + 2)] : [text];
    const [head, ...rest] = lines.map((l) => (/^\d{4,}$/.test(l.trim()) ? Number(l).toLocaleString('pt-BR') : l.trim()));
    const strong = document.createElement('strong');
    strong.textContent = head;
    b.replaceChildren(strong, ...rest.map((l) => Object.assign(document.createElement('span'), { textContent: l })));
    b.classList.add('on');
    const r = this.host.nativeElement.getBoundingClientRect();
    const t = b.getBoundingClientRect();
    const above = r.top - t.height - 10 > 8;
    const top = above ? r.top - t.height - 10 : r.bottom + 10;
    const left = Math.min(innerWidth - t.width - 8, Math.max(8, r.left + r.width / 2 - t.width / 2));
    b.style.transform = `translate(${Math.round(left)}px, ${Math.round(top)}px)`;
    b.dataset['side'] = above ? 'top' : 'bottom';
    this.host.nativeElement.setAttribute('aria-describedby', 'jo-tip');
  }

  hide() {
    this.host.nativeElement.removeAttribute('aria-describedby');
    if (owner !== this || !box) return;
    box.classList.remove('on');
    owner = null;
  }

  ngOnDestroy() { this.hide(); }
}
