import { bridge } from './bridge.js';
import { h, normalizar, fmtDuracao, fmtBytes, fmtDataHora, matiz, plural } from './util.js';
import { icone } from './icones.js';
import { abrirModal, confirmar, toast } from './componentes.js';

let filtro = ''; // preservado entre re-renderizações

/** Tela 3 — Área administrativa. */
export function renderAdmin(raiz, estado) {
  const corpoTabela = h('div');
  const contador = h('span', { class: 'text-sm font-normal text-suave' });

  const campoFiltro = h('input', {
    type: 'search', class: 'campo w-full pl-10 sm:w-72', placeholder: 'Filtrar por título ou categoria…',
    value: filtro, 'aria-label': 'Filtrar acervo',
  });
  campoFiltro.addEventListener('input', () => { filtro = campoFiltro.value; renderTabela(); });

  const btnRecarregar = h('button', { type: 'button', class: 'btn', title: 'Ler novamente a pasta /videos' },
    icone('refresh', 16), 'Recarregar pasta');
  btnRecarregar.addEventListener('click', async () => {
    btnRecarregar.disabled = true;
    try {
      await bridge.call('catalogo.recarregar');
      toast('Pasta de vídeos lida novamente.');
    } catch (e) {
      toast(e.message, 'erro');
    } finally {
      btnRecarregar.disabled = false;
    }
  });

  function renderTabela() {
    const termos = normalizar(filtro).split(/\s+/).filter(Boolean);
    const lista = estado.videos.filter((v) => {
      const alvo = normalizar(`${v.titulo} ${v.categoria}`);
      return termos.every((t) => alvo.includes(t));
    });
    contador.textContent = filtro.trim()
      ? `${lista.length} de ${plural(estado.videos.length, 'vídeo', 'vídeos')}`
      : plural(estado.videos.length, 'vídeo', 'vídeos');

    if (estado.videos.length === 0) {
      corpoTabela.replaceChildren(h('p', { class: 'px-6 py-12 text-center text-suave' },
        'Nenhum vídeo no acervo ainda. Em breve você poderá adicioná-los por aqui.'));
      return;
    }
    if (lista.length === 0) {
      corpoTabela.replaceChildren(h('p', { class: 'px-6 py-12 text-center text-suave' }, 'Nenhum vídeo corresponde ao filtro.'));
      return;
    }

    const th = (texto, classe = '') => h('th', { scope: 'col', class: `px-4 py-3 text-left text-xs font-semibold uppercase tracking-wide text-suave ${classe}` }, texto);
    corpoTabela.replaceChildren(h('table', { class: 'w-full border-collapse' },
      h('thead', { class: 'border-b border-borda bg-superficie-2/60' },
        h('tr', {},
          th('Vídeo'), th('Categoria', 'hidden sm:table-cell'), th('Duração', 'hidden md:table-cell'),
          th('Tamanho', 'hidden lg:table-cell'), th('Adicionado', 'hidden lg:table-cell'),
          th('Ações', 'text-right'))),
      h('tbody', { class: 'divide-y divide-borda' }, lista.map((v) => linha(v, estado)))));
  }

  raiz.replaceChildren(h('div', { class: 'mx-auto max-w-[1400px] px-4 pb-16 pt-5 sm:px-6' },
    h('a', { href: '#/', class: 'btn' }, icone('arrow_back', 16), 'Voltar ao catálogo'),
    h('h1', { class: 'mt-6 font-display text-3xl font-bold' }, 'Administração'),

    // Adicionar vídeos (próximas etapas)
    h('div', { class: 'mt-6 grid gap-4 md:grid-cols-2' },
      cardEmBreve('link', 'Adicionar do YouTube', 'Cole a URL de um vídeo, escolha a categoria e acompanhe o download.'),
      cardEmBreve('upload', 'Importar do computador', 'Escolha arquivos de vídeo do PC; o MiniFlix converte para MP4 quando necessário.')),

    // Acervo
    h('section', { class: 'mt-10', 'aria-labelledby': 'titulo-acervo' },
      h('div', { class: 'mb-4 flex flex-wrap items-center gap-3' },
        h('h2', { id: 'titulo-acervo', class: 'flex items-baseline gap-3 font-display text-xl font-bold' }, 'Acervo', contador),
        h('div', { class: 'ml-auto flex flex-wrap items-center gap-2' },
          h('label', { class: 'relative' },
            h('span', { class: 'pointer-events-none absolute left-3 top-1/2 flex -translate-y-1/2 text-suave' }, icone('search', 18)),
            campoFiltro),
          btnRecarregar)),
      h('div', { class: 'overflow-hidden rounded-xl bg-superficie ring-1 ring-borda' }, corpoTabela))));

  renderTabela();
}

function cardEmBreve(icon, titulo, texto) {
  return h('div', { class: 'flex gap-4 rounded-xl bg-superficie p-5 ring-1 ring-borda', 'aria-disabled': 'true' },
    h('div', { class: 'h-fit rounded-lg bg-superficie-2 p-3 text-suave' }, icone(icon, 24)),
    h('div', {},
      h('h2', { class: 'flex items-center gap-2 font-semibold' }, titulo,
        h('span', { class: 'rounded-full bg-superficie-2 px-2 py-0.5 text-xs font-medium text-suave ring-1 ring-borda' }, 'Em breve')),
      h('p', { class: 'mt-1 text-sm text-suave' }, texto)));
}

