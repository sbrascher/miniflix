import { h, fmtData, fmtDuracao } from './util.js';
import { icone } from './icones.js';
import { abrirModal } from './componentes.js';
import { botaoFavorito } from './favorito.js';
import { secaoTranscricao } from './transcricao.js';
import { obterProgresso, emAndamento, registrarProgresso, reiniciarProgresso } from './progresso.js';

/** Intervalo entre envios de progresso durante a reprodução (o C# agrupa as gravações em disco). */
const INTERVALO_PROGRESSO_MS = 10_000;
const VELOCIDADES = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2];

/** Atalhos (no estilo do YouTube) — usados pelo teclado e pela janela de ajuda. */
const ATALHOS = [
  [['Espaço', 'K'], 'Reproduzir / pausar'],
  [['←', '→'], 'Voltar / avançar 5 segundos'],
  [['J', 'L'], 'Voltar / avançar 10 segundos'],
  [['↑', '↓'], 'Aumentar / diminuir volume'],
  [['M'], 'Ativar / desativar som'],
  [['F'], 'Tela cheia'],
  [['<', '>'], 'Diminuir / aumentar velocidade'],
  [['0', '…', '9'], 'Ir para 0% … 90% do vídeo'],
  [['Home', 'End'], 'Ir para o início / fim'],
  [['?'], 'Mostrar esta lista'],
  [['Esc'], 'Sair da tela cheia ou voltar ao catálogo'],
];

export function mostrarAtalhos() {
  return abrirModal({
    titulo: 'Atalhos do teclado',
    largura: 'max-w-md',
    corpo: h('dl', { class: 'lista-divisoria' }, ATALHOS.map(([teclas, descricao]) =>
      h('div', { class: 'flex items-center justify-between gap-4 py-2.5' },
        h('dt', { class: 'text-sm' }, descricao),
        h('dd', { class: 'flex shrink-0 gap-1.5' },
          teclas.map((t) => (t === '…' ? h('span', { class: 'self-center text-suave' }, '…') : h('kbd', { class: 'tecla' }, t))))))),
    acoes: (fechar) => [h('button', { type: 'button', class: 'btn-primario', autofocus: true, onclick: () => fechar() }, 'Entendi')],
  });
}

// ---------- Preferências locais (volume) ----------
// Conveniência por usuário: localStorage pode falhar (modo restrito) e o app continua funcionando.
function lerVolume() {
  try {
    const v = JSON.parse(localStorage.getItem('miniflix.volume') ?? 'null');
    return v && typeof v.volume === 'number' ? v : null;
  } catch { return null; }
}
function salvarVolume(volume, mudo) {
  try { localStorage.setItem('miniflix.volume', JSON.stringify({ volume, mudo })); } catch { /* ignora */ }
}

const ehCampoDeTexto = (el) =>
  el instanceof HTMLElement && (el.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(el.tagName));

