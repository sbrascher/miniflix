import { h, normalizar, fmtDuracao, matiz, plural } from './util.js';
import { icone } from './icones.js';
import { obterProgresso, emAndamento, fracao, listaContinuar, removerDeContinuar, restaurarProgresso } from './progresso.js';
import { toast } from './componentes.js';

/**
 * Filtra por categoria, por todos os termos da busca (sem acento/caixa) no título e por
 * tags: o vídeo precisa ter TODAS as tags selecionadas.
 */
export function filtrar(videos, { busca = '', categoria = '', tagsSelecionadas = [] }) {
  const termos = normalizar(busca).split(/\s+/).filter(Boolean);
  const tags = tagsSelecionadas.map((t) => t.toLowerCase());
  return videos.filter((v) => {
    const doVideo = (v.tags ?? []).map((t) => t.toLowerCase());
    return (!categoria || v.categoria === categoria) &&
      termos.every((t) => normalizar(v.titulo).includes(t)) &&
      tags.every((t) => doVideo.includes(t));
  });
}

/** Barra de tags clicáveis acima do catálogo (filtro). Só aparece quando o acervo tem tags. */
function barraTags(tags, selecionadas, aoAlternar) {
  const ativa = (t) => selecionadas.includes(t);
  return h('div', { class: 'mb-9 flex flex-wrap items-center gap-2.5', role: 'group', 'aria-labelledby': 'rotulo-tags' },
    h('span', { id: 'rotulo-tags', class: 'mr-1 flex items-center gap-1.5 text-sm font-semibold text-suave' },
      icone('sell', 18), 'Filtrar por tag'),
    tags.map((t) => h('button', {
      type: 'button', class: 'chip chip-filtro', 'aria-pressed': ativa(t) ? 'true' : 'false', 'data-tag': t,
      onclick: () => aoAlternar(t),
    }, ativa(t) ? icone('check', 14) : null, t)),
    selecionadas.length
      ? h('button', { type: 'button', class: 'ml-1 text-sm font-medium text-destaque hover:underline', onclick: () => aoAlternar(null) },
          'Limpar tags')
      : null);
}

/** Capa para vídeos sem imagem (ou com imagem quebrada). */
export function capaSubstituta(v, tamanhoIcone = 40) {
  return h('div', { class: 'capa-substituta', style: { '--matiz': matiz(v.slug) } }, icone('movie', tamanhoIcone));
}

/** Imagem de capa com fallback automático para a capa substituta. */
export function imagemCapa(v, tamanhoIcone) {
  if (!v.capaUrl) return capaSubstituta(v, tamanhoIcone);
  const img = h('img', { src: v.capaUrl, alt: '', loading: 'lazy', decoding: 'async', draggable: 'false' });
  img.addEventListener('error', () => img.replaceWith(capaSubstituta(v, tamanhoIcone)), { once: true });
  return img;
}

export function cardVideo(v, { mostrarCategoria = false, mostrarRestante = false } = {}) {
  const duracao = fmtDuracao(v.duracaoSegundos);
  const p = obterProgresso(v.slug);
  const andamento = emAndamento(p);
  const pct = Math.round(fracao(p) * 100);

  return h('a', { href: `#/video/${encodeURIComponent(v.slug)}`, class: 'card-video', title: v.titulo },
    h('div', { class: 'card-video-capa' },
      imagemCapa(v),
      p?.assistido && !andamento
        ? h('span', { class: 'selo-assistido' }, icone('check_circle', 14, true), 'Assistido') : null,
      duracao ? h('span', { class: 'selo-duracao' }, h('span', { class: 'sr-only' }, 'Duração: '), duracao) : null,
      andamento
        ? h('div', { class: 'card-video-progresso', role: 'img', 'aria-label': `${pct}% assistido` },
            h('span', { style: { width: `${pct}%` } }))
        : null,
      h('div', { class: 'card-video-acao', 'aria-hidden': 'true' },
        h('span', { class: 'btn-primario rounded-full px-5' },
          icone('play_arrow', 18, true), andamento ? 'Continuar' : 'Assistir'))),
    h('h3', { class: 'card-video-titulo' }, v.titulo),
    mostrarRestante && andamento
      ? h('p', { class: 'card-video-restante' }, `Faltam ${fmtDuracao(p.duracao - p.posicao)}`)
      : null,
    mostrarCategoria ? h('p', { class: 'mx-1.5 mt-1 text-xs text-suave' }, v.categoria) : null,
  );
}

// Espaçamento generoso: o relevo precisa de "ar" em volta para a sombra aparecer.
const grade = (filhos) => h('div', {
  class: 'grid grid-cols-2 gap-x-6 gap-y-8 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6',
}, filhos);

function secao(titulo, videos, id, { icone: nomeIcone, opcoesCard, montarCard } = {}) {
  return h('section', { class: 'mb-14', 'aria-labelledby': id },
    h('h2', { id, class: 'mb-5 flex items-center gap-3 font-display text-xl font-bold' },
      nomeIcone ? h('span', { class: 'flex text-destaque' }, icone(nomeIcone, 24)) : null,
      titulo, h('span', { class: 'chip font-medium text-suave' }, plural(videos.length, 'vídeo', 'vídeos'))),
    grade(videos.map((v, i) => (montarCard ? montarCard(v, i) : cardVideo(v, opcoesCard)))));
}

/**
 * Card da fileira "Continuar assistindo": o card normal + botão para tirá-lo da fileira.
 * O botão é irmão do link (não filho): botão dentro de <a> é HTML inválido.
 */