function miniatura(v) {
  const base = 'h-[54px] w-24 shrink-0 overflow-hidden rounded-md bg-superficie-2 ring-1 ring-borda';
  if (v.capaUrl) {
    const img = h('img', { src: v.capaUrl, alt: '', loading: 'lazy', class: 'h-full w-full object-cover' });
    img.addEventListener('error', () => { img.remove(); }, { once: true });
    return h('div', { class: base }, img);
  }
  const m = matiz(v.slug);
  return h('div', { class: base, style: { background: `linear-gradient(135deg, hsl(${m} 45% 32%), hsl(${(m + 40) % 360} 55% 18%))` } });
}

function linha(v, estado) {
  const acao = (icon, rotulo, attrs) =>
    h(attrs.href ? 'a' : 'button', { type: attrs.href ? null : 'button', class: 'btn-icone h-9 w-9', 'aria-label': `${rotulo}: ${v.titulo}`, title: rotulo, ...attrs },
      icone(icon, 20));

  return h('tr', { class: 'transition hover:bg-superficie-2/50' },
    h('td', { class: 'px-4 py-3' },
      h('div', { class: 'flex items-center gap-3' },
        miniatura(v),
        h('div', { class: 'min-w-0' },
          h('p', { class: 'line-clamp-2 font-medium leading-snug' }, v.titulo),
          h('p', { class: 'mt-0.5 truncate font-mono text-xs text-suave' }, v.slug),
          h('p', { class: 'mt-0.5 text-xs text-suave sm:hidden' }, v.categoria)))),
    h('td', { class: 'hidden px-4 py-3 text-sm sm:table-cell' },
      h('span', { class: 'rounded-full bg-superficie-2 px-2.5 py-0.5 ring-1 ring-borda' }, v.categoria)),
    h('td', { class: 'hidden px-4 py-3 text-sm tabular-nums text-suave md:table-cell' }, fmtDuracao(v.duracaoSegundos) || '—'),
    h('td', { class: 'hidden px-4 py-3 text-sm tabular-nums text-suave lg:table-cell' }, fmtBytes(v.tamanhoBytes)),
    h('td', { class: 'hidden px-4 py-3 text-sm tabular-nums text-suave lg:table-cell' }, fmtDataHora(v.dataAdicionado) || '—'),
    h('td', { class: 'px-4 py-3' },
      h('div', { class: 'flex justify-end gap-1' },
        acao('play_arrow', 'Assistir', { href: `#/video/${encodeURIComponent(v.slug)}` }),
        acao('edit', 'Editar', { onclick: () => editar(v, estado) }),
        acao('delete', 'Excluir', { onclick: () => excluir(v), class: 'btn-icone h-9 w-9 hover:!text-red-500' }))));
}

async function editar(v, estado) {
  const erro = h('p', { class: 'mt-4 hidden rounded-lg bg-red-500/10 px-3 py-2 text-sm text-red-500', role: 'alert' });
  const titulo = h('input', { id: 'ed-titulo', class: 'campo w-full', required: true, maxlength: 200, value: v.titulo });
  const categoria = h('input', {
    id: 'ed-categoria', class: 'campo w-full', maxlength: 60, list: 'ed-categorias', autocomplete: 'off',
    value: v.categoria === 'Sem categoria' ? '' : v.categoria, placeholder: 'Sem categoria',
  });
  const descricao = h('textarea', { id: 'ed-descricao', class: 'campo h-auto w-full py-2 leading-relaxed', rows: 6, maxlength: 10000 });
  descricao.value = v.descricao ?? '';

  const salvar = h('button', { type: 'submit', form: 'form-editar', class: 'btn-primario' }, 'Salvar alterações');

  const form = h('form', { id: 'form-editar', class: 'space-y-4', novalidate: true },
    h('div', {}, h('label', { for: 'ed-titulo', class: 'rotulo' }, 'Título'), titulo),
    h('div', {},
      h('label', { for: 'ed-categoria', class: 'rotulo' }, 'Categoria'),
      categoria,
      h('datalist', { id: 'ed-categorias' }, estado.categorias.filter((c) => c !== 'Sem categoria').map((c) => h('option', { value: c }))),
      h('p', { class: 'mt-1 text-xs text-suave' }, 'Escolha uma existente ou digite um nome novo para criar a categoria.')),
    h('div', {}, h('label', { for: 'ed-descricao', class: 'rotulo' }, 'Descrição'), descricao),
    h('p', { class: 'text-xs text-suave' }, 'Pasta: ', h('code', { class: 'font-mono' }, `videos/${v.slug}`), ' (o nome da pasta não muda).'),
    erro);

  const salvo = abrirModal({
    titulo: 'Editar vídeo',
    largura: 'max-w-xl',
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (!titulo.value.trim()) {
          erro.textContent = 'O título não pode ficar vazio.';
          erro.classList.remove('hidden');
          titulo.focus();
          return;
        }
        salvar.disabled = true;
        erro.classList.add('hidden');
        try {
          await bridge.call('video.editar', {
            slug: v.slug, titulo: titulo.value, categoria: categoria.value, descricao: descricao.value,
          });
          fechar(true);
        } catch (ex) {
          erro.textContent = ex.message;
          erro.classList.remove('hidden');
          salvar.disabled = false;
        }
      });
      return [h('button', { type: 'button', class: 'btn', onclick: () => fechar(false) }, 'Cancelar'), salvar];
    },
  });
  titulo.focus();
  titulo.select();
  if (await salvo) toast('Alterações salvas.');
}

async function excluir(v) {
  const ok = await confirmar({
    titulo: 'Excluir vídeo?',
    mensagem: `“${v.titulo}” será apagado do disco (vídeo, capa e metadados). Esta ação não pode ser desfeita.`,
    textoConfirmar: 'Excluir definitivamente',
    perigoso: true,
  });
  if (!ok) return;
  try {
    await bridge.call('video.excluir', { slug: v.slug });
    toast('Vídeo excluído.');
  } catch (e) {
    toast(e.message, 'erro');
  }
}
