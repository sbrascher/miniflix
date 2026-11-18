import { h, normalizar, fmtDuracao, matiz, plural } from './util.js';
import { icone } from './icones.js';

/** Filtra por categoria e por todos os termos da busca (sem acento/caixa) no título. */
export function filtrar(videos, { busca = '', categoria = '' }) {
  const termos = normalizar(busca).split(/\s+/).filter(Boolean);
  return videos.filter((v) =>
    (!categoria || v.categoria === categoria) &&
    termos.every((t) => normalizar(v.titulo).includes(t)));
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

export function cardVideo(v, { mostrarCategoria = false } = {}) {
  const duracao = fmtDuracao(v.duracaoSegundos);
  return h('a', { href: `#/video/${encodeURIComponent(v.slug)}`, class: 'card-video', title: v.titulo },
    h('div', { class: 'card-video-capa' },
      imagemCapa(v),
      duracao ? h('span', { class: 'selo-duracao' }, h('span', { class: 'sr-only' }, 'Duração: '), duracao) : null,
      h('div', { class: 'card-video-acao', 'aria-hidden': 'true' },
        h('span', { class: 'btn-primario rounded-full px-5' }, icone('play_arrow', 18, true), 'Assistir'))),
    h('h3', { class: 'card-video-titulo' }, v.titulo),
    mostrarCategoria ? h('p', { class: 'mx-1.5 mt-1 text-xs text-suave' }, v.categoria) : null,
  );
}

// Espaçamento generoso: o relevo precisa de "ar" em volta para a sombra aparecer.
const grade = (filhos) => h('div', {
  class: 'grid grid-cols-2 gap-x-6 gap-y-8 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6',
}, filhos);

function secao(titulo, videos, idx) {
  const id = `secao-${idx}`;
  return h('section', { class: 'mb-14', 'aria-labelledby': id },
    h('h2', { id, class: 'mb-5 flex items-center gap-3 font-display text-xl font-bold' },
      titulo, h('span', { class: 'chip font-medium text-suave' }, plural(videos.length, 'vídeo', 'vídeos'))),
    grade(videos.map((v) => cardVideo(v))));
}

function estadoVazio(icon, titulo, texto, acao) {
  return h('div', { class: 'painel mx-auto mt-20 flex max-w-md flex-col items-center px-8 py-10 text-center' },
    h('div', { class: 'pastilha mb-5 h-16 w-16' }, icone(icon, 32)),
    h('h2', { class: 'font-display text-xl font-bold' }, titulo),
    h('p', { class: 'mt-2 text-suave' }, texto),
    acao ? h('div', { class: 'mt-6' }, acao) : null);
}

/** Renderiza a tela de catálogo em `raiz`. */
export function renderCatalogo(raiz, { videos, categorias, busca, categoria }) {
  const conteudo = h('div', { class: 'mx-auto max-w-[1600px] px-4 pb-16 pt-9 sm:px-6' });

  if (videos.length === 0) {
    conteudo.append(estadoVazio('movie', 'Seu catálogo está vazio',
      'Adicione o primeiro vídeo pela área administrativa: cole um link do YouTube ou importe um arquivo do computador.',
      h('a', { href: '#/admin', class: 'btn-primario' }, icone('settings', 18), 'Ir para a administração')));
    raiz.replaceChildren(conteudo);
    return;
  }

  const filtrados = filtrar(videos, { busca, categoria });

  if (busca.trim()) {
    conteudo.append(h('h2', { class: 'mb-6 flex flex-wrap items-center gap-3 font-display text-xl font-bold' },
      h('span', {}, 'Resultados para ', h('span', { class: 'text-destaque' }, `“${busca.trim()}”`)),
      h('span', { class: 'chip font-medium text-suave' }, plural(filtrados.length, 'vídeo', 'vídeos'))));
    conteudo.append(filtrados.length
      ? grade(filtrados.map((v) => cardVideo(v, { mostrarCategoria: !categoria })))
      : estadoVazio('search_off', 'Nada encontrado', 'Tente outras palavras ou limpe o filtro de categoria.'));
  } else {
    categorias
      .filter((c) => !categoria || c === categoria)
      .forEach((c, i) => {
        const lista = filtrados.filter((v) => v.categoria === c);
        if (lista.length) conteudo.append(secao(c, lista, i));
      });
  }

  raiz.replaceChildren(conteudo);
}
