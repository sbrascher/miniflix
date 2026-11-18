import { h, fmtData, fmtDuracao } from './util.js';
import { icone } from './icones.js';

/**
 * Tela do player (versão básica — atalhos e "continuar assistindo" chegam na etapa 3).
 * Retorna uma função de limpeza que descarrega o vídeo para liberar o arquivo no Windows.
 */
export function renderPlayer(raiz, v) {
  const voltar = h('a', { href: '#/', class: 'btn' }, icone('arrow_back', 16), 'Voltar ao catálogo');

  if (!v) {
    raiz.replaceChildren(h('div', { class: 'mx-auto max-w-3xl px-4 py-16 text-center sm:px-6' },
      h('h1', { class: 'font-display text-2xl font-bold' }, 'Vídeo não encontrado'),
      h('p', { class: 'mt-2 text-suave' }, 'Ele pode ter sido excluído ou movido.'),
      h('div', { class: 'mt-6' }, voltar)));
    return () => {};
  }

  const video = h('video', {
    src: v.videoUrl, controls: true, autoplay: true, preload: 'auto', playsinline: true,
    poster: v.capaUrl, class: 'h-full w-full bg-black',
    'aria-label': `Vídeo: ${v.titulo}`,
  });

  const meta = [
    h('span', { class: 'rounded-full bg-marca px-2.5 py-0.5 text-xs font-semibold text-white' }, v.categoria),
    v.dataPublicacao && h('span', { class: 'inline-flex items-center gap-1.5' }, icone('calendar_today', 16), fmtData(v.dataPublicacao)),
    v.duracaoSegundos && h('span', { class: 'inline-flex items-center gap-1.5' }, icone('schedule', 16), fmtDuracao(v.duracaoSegundos)),
  ];

  // Coluna com largura limitada pela altura da janela (16:9), para o título ficar visível
  // sem rolar e alinhado com o vídeo.
  raiz.replaceChildren(h('div', { class: 'px-4 pb-16 pt-5 sm:px-6' },
    h('div', { class: 'mx-auto w-full', style: { maxWidth: 'min(72rem, calc((100vh - 15rem) * 16 / 9))' } },
      voltar,
      h('div', { class: 'mt-4 aspect-video w-full overflow-hidden rounded-xl bg-black shadow-2xl shadow-black/40 ring-1 ring-borda' }, video),
      h('h1', { class: 'mt-6 font-display text-2xl font-bold leading-tight sm:text-3xl' }, v.titulo),
      h('div', { class: 'mt-3 flex flex-wrap items-center gap-x-4 gap-y-2 text-sm text-suave' }, meta),
      v.descricao && h('p', { class: 'mt-5 max-w-3xl whitespace-pre-line leading-relaxed text-texto/90' }, v.descricao),
    )));
  video.focus({ preventScroll: true });

  return () => {
    video.pause();
    video.removeAttribute('src');
    video.load(); // solta o handle do arquivo
  };
}
