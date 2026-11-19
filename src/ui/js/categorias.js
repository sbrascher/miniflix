// Painel "Categorias" da administração: editar o nome (muda todos os vídeos dela de uma vez) e a descrição.
import { bridge } from './bridge.js';
import { h, plural } from './util.js';
import { icone } from './icones.js';
import { abrirModal, toast } from './componentes.js';

const SEM_CATEGORIA = 'Sem categoria';

export function painelCategorias(estado) {
  const contagem = new Map();
  for (const v of estado.videos) contagem.set(v.categoria, (contagem.get(v.categoria) ?? 0) + 1);

  const descricoes = estado.descricoesCategorias ?? {};
  const itens = estado.categorias.map((c) => h('li', { class: 'bandeja flex items-center gap-3 py-1.5 pl-4 pr-1.5' },
    h('div', { class: 'min-w-0 flex-1' },
      h('p', { class: `truncate font-medium ${c === SEM_CATEGORIA ? 'italic text-suave' : ''}` }, c),
      descricoes[c] ? h('p', { class: 'truncate text-xs text-suave', title: descricoes[c] }, descricoes[c]) : null),
    h('span', { class: 'chip shrink-0 font-medium text-suave' }, plural(contagem.get(c) ?? 0, 'vídeo', 'vídeos')),
    h('button', {
      type: 'button', class: 'btn-icone', title: c === SEM_CATEGORIA ? 'Dar uma categoria' : 'Editar',
      'aria-label': c === SEM_CATEGORIA ? 'Dar uma categoria aos vídeos sem categoria' : `Editar a categoria ${c}`,
      onclick: () => renomear(c, contagem.get(c) ?? 0, estado),
    }, icone('edit', 20))));

  return h('section', { class: 'mt-12', 'aria-labelledby': 'titulo-categorias' },
    h('div', { class: 'mb-5 flex flex-wrap items-center gap-3' },
      h('h2', { id: 'titulo-categorias', class: 'flex items-center gap-3 font-display text-xl font-bold' }, 'Categorias',
        h('span', { class: 'chip font-medium text-suave' }, plural(estado.categorias.length, 'categoria', 'categorias'))),
      h('p', { class: 'text-sm text-suave' }, 'Mudar o nome de uma categoria muda todos os vídeos dela de uma vez. A descrição aparece no catálogo.')),
    h('div', { class: 'painel p-5' },
      itens.length
        ? h('ul', { class: 'grid gap-3 md:grid-cols-2 xl:grid-cols-3' }, itens)
        : h('p', { class: 'py-6 text-center text-suave' }, 'Nenhuma categoria ainda.')));
}

async function renomear(antiga, quantidade, estado) {
  const semCategoria = antiga === SEM_CATEGORIA;
  const outras = estado.categorias.filter((c) => c !== antiga && c !== SEM_CATEGORIA);

  const campo = h('input', {
    id: 'cat-nome', class: 'campo w-full', maxlength: 60, list: 'cat-sugestoes', autocomplete: 'off',
    value: semCategoria ? '' : antiga, placeholder: SEM_CATEGORIA, 'aria-describedby': 'cat-aviso',
  });
  const aviso = h('p', { id: 'cat-aviso', class: 'mt-2 text-sm text-suave', 'aria-live': 'polite' });
  const descricaoAtual = (estado.descricoesCategorias ?? {})[antiga] ?? '';
  const descricao = h('textarea', {
    id: 'cat-descricao', class: 'campo w-full', rows: 3, maxlength: 500, 'aria-describedby': 'cat-descricao-ajuda',
    placeholder: 'Ex.: Aulas gravadas do curso de Python, em ordem.',
  });
  descricao.value = descricaoAtual;
  const ajudaDescricao = h('p', { id: 'cat-descricao-ajuda', class: 'mt-1 text-xs text-suave' });
  const atualizarContador = () => { ajudaDescricao.textContent = `Opcional. Aparece abaixo do nome da categoria no catálogo. ${descricao.value.length}/500`; };
  descricao.addEventListener('input', () => { atualizarContador(); atualizarAviso(); });
  atualizarContador();
  const erro = h('p', { class: 'alerta-erro hidden', role: 'alert' });
  const salvar = h('button', { type: 'submit', form: 'form-categoria', class: 'btn-primario' }, 'Salvar');

  // Explica o que vai acontecer conforme o nome digitado (renomear, unir ou tirar a categoria).
  function atualizarAviso() {
    const nome = campo.value.trim();
    const n = plural(quantidade, 'vídeo', 'vídeos');
    const uniao = outras.find((c) => c.toLowerCase() === nome.toLowerCase());
    if (!nome || nome.toLowerCase() === SEM_CATEGORIA.toLowerCase()) {
      aviso.textContent = semCategoria ? 'Digite o nome da categoria.' : `${n} ficarão sem categoria.`;
    } else if (uniao) {
      aviso.textContent = `“${uniao}” já existe: os ${n} serão juntados a ela.`;
    } else {
      aviso.textContent = `${n} passarão para “${nome}”.`;
    }
    const igual = semCategoria
      ? !nome || nome.toLowerCase() === SEM_CATEGORIA.toLowerCase()
      : nome === antiga && descricao.value.trim() === descricaoAtual;
    salvar.disabled = igual;
    if (!semCategoria && nome === antiga) aviso.textContent = 'O nome continua o mesmo.';
  }
  campo.addEventListener('input', atualizarAviso);

  const form = h('form', { id: 'form-categoria', class: 'space-y-4', novalidate: true },
    h('div', {},
      h('label', { for: 'cat-nome', class: 'rotulo' }, semCategoria ? 'Categoria para os vídeos sem categoria' : 'Nome'),
      campo,
      h('datalist', { id: 'cat-sugestoes' }, outras.map((c) => h('option', { value: c }))),
      aviso),
    h('div', {},
      h('label', { for: 'cat-descricao', class: 'rotulo' }, 'Descrição ', h('span', { class: 'font-normal text-suave' }, '(opcional)')),
      descricao,
      ajudaDescricao),
    erro);

  const r = abrirModal({
    titulo: semCategoria ? 'Dar uma categoria' : `Editar categoria “${antiga}”`,
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (salvar.disabled) return;
        salvar.disabled = true;
        erro.classList.add('hidden');
        try {
          const mudouDescricao = descricao.value.trim() !== descricaoAtual;
          fechar({
            ...(await bridge.call('categoria.editar', {
              antiga, nova: campo.value, ...(mudouDescricao ? { descricao: descricao.value } : {}),
            })),
            soDescricao: campo.value.trim() === antiga,
          });
        } catch (ex) {
          erro.textContent = ex.message;
          erro.classList.remove('hidden');
          salvar.disabled = false;
        }
      });
      return [h('button', { type: 'button', class: 'btn', onclick: () => fechar() }, 'Cancelar'), salvar];
    },
  });
  atualizarAviso();
  campo.focus();
  campo.select();
  const resultado = await r;
  if (resultado) toast(resultado.soDescricao ? 'Categoria salva.' : `Categoria alterada em ${plural(resultado.alterados, 'vídeo', 'vídeos')}.`);
}
