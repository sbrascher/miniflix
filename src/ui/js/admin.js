import { bridge } from './bridge.js';
import { h, normalizar, fmtDuracao, fmtBytes, fmtDataHora, plural } from './util.js';
import { icone } from './icones.js';
import { abrirModal, confirmar, toast } from './componentes.js';
import { cardImportar } from './importar.js';
import { cardYoutube } from './youtube.js';
import { criarPainelFila } from './fila.js';
import { imagemCapa } from './catalogo.js';
import { esquecerProgresso } from './progresso.js';
import { secaoCapa } from './capa.js';

let filtro = ''; // preservado entre re-renderizações

/** Tela 3 — Área administrativa. */
export function renderAdmin(raiz, estado) {
  const corpoTabela = h('div');
  const contador = h('span', { class: 'chip font-medium text-suave' });

  const campoFiltro = h('input', {
    type: 'search', class: 'campo w-full pl-11 sm:w-72', placeholder: 'Filtrar por título ou categoria…',
    value: filtro, 'aria-label': 'Filtrar acervo',
  });
  campoFiltro.addEventListener('input', () => { filtro = campoFiltro.value; renderTabela(); });

  const btnRecarregar = h('button', { type: 'button', class: 'btn', title: 'Ler novamente a pasta /videos' },
    icone('refresh', 18), 'Recarregar pasta');
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
      corpoTabela.replaceChildren(h('p', { class: 'px-6 py-14 text-center text-suave' },
        'Nenhum vídeo no acervo ainda. Importe um arquivo do computador ou baixe do YouTube.'));
      return;
    }
    if (lista.length === 0) {
      corpoTabela.replaceChildren(h('p', { class: 'px-6 py-14 text-center text-suave' }, 'Nenhum vídeo corresponde ao filtro.'));
      return;
    }

    const th = (texto, classe = '') => h('th', { scope: 'col', class: classe }, texto);
    corpoTabela.replaceChildren(h('table', { class: 'tabela' },
      h('thead', {},
        h('tr', {},
          th('Vídeo'), th('Categoria', 'hidden sm:table-cell'), th('Duração', 'hidden md:table-cell'),
          th('Tamanho', 'hidden lg:table-cell'), th('Adicionado', 'hidden lg:table-cell'),
          th('Ações', 'text-right'))),
      h('tbody', {}, lista.map((v) => linha(v, estado)))));
  }

  raiz.replaceChildren(h('div', { class: 'mx-auto max-w-[1400px] px-4 pb-16 pt-6 sm:px-6' },
    h('a', { href: '#/', class: 'btn' }, icone('arrow_back', 18), 'Voltar ao catálogo'),
    h('h1', { class: 'mt-7 font-display text-3xl font-bold' }, 'Administração'),

    // Adicionar vídeos
    h('div', { class: 'mt-7 grid gap-6 md:grid-cols-2' },
      cardImportar(estado),
      cardYoutube(estado)),

    criarPainelFila(),

    // Acervo
    h('section', { class: 'mt-12', 'aria-labelledby': 'titulo-acervo' },
      h('div', { class: 'mb-5 flex flex-wrap items-center gap-3' },
        h('h2', { id: 'titulo-acervo', class: 'flex items-center gap-3 font-display text-xl font-bold' }, 'Acervo', contador),
        h('div', { class: 'ml-auto flex flex-wrap items-center gap-3' },
          h('label', { class: 'relative' },
            h('span', { class: 'pointer-events-none absolute left-3.5 top-1/2 flex -translate-y-1/2 text-suave' }, icone('search', 18)),
            campoFiltro),
          btnRecarregar)),
      h('div', { class: 'painel overflow-hidden' }, corpoTabela))));

  renderTabela();
}

/** Miniatura na bandeja rebaixada (mesmo fallback das capas do catálogo). */
function miniatura(v) {
  const capa = imagemCapa(v, 20);
  if (capa.tagName === 'IMG') capa.className = 'h-full w-full object-cover';
  return h('div', { class: 'bandeja h-[58px] w-[104px] shrink-0 overflow-hidden p-0' }, capa);
}

