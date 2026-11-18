// Seção "Capa" do modal Editar vídeo: trocar por uma imagem do computador ou por um quadro do vídeo.
// A capa nova vira primeiro uma PRÉVIA no disco; só substitui a atual ao confirmar (Salvar alterações).
import { bridge } from './bridge.js';
import { h, fmtDuracao } from './util.js';
import { icone } from './icones.js';
import { capaSubstituta } from './catalogo.js';

/**
 * Cria a seção. Devolve:
 *   el          — elemento para inserir no formulário
 *   alterada()  — true se há uma capa nova esperando confirmação
 *   confirmar() — grava a capa nova (chamar ao salvar o modal)
 *   descartar() — apaga a prévia (chamar ao cancelar/fechar)
 *   liberar()   — descarrega o vídeo do seletor de quadro (solta o arquivo no Windows)
 */
export function secaoCapa(v) {
  let pendente = false;
  let ocupado = false;

  const imagem = h('div', { class: 'bandeja aspect-video w-full overflow-hidden' });
  const mostrarImagem = (url) => {
    if (!url) { imagem.replaceChildren(capaSubstituta(v, 32)); return; }
    imagem.replaceChildren(h('img', { src: url, alt: 'Capa do vídeo', class: 'h-full w-full object-cover' }));
  };
  mostrarImagem(v.capaUrl);

  const status = h('p', { class: 'mt-2 min-h-[1.25rem] text-xs text-suave', 'aria-live': 'polite' });
  const erro = h('p', { class: 'alerta-erro mt-2 hidden', role: 'alert' });
  const mostrarErro = (msg) => { erro.textContent = msg ?? ''; erro.classList.toggle('hidden', !msg); };

  async function usarPrevia(promessa) {
    if (ocupado) return;
    ocupado = true;
    mostrarErro('');
    status.textContent = 'Gerando a capa…';
    botoes.forEach((b) => { b.disabled = true; });
    try {
      const r = await promessa();
      if (!r) { status.textContent = pendente ? status.textContent : ''; return; } // diálogo cancelado
      mostrarImagem(r.url);
      pendente = true;
      status.textContent = 'Nova capa — será salva ao clicar em “Salvar alterações”.';
    } catch (e) {
      status.textContent = '';
      mostrarErro(e.message);
    } finally {
      ocupado = false;
      botoes.forEach((b) => { b.disabled = false; });
    }
  }

  // ---------- Seletor de quadro ----------
  const duracao = v.duracaoSegundos || 0;
  const video = h('video', { muted: true, preload: 'auto', playsinline: true, class: 'h-full w-full', 'aria-hidden': 'true' });
  const tempo = h('output', { class: 'w-14 shrink-0 text-right text-sm tabular-nums text-suave' }, '0:00');
  const deslizante = h('input', {
    type: 'range', min: 0, max: duracao || 1, step: 0.1, value: Math.min(duracao * 0.1, 60),
    class: 'deslizante flex-1', 'aria-label': 'Momento do vídeo para a capa',
  });
  const atualizarQuadro = () => {
    const t = Number(deslizante.value);
    tempo.textContent = fmtDuracao(t) || '0:00';
    deslizante.setAttribute('aria-valuetext', tempo.textContent);
    if (video.readyState >= 1) video.currentTime = t;
  };
  deslizante.addEventListener('input', atualizarQuadro);
  video.addEventListener('loadedmetadata', () => {
    if (!duracao && Number.isFinite(video.duration)) deslizante.max = video.duration;
    atualizarQuadro();
  });

  const usarQuadro = h('button', { type: 'button', class: 'btn-primario' }, icone('photo_camera', 18), 'Usar este quadro');
  usarQuadro.addEventListener('click', () => usarPrevia(async () => {
    const r = await bridge.call('capa.prepararDeQuadro', { slug: v.slug, segundos: Number(deslizante.value) });
    fecharSeletor();
    return r;
  }));

  const seletor = h('div', { class: 'mt-4 hidden space-y-3' },
    h('div', { class: 'bandeja aspect-video w-full overflow-hidden bg-black' }, video),
    h('div', { class: 'flex items-center gap-3' }, deslizante, tempo),
    h('div', { class: 'flex justify-end' }, usarQuadro));

  function abrirSeletor() {
    if (!video.getAttribute('src')) video.src = v.videoUrl;
    seletor.classList.remove('hidden');
    btnQuadro.setAttribute('aria-expanded', 'true');
    deslizante.focus();
  }
  function fecharSeletor() {
    seletor.classList.add('hidden');
    btnQuadro.setAttribute('aria-expanded', 'false');
    liberar();
  }
  function liberar() {
    if (!video.getAttribute('src')) return;
    video.pause();
    video.removeAttribute('src');
    video.load();
  }

  // ---------- Botões ----------
  const btnImagem = h('button', { type: 'button', class: 'btn' }, icone('image', 18), 'Escolher imagem…');
  btnImagem.addEventListener('click', () => usarPrevia(() => bridge.call('capa.prepararDeImagem', { slug: v.slug })));

  const btnQuadro = h('button', { type: 'button', class: 'btn', 'aria-expanded': 'false' },
    icone('movie', 18), 'Escolher quadro do vídeo');
  btnQuadro.addEventListener('click', () => (seletor.classList.contains('hidden') ? abrirSeletor() : fecharSeletor()));

  const botoes = [btnImagem, btnQuadro, usarQuadro];

  const el = h('div', {},
    h('p', { class: 'rotulo' }, 'Capa'),
    h('div', { class: 'grid gap-4 sm:grid-cols-[minmax(0,15rem)_1fr] sm:items-start' },
      imagem,
      h('div', { class: 'flex flex-col items-start gap-3' },
        btnImagem, btnQuadro,
        h('p', { class: 'text-xs text-suave' }, 'A imagem é ajustada para 640 px de largura.'))),
    status, erro, seletor);

  return {
    el,
    alterada: () => pendente,
    async confirmar() {
      if (!pendente) return;
      await bridge.call('capa.confirmar', { slug: v.slug });
      pendente = false;
    },
    async descartar() {
      if (!pendente) return;
      pendente = false;
      await bridge.call('capa.descartar', { slug: v.slug }).catch(() => {});
    },
    liberar,
  };
}
