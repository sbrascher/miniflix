// Painel "Categorias" da administração: renomear uma categoria muda todos os vídeos dela de uma vez.
import { bridge } from './bridge.js';
import { h, plural } from './util.js';
import { icone } from './icones.js';
import { abrirModal, toast } from './componentes.js';

const SEM_CATEGORIA = 'Sem categoria';

export function painelCategorias(estado) {
  const contagem = new Map();
  for (const v of estado.videos) contagem.set(v.categoria, (contagem.get(v.categoria) ?? 0) + 1);

  const itens = estado.categorias.map((c) => h('li', { class: 'bandeja flex items-center gap-3 py-1.5 pl-4 pr-1.5' },
    h('span', { class: `min-w-0 flex-1 truncate font-medium ${c === SEM_CATEGORIA ? 'italic text-suave' : ''}` }, c),
    h('span', { class: 'chip shrink-0 font-medium text-suave' }, plural(contagem.get(c) ?? 0, 'vídeo', 'vídeos')),
    h('button', {
      type: 'button', class: 'btn-icone', title: c === SEM_CATEGORIA ? 'Dar uma categoria' : 'Renomear',
      'aria-label': c === SEM_CATEGORIA ? 'Dar uma categoria aos vídeos sem categoria' : `Renomear a categoria ${c}`,
      onclick: () => renomear(c, contagem.get(c) ?? 0, estado),
    }, icone('edit', 20))));

  return h('section', { class: 'mt-12', 'aria-labelledby': 'titulo-categorias' },
    h('div', { class: 'mb-5 flex flex-wrap items-center gap-3' },
      h('h2', { id: 'titulo-categorias', class: 'flex items-center gap-3 font-display text-xl font-bold' }, 'Categorias',
        h('span', { class: 'chip font-medium text-suave' }, plural(estado.categorias.length, 'categoria', 'categorias'))),
      h('p', { class: 'text-sm text-suave' }, 'Renomear uma categoria muda todos os vídeos dela de uma vez.')),
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
    const igual = semCategoria ? !nome || nome.toLowerCase() === SEM_CATEGORIA.toLowerCase() : nome === antiga;
    salvar.disabled = igual;
  }
  campo.addEventListener('input', atualizarAviso);

  const form = h('form', { id: 'form-categoria', class: 'space-y-4', novalidate: true },
    h('div', {},
      h('label', { for: 'cat-nome', class: 'rotulo' }, semCategoria ? 'Categoria para os vídeos sem categoria' : 'Novo nome'),
      campo,
      h('datalist', { id: 'cat-sugestoes' }, outras.map((c) => h('option', { value: c }))),
      aviso),
    erro);

  const r = abrirModal({
    titulo: semCategoria ? 'Dar uma categoria' : `Renomear “${antiga}”`,
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (salvar.disabled) return;
        salvar.disabled = true;
        erro.classList.add('hidden');
        try {
          fechar(await bridge.call('categoria.renomear', { antiga, nova: campo.value }));
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
  if (resultado) toast(`Categoria alterada em ${plural(resultado.alterados, 'vídeo', 'vídeos')}.`);
}