function linha(v, estado) {
  const acao = (icon, rotulo, attrs) =>
    h(attrs.href ? 'a' : 'button', { type: attrs.href ? null : 'button', class: 'btn-icone', 'aria-label': `${rotulo}: ${v.titulo}`, title: rotulo, ...attrs },
      icone(icon, 20));

  return h('tr', {},
    h('td', {},
      h('div', { class: 'flex items-center gap-4' },
        miniatura(v),
        h('div', { class: 'min-w-0' },
          h('p', { class: 'line-clamp-2 font-medium leading-snug' }, v.titulo),
          h('p', { class: 'mt-0.5 truncate font-mono text-xs text-suave' }, v.slug),
          h('p', { class: 'mt-0.5 text-xs text-suave sm:hidden' }, v.categoria)))),
    h('td', { class: 'hidden sm:table-cell' }, h('span', { class: 'chip' }, v.categoria)),
    h('td', { class: 'hidden text-sm tabular-nums text-suave md:table-cell' }, fmtDuracao(v.duracaoSegundos) || '—'),
    h('td', { class: 'hidden text-sm tabular-nums text-suave lg:table-cell' }, fmtBytes(v.tamanhoBytes)),
    h('td', { class: 'hidden text-sm tabular-nums text-suave lg:table-cell' }, fmtDataHora(v.dataAdicionado) || '—'),
    h('td', {},
      h('div', { class: 'flex justify-end gap-1.5' },
        acao('play_arrow', 'Assistir', { href: `#/video/${encodeURIComponent(v.slug)}` }),
        acao('edit', 'Editar', { onclick: () => editar(v, estado) }),
        acao('delete', 'Excluir', { onclick: () => excluir(v), class: 'btn-icone btn-icone-perigo' }))));
}

async function editar(v, estado) {
  const erro = h('p', { id: 'ed-erro', class: 'alerta-erro hidden', role: 'alert' });
  const titulo = h('input', { id: 'ed-titulo', class: 'campo w-full', required: true, maxlength: 200, value: v.titulo, 'aria-describedby': 'ed-erro' });
  const categoria = h('input', {
    id: 'ed-categoria', class: 'campo w-full', maxlength: 60, list: 'ed-categorias', autocomplete: 'off',
    value: v.categoria === 'Sem categoria' ? '' : v.categoria, placeholder: 'Sem categoria',
  });
  const descricao = h('textarea', { id: 'ed-descricao', class: 'campo w-full', rows: 4, maxlength: 10000 });
  const capa = secaoCapa(v);
  descricao.value = v.descricao ?? '';

  const salvar = h('button', { type: 'submit', form: 'form-editar', class: 'btn-primario' }, 'Salvar alterações');

  const form = h('form', { id: 'form-editar', class: 'space-y-5', novalidate: true },
    capa.el,
    h('div', {}, h('label', { for: 'ed-titulo', class: 'rotulo' }, 'Título'), titulo),
    h('div', {},
      h('label', { for: 'ed-categoria', class: 'rotulo' }, 'Categoria'),
      categoria,
      h('datalist', { id: 'ed-categorias' }, estado.categorias.filter((c) => c !== 'Sem categoria').map((c) => h('option', { value: c }))),
      h('p', { class: 'mt-1.5 text-xs text-suave' }, 'Escolha uma existente ou digite um nome novo para criar a categoria.')),
    h('div', {}, h('label', { for: 'ed-descricao', class: 'rotulo' }, 'Descrição'), descricao),
    h('p', { class: 'text-xs text-suave' }, 'Pasta: ', h('code', { class: 'font-mono' }, `videos/${v.slug}`), ' (o nome da pasta não muda).'),
    erro);

  const salvo = abrirModal({
    titulo: 'Editar vídeo',
    largura: 'max-w-2xl',
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (!titulo.value.trim()) {
          erro.textContent = 'O título não pode ficar vazio.';
          erro.classList.remove('hidden');
          titulo.setAttribute('aria-invalid', 'true');
          titulo.focus();
          return;
        }
        salvar.disabled = true;
        erro.classList.add('hidden');
        titulo.removeAttribute('aria-invalid');
        try {
          await bridge.call('video.editar', {
            slug: v.slug, titulo: titulo.value, categoria: categoria.value, descricao: descricao.value,
          });
          await capa.confirmar();
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
  const ok = await salvo;
  capa.liberar();                  // solta o arquivo de vídeo usado pelo seletor de quadro
  if (!ok) await capa.descartar(); // cancelou: a capa atual continua
  else toast('Alterações salvas.');
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
    esquecerProgresso(v.slug);
    toast('Vídeo excluído.');
  } catch (e) {
    toast(e.message, 'erro');
  }
}
