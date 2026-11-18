import { h, normalizar, fmtDuracao, matiz, plural } from './util.js';
import { icone } from './icones.js';

/** Filtra por categoria e por todos os termos da busca (sem acento/caixa) no título. */
export function filtrar(videos, { busca = '', categoria = '' }) {
  const termos = normalizar(busca).split(/\s+/).filter(Boolean);
  return videos.filter((v) =>
    (!categoria || v.categoria === categoria) &&
    termos.every((t) => normalizar(v.titulo).includes(t)));
}

function capaSubstituta(v) {
  const m = matiz(v.slug);
  return h('div', {
    class: 'flex h-full w-full items-center justify-center text-white/80',
    style: { background: `linear-gradient(135deg, hsl(${m} 45% 32%), hsl(${(m + 40) % 360} 55% 18%))` },
  }, icone('movie', 40));
}

export function cardVideo(v, { mostrarCategoria = false } = {}) {
  const midia = h('div', {
    class: 'relative aspect-video overflow-hidden rounded-lg bg-superficie-2 ring-1 ring-borda transition duration-300 ' +
           'group-hover:ring-2 group-hover:ring-destaque/70 group-hover:shadow-xl group-hover:shadow-black/30 ' +
           'group-focus-visible:ring-2 group-focus-visible:ring-destaque',
  });

  if (v.capaUrl) {
    const img = h('img', {
      src: v.capaUrl, alt: '', loading: 'lazy', decoding: 'async', draggable: 'false',
      class: 'h-full w-full object-cover transition duration-300 ease-out group-hover:scale-105',
    });
    img.addEventListener('error', () => img.replaceWith(capaSubstituta(v)), { once: true });
    midia.append(img);
  } else {
    midia.append(capaSubstituta(v));
  }

  const duracao = fmtDuracao(v.duracaoSegundos);
  if (duracao) {
    midia.append(h('span', {
      class: 'absolute bottom-2 right-2 rounded-md bg-black/80 px-1.5 py-0.5 text-xs font-semibold tabular-nums text-white',
    }, duracao));
  }

  midia.append(h('div', {
    class: 'absolute inset-0 flex items-center justify-center bg-black/0 opacity-0 transition duration-300 ' +
           'group-hover:bg-black/40 group-hover:opacity-100 group-focus-visible:bg-black/40 group-focus-visible:opacity-100',
  }, h('span', { class: 'btn-primario rounded-full px-5 shadow-lg' }, icone('play_arrow', 16, true), 'Assistir')));

  return h('a', {
    href: `#/video/${encodeURIComponent(v.slug)}`,
    class: 'group block rounded-lg focus-visible:outline-none',
    title: v.titulo,
  },
    midia,
    h('h3', { class: 'mt-2.5 line-clamp-2 text-[15px] font-medium leading-snug transition group-hover:text-destaque' }, v.titulo),
    mostrarCategoria ? h('p', { class: 'mt-0.5 text-xs text-suave' }, v.categoria) : null,
  );
}

const grade = (filhos) => h('div', {
  class: 'grid grid-cols-2 gap-x-4 gap-y-7 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5 2xl:grid-cols-6',
}, filhos);

function secao(titulo, videos, idx) {
  const id = `secao-${idx}`;
  return h('section', { class: 'mb-12', 'aria-labelledby': id },
    h('h2', { id, class: 'mb-4 flex items-baseline gap-3 font-display text-xl font-bold' },
      titulo, h('span', { class: 'text-sm font-normal text-suave' }, plural(videos.length, 'vídeo', 'vídeos'))),
    grade(videos.map((v) => cardVideo(v))));
}

function estadoVazio(icon, titulo, texto, acao) {
  return h('div', { class: 'mx-auto mt-24 flex max-w-md flex-col items-center text-center' },
    h('div', { class: 'mb-5 rounded-2xl bg-superficie-2 p-5 text-suave ring-1 ring-borda' }, icone(icon, 40)),
    h('h2', { class: 'font-display text-xl font-bold' }, titulo),
    h('p', { class: 'mt-2 text-suave' }, texto),
    acao ? h('div', { class: 'mt-6' }, acao) : null);
}

/** Renderiza a tela de catálogo em `raiz`. */
export function renderCatalogo(raiz, { videos, categorias, busca, categoria }) {
  const conteudo = h('div', { class: 'mx-auto max-w-[1600px] px-4 pb-16 pt-8 sm:px-6' });

  if (videos.length === 0) {
    conteudo.append(estadoVazio('movie', 'Seu catálogo está vazio',
      'Adicione o primeiro vídeo pela área administrativa: cole um link do YouTube ou importe um arquivo do computador.',
      h('a', { href: '#/admin', class: 'btn-primario' }, icone('settings', 16), 'Ir para a administração')));
    raiz.replaceChildren(conteudo);
    return;
  }

  const filtrados = filtrar(videos, { busca, categoria });

  if (busca.trim()) {
    conteudo.append(h('h2', { class: 'mb-5 font-display text-xl font-bold' },
      'Resultados para ', h('span', { class: 'text-destaque' }, `“${busca.trim()}”`),
      h('span', { class: 'ml-3 text-sm font-normal text-suave' }, plural(filtrados.length, 'vídeo', 'vídeos'))));
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
