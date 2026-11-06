// Generated from web/projects/ui/src/lib/iso-art.ts (types stripped). Do not edit by hand.
/**
 * Ícones isométricos desenhados por código: cada agente é uma estação sobre a mesma base, então a família toda tem a mesma
 * escala, a mesma luz (topo claro, esquerda média, direita escura) e as mesmas cores da marca. Sem arquivo de terceiros.
 */
                                                       
                                   

                                                                         
                                                                                                          

                                                               
                                                             

const C = Math.cos(Math.PI / 6);
const P = ([x, y, z]    )                   => [(x - y) * C, (x + y) * 0.5 - z];
const fmt = (n        ) => Math.round(n * 100) / 100;
const poly = (pts      ) => 'M ' + pts.map((v) => P(v).map(fmt).join(' ')).join(' L ') + ' Z';
const open = (pts      ) => 'M ' + pts.map((v) => P(v).map(fmt).join(' ')).join(' L ');

const BASE      = { x: 0, y: 0, z: 0, w: 36, d: 36, h: 4, m: 'base' };

/** Folha deitada com linhas de texto no topo. */
function sheet(x        , y        , z        , w = 20, d = 26, lines = 4, first               = 'accent')      {
  const top = z + 1.4;
  return {
    x, y, z, w, d, h: 1.4, m: 'paper',
    lines: Array.from({ length: lines }, (_, i) => ({
      pts: [[x + 3, y + 4 + i * 5, top], [x + w - (i === lines - 1 ? 9 : 3), y + 4 + i * 5, top]]        ,
      tone: i === 0 ? first : 'ink', w: 1.3,
    })),
  };
}

/** Anel isométrico deitado (plano z). */
function ring(cx        , cy        , z        , r        , steps = 28)       {
  return Array.from({ length: steps + 1 }, (_, i) => {
    const a = (i / steps) * Math.PI * 2;
    return [cx + Math.cos(a) * r, cy + Math.sin(a) * r, z]      ;
  });
}