/**
 * Tela do player. Retorna uma função de limpeza que salva o progresso, remove os
 * atalhos e descarrega o vídeo (para liberar o arquivo no Windows).
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

  // ---------- Elementos ----------
  const video = h('video', {
    src: v.videoUrl, controls: true, autoplay: true, preload: 'auto', playsinline: true,
    poster: v.capaUrl, 'aria-label': `Vídeo: ${v.titulo}`,
  });
  const osd = h('div', { class: 'osd', role: 'status', 'aria-live': 'polite' });
  const tela = h('div', { class: 'tela' }, video, osd);
  const transcricao = secaoTranscricao(v, video);

  const info = (nomeIcone, texto) => h('span', { class: 'inline-flex items-center gap-1.5' }, icone(nomeIcone, 18), texto);
  const meta = [
    h('span', { class: 'chip chip-marca' }, v.categoria),
    v.dataPublicacao && info('calendar_today', fmtData(v.dataPublicacao)),
    v.duracaoSegundos > 0 && info('schedule', fmtDuracao(v.duracaoSegundos)),
    v.origem === 'youtube' && v.urlOrigem && h('a', {
      href: v.urlOrigem, target: '_blank', rel: 'noopener', class: 'inline-flex items-center gap-1.5 hover:text-destaque',
    }, icone('open_in_new', 18), 'Ver no YouTube'),
  ];

  raiz.replaceChildren(h('div', { class: 'px-4 pb-16 pt-6 sm:px-6' },
    h('div', { class: 'coluna-player' },
      h('div', { class: 'flex items-center justify-between gap-3' },
        voltar,
        h('button', { type: 'button', class: 'btn', onclick: () => mostrarAtalhos(), 'aria-keyshortcuts': 'Shift+?' },
          icone('keyboard', 18), 'Atalhos')),
      h('div', { class: 'moldura-player mt-5' }, tela),
      h('div', { class: 'mt-7 flex items-start justify-between gap-4' },
        h('h1', { class: 'font-display text-2xl font-bold leading-tight sm:text-3xl' }, v.titulo),
        h('div', { class: 'shrink-0' }, botaoFavorito(v, { comTexto: true }))),
      h('div', { class: 'mt-3 flex flex-wrap items-center gap-x-5 gap-y-2 text-sm text-suave' }, meta),
      v.tags?.length > 0 && h('ul', { class: 'mt-4 flex flex-wrap gap-2', 'aria-label': 'Tags' },
        v.tags.map((t) => h('li', {},
          h('button', {
            type: 'button', class: 'chip chip-filtro', title: `Ver vídeos com a tag “${t}”`,
            onclick: () => document.dispatchEvent(new CustomEvent('miniflix:filtrar-tag', { detail: t })),
          }, icone('sell', 14), t)))),
      v.descricao && h('div', { class: 'painel mt-6 max-w-3xl px-6 py-5' },
        h('h2', { class: 'mb-2 text-sm font-semibold text-suave' }, 'Descrição'),
        h('p', { class: 'whitespace-pre-line leading-relaxed' }, v.descricao)),
      transcricao.el,
    )));

  // ---------- Aviso rápido (OSD) ----------
  let timerOsd;
  function mostrarOsd(nomeIcone, texto) {
    osd.replaceChildren(icone(nomeIcone, 24, true), texto);
    osd.classList.add('visivel');
    clearTimeout(timerOsd);
    timerOsd = setTimeout(() => osd.classList.remove('visivel'), 750);
  }

  // ---------- Volume lembrado entre sessões ----------
  const pref = lerVolume();
  if (pref) { video.volume = Math.min(Math.max(pref.volume, 0), 1); video.muted = !!pref.mudo; }
  video.addEventListener('volumechange', () => salvarVolume(video.volume, video.muted));

  // ---------- Continuar assistindo ----------
  const duracaoValida = () => Number.isFinite(video.duration) && video.duration > 0;
  let ultimoEnvio = 0;
  function salvarProgresso() {
    if (!duracaoValida()) return;
    ultimoEnvio = performance.now();
    registrarProgresso(v.slug, video.currentTime, video.duration);
  }

  video.addEventListener('timeupdate', () => {
    if (!video.paused && performance.now() - ultimoEnvio > INTERVALO_PROGRESSO_MS) salvarProgresso();
  });
  video.addEventListener('pause', () => { if (!video.ended) salvarProgresso(); });
  video.addEventListener('ended', () => {
    if (duracaoValida()) registrarProgresso(v.slug, video.duration, video.duration);
  });
  window.addEventListener('pagehide', salvarProgresso);

  const progresso = obterProgresso(v.slug);
  if (emAndamento(progresso)) {
    video.addEventListener('loadedmetadata', () => {
      video.currentTime = Math.min(progresso.posicao, Math.max(video.duration - 1, 0));
      mostrarAvisoRetomar(progresso.posicao);
    }, { once: true });
  }

  function mostrarAvisoRetomar(posicao) {
    const aviso = h('div', { class: 'aviso-retomar', role: 'status' },
      icone('history', 18), `Continuando de ${fmtDuracao(posicao)}`,
      h('button', {
        type: 'button',
        onclick: () => {
          video.currentTime = 0;
          reiniciarProgresso(v.slug);
          sumir();
          video.focus();
        },
      }, 'Começar do início'));
    const sumir = () => { aviso.style.opacity = '0'; setTimeout(() => aviso.remove(), 300); };
    tela.append(aviso);
    setTimeout(sumir, 8000);
  }

  // ---------- Ações dos atalhos ----------
  const limitar = (t) => Math.min(Math.max(t, 0), duracaoValida() ? video.duration : t);

  const acoes = {
    alternar() {
      if (video.paused || video.ended) { video.play().catch(() => {}); mostrarOsd('play_arrow', 'Reproduzir'); }
      else { video.pause(); mostrarOsd('pause', 'Pausado'); }
    },
    pular(segundos) {
      video.currentTime = limitar(video.currentTime + segundos);
      mostrarOsd(segundos < 0 ? 'fast_rewind' : 'fast_forward', `${segundos > 0 ? '+' : '−'}${Math.abs(segundos)} s`);
    },
    volume(delta) {
      video.muted = false;
      video.volume = Math.round(Math.min(Math.max(video.volume + delta, 0), 1) * 10) / 10;
      mostrarOsd(video.volume === 0 ? 'volume_mute' : delta > 0 ? 'volume_up' : 'volume_down',
        `Volume ${Math.round(video.volume * 100)}%`);
    },
    mudo() {
      video.muted = !video.muted;
      mostrarOsd(video.muted ? 'volume_off' : 'volume_up', video.muted ? 'Sem som' : 'Som ativado');
    },
    telaCheia() {
      if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
      else tela.requestFullscreen().catch(() => {});
    },
    velocidade(passo) {
      const atual = VELOCIDADES.reduce((a, b) => (Math.abs(b - video.playbackRate) < Math.abs(a - video.playbackRate) ? b : a));
      const i = Math.min(Math.max(VELOCIDADES.indexOf(atual) + passo, 0), VELOCIDADES.length - 1);
      video.playbackRate = VELOCIDADES[i];
      mostrarOsd('speed', `${VELOCIDADES[i].toLocaleString('pt-BR')}×`);
    },
    irPara(fracaoAlvo) {
      if (!duracaoValida()) return;
      video.currentTime = limitar(video.duration * fracaoAlvo);
      mostrarOsd('schedule', fmtDuracao(video.currentTime) || '0:00');
    },
  };

  function aoTeclar(e) {
    if (e.defaultPrevented || e.ctrlKey || e.altKey || e.metaKey) return;
    if (document.querySelector('dialog[open]')) return;     // o modal cuida do próprio teclado
    if (ehCampoDeTexto(e.target)) return;
    const emControle = e.target instanceof HTMLElement && e.target !== video && /^(BUTTON|A)$/.test(e.target.tagName);

    const k = e.key;
    let tratado = true;
    if ((k === ' ' || k === 'Enter') && emControle) return; // deixa botões/links funcionarem normalmente
    else if (k === ' ' || k === 'k' || k === 'K') acoes.alternar();
    else if (k === 'ArrowLeft') acoes.pular(-5);
    else if (k === 'ArrowRight') acoes.pular(5);
    else if (k === 'j' || k === 'J') acoes.pular(-10);
    else if (k === 'l' || k === 'L') acoes.pular(10);
    else if (k === 'ArrowUp') acoes.volume(0.1);
    else if (k === 'ArrowDown') acoes.volume(-0.1);
    else if (k === 'm' || k === 'M') acoes.mudo();
    else if (k === 'f' || k === 'F') acoes.telaCheia();
    else if (k === '<') acoes.velocidade(-1);
    else if (k === '>') acoes.velocidade(1);
    else if (/^[0-9]$/.test(k)) acoes.irPara(Number(k) / 10);
    else if (k === 'Home') acoes.irPara(0);
    else if (k === 'End') acoes.irPara(0.999);
    else if (k === '?') mostrarAtalhos();
    else if (k === 'Escape') {
      if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
      else location.hash = '#/';
    } else tratado = false;

    if (tratado) {
      // Captura + preventDefault: evita que o <video> focado execute a mesma ação de novo.
      e.preventDefault();
      e.stopPropagation();
    }
  }
  window.addEventListener('keydown', aoTeclar, { capture: true });

  video.focus({ preventScroll: true });

  // ---------- Limpeza ao sair da tela ----------
  return () => {
    transcricao.limpar();
    salvarProgresso();
    window.removeEventListener('keydown', aoTeclar, { capture: true });
    window.removeEventListener('pagehide', salvarProgresso);
    clearTimeout(timerOsd);
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    video.pause();
    video.removeAttribute('src');
    video.load(); // solta o handle do arquivo
  };
}
