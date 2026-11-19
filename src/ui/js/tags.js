// Tags dos vídeos: campo de edição (chips) usado no modal "Editar vídeo".
import { h } from './util.js';
import { icone } from './icones.js';

export const MAX_TAGS = 20;
export const MAX_TAG = 30;

/** "  aula   ao vivo " -> "aula ao vivo". */
const limpar = (t) => (t ?? '').replace(/\s+/g, ' ').trim();
const mesma = (a, b) => a.toLowerCase() === b.toLowerCase(); // como o C# (OrdinalIgnoreCase)

/**
 * Campo de tags: digite e tecle Enter (ou vírgula) para adicionar; Backspace no campo vazio remove a última.
 * Sugere as tags já usadas no acervo e reaproveita a grafia delas ("PYTHON" -> "Python").
 * Devolve { el, valores() } — valores() inclui o texto ainda não confirmado no campo.
 */
export function campoTags({ id, valores = [], sugestoes = [] }) {
  let tags = [...valores];
  const idLista = `${id}-sugestoes`;
  const idAjuda = `${id}-ajuda`;

  const anuncio = h('span', { class: 'sr-only', 'aria-live': 'polite' });
  const lista = h('ul', { class: 'contents', 'aria-label': 'Tags do vídeo' });
  const entrada = h('input', {
    id, class: 'campo-tags-entrada', list: idLista, maxlength: MAX_TAG, autocomplete: 'off', spellcheck: 'false',
    'aria-describedby': idAjuda,
  });
  const caixa = h('div', { class: 'campo campo-tags' }, lista, entrada);
  caixa.addEventListener('click', (e) => { if (e.target === caixa) entrada.focus(); });
  const datalist = h('datalist', { id: idLista });
  const ajuda = h('p', { id: idAjuda, class: 'mt-1.5 text-xs text-suave' });

  function desenhar() {
    lista.replaceChildren(...tags.map((t) => h('li', { class: 'chip chip-tag' },
      h('span', {}, t),
      h('button', {
        type: 'button', class: 'chip-tag-remover', 'aria-label': `Remover a tag ${t}`, title: 'Remover',
        onclick: () => remover(t),
      }, icone('close', 14)))));
    // Sugestões: só as que ainda não estão no vídeo.
    datalist.replaceChildren(...sugestoes.filter((s) => !tags.some((t) => mesma(t, s))).map((s) => h('option', { value: s })));
    entrada.placeholder = tags.length ? '' : 'Ex.: python, iniciante, aula ao vivo';
    ajuda.textContent = tags.length >= MAX_TAGS
      ? `Limite de ${MAX_TAGS} tags atingido.`
      : 'Tecle Enter ou vírgula para adicionar. As tags servem para filtrar o catálogo.';
  }

  function adicionar(texto) {
    const novas = [];
    for (const parte of (texto ?? '').split(',')) {
      let t = limpar(parte).slice(0, MAX_TAG);
      if (!t || tags.length >= MAX_TAGS || tags.some((x) => mesma(x, t))) continue;
      t = sugestoes.find((s) => mesma(s, t)) ?? t;
      tags.push(t);
      novas.push(t);
    }
    entrada.value = '';
    if (novas.length) {
      anuncio.textContent = `Tag adicionada: ${novas.join(', ')}.`;
      desenhar();
    }
  }

  function remover(t) {
    tags = tags.filter((x) => x !== t);
    anuncio.textContent = `Tag removida: ${t}.`;
    desenhar();
    entrada.focus();
  }

  entrada.addEventListener('keydown', (e) => {
    if ((e.key === 'Enter' || e.key === ',') && limpar(entrada.value)) {
      e.preventDefault(); // com texto, Enter adiciona a tag em vez de enviar o formulário
      adicionar(entrada.value);
    } else if (e.key === ',') {
      e.preventDefault();
    } else if (e.key === 'Backspace' && !entrada.value && tags.length) {
      remover(tags[tags.length - 1]);
    }
  });
  // Escolher uma sugestão da lista já adiciona a tag.
  entrada.addEventListener('input', (e) => {
    if (e.inputType === 'insertReplacementText') adicionar(entrada.value);
    else if (entrada.value.includes(',')) adicionar(entrada.value);
  });
  entrada.addEventListener('blur', () => adicionar(entrada.value));

  desenhar();
  return {
    el: h('div', {}, caixa, datalist, ajuda, anuncio),
    valores: () => { adicionar(entrada.value); return [...tags]; },
  };
}
