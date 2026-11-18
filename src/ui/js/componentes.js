// Componentes de interface reutilizáveis: modal (<dialog>) e avisos rápidos (toast).
// Nunca usamos alert()/confirm(): bloqueiam o WebView2 e quebram a aparência do app.
import { h } from './util.js';
import { icone } from './icones.js';

/**
 * Abre um modal. `corpo` é um nó (ou lista); `acoes` recebe a função fechar(valor) e devolve os botões.
 * Retorna uma Promise com o valor passado a fechar() (ou undefined se fechado por Esc/clique fora).
 */
let contadorModais = 0;

export function abrirModal({ titulo, corpo, acoes, largura = 'max-w-lg' }) {
  return new Promise((resolve) => {
    const idTitulo = `modal-titulo-${++contadorModais}`;
    const dlg = h('dialog', { class: `modal ${largura}`, 'aria-labelledby': idTitulo });
    let valor;
    const fechar = (v) => { valor = v; dlg.close(); };

    dlg.append(
      h('div', { class: 'flex items-center justify-between gap-4 px-6 pb-3 pt-5' },
        h('h2', { id: idTitulo, class: 'font-display text-lg font-bold' }, titulo),
        h('button', { type: 'button', class: 'btn-icone -mr-2', 'aria-label': 'Fechar', onclick: () => fechar() },
          icone('close', 20))),
      h('div', { class: 'px-6 pb-6 pt-2' }, corpo),
      h('div', { class: 'modal-rodape flex justify-end gap-3 px-6 py-4' }, acoes(fechar)),
    );

    dlg.addEventListener('click', (e) => { if (e.target === dlg) fechar(); }); // clique no fundo
    dlg.addEventListener('close', () => { dlg.remove(); resolve(valor); });
    document.body.append(dlg);
    dlg.showModal();
  });
}

/** Modal de confirmação. Resolve true/false. */
export function confirmar({ titulo, mensagem, textoConfirmar = 'Confirmar', perigoso = false }) {
  return abrirModal({
    titulo,
    corpo: h('p', { class: 'leading-relaxed text-suave' }, mensagem),
    acoes: (fechar) => [
      // Em ações perigosas o foco começa em "Cancelar": um Enter acidental não apaga nada.
      h('button', { type: 'button', class: 'btn', autofocus: perigoso, onclick: () => fechar(false) }, 'Cancelar'),
      h('button', {
        type: 'button', autofocus: !perigoso,
        class: perigoso ? 'btn-perigo' : 'btn-primario',
        onclick: () => fechar(true),
      }, textoConfirmar),
    ],
  }).then(Boolean);
}

let pilhaToasts;
/** Aviso rápido no canto da tela. tipo: 'ok' | 'erro'. */
export function toast(mensagem, tipo = 'ok') {
  if (!pilhaToasts) {
    pilhaToasts = h('div', { class: 'pointer-events-none fixed bottom-6 right-6 z-50 flex flex-col items-end gap-3' });
    document.body.append(pilhaToasts);
  }
  const erro = tipo === 'erro';
  // Erros são anunciados imediatamente por leitores de tela; confirmações, com educação.
  const el = h('div', { class: 'toast pointer-events-auto', role: erro ? 'alert' : 'status' },
    h('span', { class: `flex ${erro ? 'text-perigo' : 'text-sucesso'}` }, icone(erro ? 'error' : 'check_circle', 22, true)),
    mensagem);
  pilhaToasts.append(el);
  setTimeout(() => { el.classList.add('opacity-0', 'translate-y-2'); setTimeout(() => el.remove(), 300); }, erro ? 6000 : 3000);
}
