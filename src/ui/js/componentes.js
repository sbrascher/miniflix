// Componentes de interface reutilizáveis: modal (<dialog>) e avisos rápidos (toast).
// Nunca usamos alert()/confirm(): bloqueiam o WebView2 e quebram a aparência do app.
import { h } from './util.js';
import { icone } from './icones.js';

/**
 * Abre um modal. `corpo` é um nó (ou lista); `acoes` recebe a função fechar(valor) e devolve os botões.
 * Retorna uma Promise com o valor passado a fechar() (ou undefined se fechado por Esc/clique fora).
 */
export function abrirModal({ titulo, corpo, acoes, largura = 'max-w-lg' }) {
  return new Promise((resolve) => {
    const dlg = h('dialog', {
      class: `m-auto w-[calc(100%-2rem)] ${largura} rounded-2xl bg-superficie p-0 text-texto shadow-2xl ring-1 ring-borda ` +
             'backdrop:bg-black/60 backdrop:backdrop-blur-sm open:animate-[entrar_.15s_ease-out]',
      'aria-labelledby': 'modal-titulo',
    });
    let valor;
    const fechar = (v) => { valor = v; dlg.close(); };

    dlg.append(
      h('div', { class: 'flex items-start justify-between gap-4 border-b border-borda px-6 py-4' },
        h('h2', { id: 'modal-titulo', class: 'font-display text-lg font-bold' }, titulo),
        h('button', { type: 'button', class: 'btn-icone -mr-2 -mt-1 h-8 w-8', 'aria-label': 'Fechar', onclick: () => fechar() },
          icone('close', 16))),
      h('div', { class: 'px-6 py-5' }, corpo),
      h('div', { class: 'flex justify-end gap-2 border-t border-borda bg-superficie-2/50 px-6 py-4' }, acoes(fechar)),
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
    pilhaToasts = h('div', {
      class: 'pointer-events-none fixed bottom-5 right-5 z-50 flex flex-col items-end gap-2',
      role: 'status', 'aria-live': 'polite',
    });
    document.body.append(pilhaToasts);
  }
  const erro = tipo === 'erro';
  const el = h('div', {
    class: 'pointer-events-auto flex max-w-sm items-center gap-3 rounded-xl bg-superficie px-4 py-3 text-sm font-medium ' +
           'shadow-xl ring-1 ring-borda transition duration-300 animate-[entrar_.2s_ease-out]',
  },
    h('span', { class: erro ? 'text-destaque' : 'text-emerald-500' }, icone(erro ? 'warning' : 'check_circle', 20, true)),
    mensagem);
  pilhaToasts.append(el);
  setTimeout(() => { el.classList.add('opacity-0', 'translate-y-2'); setTimeout(() => el.remove(), 300); }, erro ? 6000 : 3000);
}