const ART                        = {
  // Leitor: duas folhas empilhadas, a de cima com as linhas.
  reader: [BASE, sheet(10, 6, 4, 20, 26, 0), sheet(7, 4, 6.2, 20, 26)],
  // Extrator: a folha e três fatos (cubos) subindo dela.
  extractor: [BASE, sheet(5, 7, 4, 18, 24, 3),
    { x: 25, y: 26, z: 4, w: 6, d: 6, h: 6, m: 'accent' }, { x: 25, y: 17, z: 9, w: 5, d: 5, h: 5, m: 'accent' }, { x: 26, y: 9, z: 15, w: 4, d: 4, h: 4, m: 'accent' }],
  // Conferente: a folha com o visto.
  checker: [BASE, {
    ...sheet(8, 5, 4, 20, 26, 2, 'ink'),
    lines: [...(sheet(8, 5, 4, 20, 26, 2, 'ink').lines ?? []), { pts: [[12, 20, 5.4], [16, 26, 5.4], [25, 15, 5.4]], tone: 'accent', w: 3.2 }],
  }],
  // Seletor: peneira de três placas, larga em cima, e o fato escolhido descendo.
  selector: [BASE, { x: 11, y: 11, z: 4, w: 14, d: 14, h: 3, m: 'dark' }, { x: 8, y: 8, z: 10, w: 20, d: 20, h: 2.5, m: 'paper' },
    { x: 5, y: 5, z: 16, w: 26, d: 26, h: 2.5, m: 'base' }, { x: 15, y: 15, z: 23, w: 6, d: 6, h: 6, m: 'accent' }],
  // Redator: a folha e a caneta deitada, com a ponta na cor da marca.
  writer: [BASE, sheet(4, 6, 4, 20, 26, 4, 'ink'), { x: 27, y: 4, z: 4, w: 4, d: 22, h: 4, m: 'dark' }, { x: 27, y: 26, z: 4, w: 4, d: 5, h: 4, m: 'accent' }],
  // Revisor: a folha e a lente suspensa.
  reviewer: [BASE, {
    ...sheet(6, 6, 4, 20, 26, 4, 'ink'),
    lines: [...(sheet(6, 6, 4, 20, 26, 4, 'ink').lines ?? []), { pts: ring(18, 18, 14, 8), tone: 'accent', w: 3 }, { pts: [[24, 24, 14], [31, 31, 9]], tone: 'accent', w: 3 }],
  }],
  // Renderizador: a impressora com a folha saindo.
  renderer: [BASE, { x: 7, y: 9, z: 4, w: 22, d: 20, h: 10, m: 'dark', lines: [{ pts: [[9, 29, 9], [27, 29, 9]], tone: 'accent', w: 1.6 }] },
    { x: 10, y: 12, z: 14, w: 16, d: 1.6, h: 12, m: 'paper', lines: [{ pts: [[12, 13.6, 23], [22, 13.6, 23]], tone: 'ink', w: 1.2 }, { pts: [[12, 13.6, 19], [24, 13.6, 19]], tone: 'ink', w: 1.2 }] }],
  // Inspetor ATS: a folha em pé e a barra de leitura atravessando.
  ats: [BASE, {
    x: 8, y: 16, z: 4, w: 20, d: 1.6, h: 26, m: 'paper',
    lines: [4, 8, 12, 20].map((h, i) => ({ pts: [[11, 17.6, 4 + 26 - h], [i === 3 ? 19 : 25, 17.6, 4 + 26 - h]]        , tone: 'ink'         , w: 1.2 })),
  }, { x: 5, y: 13, z: 16, w: 26, d: 8, h: 2, m: 'accent' }],
  // Guardião: o cofre, a porta e a fechadura.
  guardian: [BASE, {
    x: 7, y: 7, z: 4, w: 22, d: 22, h: 22, m: 'dark',
    lines: [
      { pts: [[10, 29, 7], [26, 29, 7], [26, 29, 23], [10, 29, 23], [10, 29, 7]], tone: 'soft', w: 1.2 },
      { pts: Array.from({ length: 25 }, (_, i) => { const a = (i / 24) * Math.PI * 2; return [18 + Math.cos(a) * 2.6, 29, 17 + Math.sin(a) * 2.6]      ; }), tone: 'accent', w: 2.2 },
      { pts: [[18, 29, 14.2], [18, 29, 10.5]], tone: 'accent', w: 2.6 },
    ],
  }],
  // Roteador: a caixa com as portas e a antena.
  router: [BASE, { x: 5, y: 9, z: 4, w: 26, d: 18, h: 6, m: 'dark', lines: [10, 16, 22].map((x) => ({ pts: [[x, 27, 7], [x + 3, 27, 7]]        , tone: 'accent'         , w: 2 })) },
    { x: 26, y: 11, z: 10, w: 2, d: 2, h: 14, m: 'paper' }, { x: 25.5, y: 10.5, z: 24, w: 3, d: 3, h: 3, m: 'accent' }],
  // Você: a tela sobre o suporte.
  you: [BASE, { x: 15, y: 15, z: 4, w: 6, d: 6, h: 6, m: 'dark' },
    { x: 5, y: 16, z: 10, w: 26, d: 2, h: 17, m: 'dark', lines: [[23, 'accent'], [19, 'soft'], [15, 'soft']].map(([z, tone]) => ({ pts: [[8, 18, z          ], [26, 18, z          ]]        , tone: tone                , w: 1.4 })) }],
  // Batedor: o radar varrendo a base.
  radar: [{
    ...BASE,
    lines: [{ pts: ring(18, 18, 4, 14), tone: 'soft', w: 1.2 }, { pts: ring(18, 18, 4, 9), tone: 'soft', w: 1.2 }, { pts: [[18, 18, 4], [30, 9, 4]], tone: 'accent', w: 2.4 }],
  }, { x: 16.5, y: 16.5, z: 4, w: 3, d: 3, h: 9, m: 'dark' }, { x: 15.5, y: 15.5, z: 13, w: 5, d: 5, h: 5, m: 'accent' }, { x: 27, y: 9, z: 4, w: 3, d: 3, h: 3, m: 'accent' }],
  // Identidade: o crachá em pé, com a foto e as linhas.
  identity: [BASE, {
    x: 7, y: 16, z: 4, w: 22, d: 1.6, h: 16, m: 'paper',
    lines: [{ pts: [[17, 17.6, 15], [26, 17.6, 15]], tone: 'ink', w: 1.3 }, { pts: [[17, 17.6, 11], [24, 17.6, 11]], tone: 'ink', w: 1.3 }, { pts: [[10, 17.6, 8], [26, 17.6, 8]], tone: 'soft', w: 1.3 }],
  }, { x: 9, y: 17.6, z: 10, w: 6, d: 0.8, h: 7, m: 'accent' }],
  // Juiz: a cancela; passa o que cabe.
  gate: [BASE, { x: 5, y: 15, z: 4, w: 3, d: 3, h: 17, m: 'dark' }, { x: 28, y: 15, z: 4, w: 3, d: 3, h: 17, m: 'dark' },
    { x: 8, y: 15.5, z: 15, w: 20, d: 2, h: 3, m: 'accent' }, { x: 15, y: 22, z: 4, w: 6, d: 6, h: 6, m: 'paper' }],
  // Analista: as barras da nota, a mais alta na cor da marca.
  chart: [BASE, { x: 6, y: 20, z: 4, w: 6, d: 6, h: 7, m: 'paper' }, { x: 15, y: 15, z: 4, w: 6, d: 6, h: 12, m: 'paper' }, { x: 24, y: 10, z: 4, w: 6, d: 6, h: 19, m: 'accent' }],
  // Preparador: a prancheta com o formulário e a presilha.
  form: [BASE, { x: 7, y: 5, z: 4, w: 22, d: 27, h: 2, m: 'dark' }, {
    x: 9, y: 9, z: 6, w: 18, d: 21, h: 1.2, m: 'paper',
    lines: [12, 17, 22].flatMap((y, i) => [
      { pts: [[11, y, 7.3], [14, y, 7.3], [14, y + 3, 7.3], [11, y + 3, 7.3], [11, y, 7.3]]        , tone: (i === 0 ? 'accent' : 'soft')                , w: 1.1 },
      { pts: [[16, y + 1.5, 7.3], [25, y + 1.5, 7.3]]        , tone: 'ink'         , w: 1.2 }]),
  }, { x: 14, y: 4, z: 6, w: 8, d: 4, h: 2.5, m: 'accent' }],
  // Envio: o envelope saindo, com a seta subindo.
  send: [BASE, {
    x: 6, y: 10, z: 4, w: 24, d: 16, h: 3, m: 'paper',
    lines: [{ pts: [[6, 10, 7], [18, 19, 7], [30, 10, 7]], tone: 'soft', w: 1.2 }, { pts: [[20, 14, 12], [30, 4, 22]], tone: 'accent', w: 2.4 }, { pts: [[25, 4, 22], [30, 4, 22], [30, 9, 22]], tone: 'accent', w: 2.4 }],
  }],
  // Verificador: o recibo em pé com o visto.
  receipt: [BASE, {
    x: 10, y: 16, z: 4, w: 16, d: 1.6, h: 24, m: 'paper',
    lines: [{ pts: [[13, 17.6, 24], [23, 17.6, 24]], tone: 'ink', w: 1.2 }, { pts: [[13, 17.6, 20], [21, 17.6, 20]], tone: 'ink', w: 1.2 },
      { pts: [[13, 17.6, 12], [16, 17.6, 9], [23, 17.6, 16]], tone: 'accent', w: 2.6 }],
  }],
  // Rastreador: a bandeja de entrada com o e-mail.
  inbox: [BASE, { x: 5, y: 6, z: 4, w: 26, d: 24, h: 4, m: 'dark' }, {
    x: 9, y: 10, z: 8, w: 18, d: 14, h: 2, m: 'paper',
    lines: [{ pts: [[9, 10, 10], [18, 18, 10], [27, 10, 10]], tone: 'accent', w: 1.8 }],
  }],
  // Com você: a fila de pendências, a de cima marcada.
  queue: [BASE, { x: 8, y: 8, z: 4, w: 20, d: 20, h: 2, m: 'paper' }, { x: 9, y: 7, z: 6.5, w: 20, d: 20, h: 2, m: 'paper' }, {
    x: 10, y: 6, z: 9, w: 20, d: 20, h: 2, m: 'paper',
    lines: [{ pts: [[13, 10, 11], [26, 10, 11]], tone: 'ink', w: 1.3 }, { pts: [[13, 15, 11], [22, 15, 11]], tone: 'soft', w: 1.3 }],
  }, { x: 24, y: 19, z: 11, w: 5, d: 5, h: 5, m: 'accent' }],
  // Modelo: o chip com os pinos e o núcleo na cor da marca.
  model: [BASE, ...[9, 15, 21].flatMap((v) => [{ x: v, y: 5, z: 4, w: 2, d: 3, h: 1.5, m: 'paper'        }, { x: 5, y: v, z: 4, w: 3, d: 2, h: 1.5, m: 'paper'        }]),
    { x: 8, y: 8, z: 4, w: 20, d: 20, h: 4, m: 'dark' },
    ...[9, 15, 21].flatMap((v) => [{ x: v, y: 28, z: 4, w: 2, d: 3, h: 1.5, m: 'paper'        }, { x: 28, y: v, z: 4, w: 3, d: 2, h: 1.5, m: 'paper'        }]),
    { x: 13, y: 13, z: 8, w: 10, d: 10, h: 2, m: 'accent' }],
};

export const ISO_NAMES = Object.keys(ART);

/** As faces visíveis (topo, esquerda e direita) de cada bloco, de trás para a frente, e as linhas de cada um por cima dele. */
export function isoArt(name        )         {
  const boxes = ART[name] ?? ART['model'];
  const faces            = [];
  for (const b of boxes) {
    const { x, y, z, w, d, h } = b;
    faces.push({ d: poly([[x, y + d, z], [x + w, y + d, z], [x + w, y + d, z + h], [x, y + d, z + h]]), cls: `iso-f iso-l iso-${b.m}` });
    faces.push({ d: poly([[x + w, y, z], [x + w, y + d, z], [x + w, y + d, z + h], [x + w, y, z + h]]), cls: `iso-f iso-r iso-${b.m}` });
    faces.push({ d: poly([[x, y, z + h], [x + w, y, z + h], [x + w, y + d, z + h], [x, y + d, z + h]]), cls: `iso-f iso-t iso-${b.m}` });
    for (const l of b.lines ?? []) faces.push({ d: open(l.pts), cls: `iso-line iso-${l.tone}`, w: l.w ?? 1.4 });
  }
  return { faces, viewBox: '-34 -24 68 62' };
}

export { ART, P };
