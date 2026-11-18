import { h, fmtData, fmtDuracao } from './util.js';
import { icone } from './icones.js';

/**
 * Tela do player (versão básica — atalhos e "continuar assistindo" chegam na etapa 3).
 * Retorna uma função de limpeza que descarrega o vídeo para liberar o arquivo no Windows.
 */
export function renderPlayer(raiz, v) {
  const voltar = h('a', { href: '#/', class: 'btn' }, icone('arrow_back', 18), 'Voltar ao catálogo');

  if (!v) {
    raiz.replaceChildren(h('div', { class: 'px-4 py-16 sm:px-6' },
      h('div', { class: 'painel mx-auto max-w-lg px-8 py-10 text-center' },
        h('div', { class: 'pastilha mx-auto mb-5 h-16 w-16' }, icone('videocam_off', 32)),
        h('h1', { class: 'font-display text-2xl font-bold' }, 'Vídeo não encontrado'),
        h('p', { class: 'mt-2 text-suave' }, 'Ele pode ter sido excluído ou movido.'),
        h('div', { class: 'mt-6' }, voltar))));
    return () => {};
  }

  const video = h('video', {
    src: v.videoUrl, controls: true, autoplay: true, preload: 'auto', playsinline: true,
    poster: v.capaUrl, class: 'h-full w-full', 'aria-label': `Vídeo: ${v.titulo}`,
  });

  const info = (nomeIcone, texto) => h('span', { class: 'inline-flex items-center gap-1.5' }, icone(nomeIcone, 18), texto);
  const meta = [
    h('span', { class: 'chip chip-marca' }, v.categoria),
    v.dataPublicacao && info('calendar_today', fmtData(v.dataPublicacao)),
    v.duracaoSegundos && info('schedule', fmtDuracao(v.duracaoSegundos)),
    // Abre no navegador padrão (o C# intercepta links externos).
    v.origem === 'youtube' && v.urlOrigem && h('a', {
      href: v.urlOrigem, target: '_blank', rel: 'noopener', class: 'inline-flex items-center gap-1.5 hover:text-destaque',
    }, icone('open_in_new', 18), 'Ver no YouTube'),
  ];

  raiz.replaceChildren(h('div', { class: 'px-4 pb-16 pt-6 sm:px-6' },
    h('div', { class: 'coluna-player' },
      voltar,
      h('div', { class: 'moldura-player mt-5' }, h('div', { class: 'tela' }, video)),
      h('h1', { class: 'mt-7 font-display text-2xl font-bold leading-tight sm:text-3xl' }, v.titulo),
      h('div', { class: 'mt-3 flex flex-wrap items-center gap-x-5 gap-y-2 text-sm text-suave' }, meta),
      v.descricao && h('div', { class: 'painel mt-6 max-w-3xl px-6 py-5' },
        h('h2', { class: 'mb-2 text-sm font-semibold text-suave' }, 'Descrição'),
        h('p', { class: 'whitespace-pre-line leading-relaxed' }, v.descricao)),
    )));
  video.focus({ preventScroll: true });

  return () => {
    video.pause();
    video.removeAttribute('src');
    video.load(); // solta o handle do arquivo
  };
}