function cardContinuar(v, indice, aoAlterar) {
  const remover = h('button', {
    type: 'button', class: 'card-remover', title: 'Remover de Continuar assistindo',
    'aria-label': `Remover “${v.titulo}” de Continuar assistindo`,
    onclick: async () => {
      const anterior = await removerDeContinuar(v.slug);
      aoAlterar?.({ focarIndice: indice });
      toast('Removido de Continuar assistindo.', 'ok', {
        rotulo: 'Desfazer',
        aoClicar: async () => {
          await restaurarProgresso(v.slug, anterior);
          aoAlterar?.({});
        },
      });
    },
  }, icone('close', 18));
  return h('div', { class: 'card-continuar' }, cardVideo(v, { mostrarRestante: true }), remover);
}

function estadoVazio(icon, titulo, texto, acao) {
  return h('div', { class: 'painel mx-auto mt-20 flex max-w-md flex-col items-center px-8 py-10 text-center' },
    h('div', { class: 'pastilha mb-5 h-16 w-16' }, icone(icon, 32)),
    h('h2', { class: 'font-display text-xl font-bold' }, titulo),
    h('p', { class: 'mt-2 text-suave' }, texto),
    acao ? h('div', { class: 'mt-6' }, acao) : null);
}

/**
 * Renderiza a tela de catálogo em `raiz`.
 * aoAlterarProgresso({ focarIndice }): chamado quando um vídeo sai (ou volta) da fileira
 * "Continuar assistindo" — quem chama redesenha a tela.
 */
export function renderCatalogo(raiz, { videos, categorias, tags = [], tagsSelecionadas = [], pasta, busca, categoria },
  { aoAlterarProgresso, aoAlternarTag, aoRecarregar } = {}) {
  const conteudo = h('div', { class: 'mx-auto max-w-[1600px] px-4 pb-16 pt-9 sm:px-6' });

  if (pasta && !pasta.disponivel) {
    const tentar = h('button', { type: 'button', class: 'btn-primario' }, icone('refresh', 18), 'Tentar novamente');
    tentar.addEventListener('click', async () => {
      tentar.disabled = true;
      try { await aoRecarregar?.(); } finally { tentar.disabled = false; }
    });
    conteudo.append(estadoVazio('folder_off', 'Pasta de vídeos não encontrada',
      ['O MiniFlix não encontrou a pasta ',
        h('code', { class: 'my-2 block break-all font-mono text-sm text-texto' }, pasta.caminho),
        'Se ela fica num HD externo ou pendrive, conecte-o e tente novamente — ou escolha outra pasta na administração.'],
      h('div', { class: 'flex flex-wrap justify-center gap-3' },
        tentar, h('a', { href: '#/admin', class: 'btn' }, icone('settings', 18), 'Administração'))));
    raiz.replaceChildren(conteudo);
    return;
  }

  if (videos.length === 0) {
    conteudo.append(estadoVazio('movie', 'Seu catálogo está vazio',
      'Adicione o primeiro vídeo pela área administrativa: cole um link do YouTube ou importe um arquivo do computador.',
      h('a', { href: '#/admin', class: 'btn-primario' }, icone('settings', 18), 'Ir para a administração')));
    raiz.replaceChildren(conteudo);
    return;
  }

  const filtrados = filtrar(videos, { busca, categoria, tagsSelecionadas });
  if (tags.length) conteudo.append(barraTags(tags, tagsSelecionadas, aoAlternarTag));

  if (busca.trim() || tagsSelecionadas.length) {
    const titulo = busca.trim()
      ? h('span', {}, 'Resultados para ', h('span', { class: 'text-destaque' }, `“${busca.trim()}”`))
      : h('span', {}, tagsSelecionadas.length > 1 ? 'Vídeos com as tags ' : 'Vídeos com a tag ',
          h('span', { class: 'text-destaque' }, tagsSelecionadas.join(' + ')));
    conteudo.append(h('h2', { class: 'mb-6 flex flex-wrap items-center gap-3 font-display text-xl font-bold' },
      titulo, h('span', { class: 'chip font-medium text-suave' }, plural(filtrados.length, 'vídeo', 'vídeos'))));
    conteudo.append(filtrados.length
      ? grade(filtrados.map((v) => cardVideo(v, { mostrarCategoria: !categoria })))
      : estadoVazio('search_off', 'Nada encontrado',
          tagsSelecionadas.length > 1
            ? 'Nenhum vídeo tem todas essas tags juntas. Desmarque alguma tag, tente outras palavras ou limpe o filtro de categoria.'
            : 'Tente outras palavras, outra tag ou limpe o filtro de categoria.'));
  } else {
    // "Continuar assistindo" só na visão geral (sem filtro de categoria).
    const continuar = categoria ? [] : listaContinuar(videos);
    if (continuar.length) {
      conteudo.append(secao('Continuar assistindo', continuar, 'secao-continuar', {
        icone: 'history',
        montarCard: (v, i) => cardContinuar(v, i, aoAlterarProgresso),
      }));
    }
    categorias
      .filter((c) => !categoria || c === categoria)
      .forEach((c, i) => {
        const lista = filtrados.filter((v) => v.categoria === c);
        if (lista.length) conteudo.append(secao(c, lista, `secao-${i}`));
      });
  }

  raiz.replaceChildren(conteudo);
}

/**
 * Depois de remover um card da fileira, o foco iria para o <body> (o botão sumiu).
 * Leva o foco ao card que ocupou a mesma posição, ou ao primeiro card da página.
 */
export function focarAposRemocao(raiz, indice) {
  const cards = raiz.querySelectorAll('section[aria-labelledby=secao-continuar] .card-video');
  const alvo = cards[Math.min(indice, cards.length - 1)] ?? raiz.querySelector('.card-video');
  alvo?.focus({ preventScroll: true });
}
