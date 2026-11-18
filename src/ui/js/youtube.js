// Adicionar vídeos do YouTube (etapa 5).
import { bridge } from './bridge.js';
import { h } from './util.js';
import { icone } from './icones.js';
import { toast } from './componentes.js';

const padraoYoutube = /^(https?:\/\/)?((www|m|music)\.)?(youtube\.com|youtu\.be|youtube-nocookie\.com)\//i;

/** Card "Adicionar do YouTube" da tela de administração. */
export function cardYoutube(estado) {
  const url = h('input', {
    id: 'yt-url', type: 'url', class: 'campo w-full', placeholder: 'https://www.youtube.com/watch?v=…',
    autocomplete: 'off', spellcheck: 'false', required: true, 'aria-describedby': 'yt-erro',
  });
  const categoria = h('input', {
    id: 'yt-categoria', class: 'campo w-full', maxlength: 60, list: 'yt-categorias', autocomplete: 'off', placeholder: 'Sem categoria',
  });
  const erro = h('p', { id: 'yt-erro', class: 'alerta-erro hidden', role: 'alert' });
  const baixar = h('button', { type: 'submit', class: 'btn-primario' }, icone('download', 18), 'Baixar');
  const versao = h('span', { class: 'text-suave' });
  const btnAtualizar = h('button', { type: 'button', class: 'font-medium text-destaque hover:underline disabled:opacity-50' }, 'Atualizar yt-dlp');

  const mostrarErro = (msg) => { erro.textContent = msg; erro.classList.toggle('hidden', !msg); };

  const form = h('form', { class: 'mt-5 space-y-4', novalidate: true },
    h('div', {}, h('label', { for: 'yt-url', class: 'rotulo' }, 'Link do vídeo'), url),
    h('div', { class: 'flex flex-wrap items-end gap-3' },
      h('div', { class: 'min-w-48 flex-1' },
        h('label', { for: 'yt-categoria', class: 'rotulo' }, 'Categoria'),
        categoria,
        h('datalist', { id: 'yt-categorias' },
          estado.categorias.filter((c) => c !== 'Sem categoria').map((c) => h('option', { value: c })))),
      baixar),
    erro);

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const valor = url.value.trim();
    if (!valor) { mostrarErro('Cole o link de um vídeo do YouTube.'); url.focus(); return; }
    if (!padraoYoutube.test(valor)) { mostrarErro('Isso não parece um link do YouTube.'); url.focus(); return; }
    mostrarErro('');
    baixar.disabled = true;
    try {
      await bridge.call('fila.adicionarYoutube', { url: valor, categoria: categoria.value });
      url.value = '';
      toast('Download adicionado à fila.');
    } catch (ex) {
      mostrarErro(ex.message);
    } finally {
      baixar.disabled = false;
    }
  });
  url.addEventListener('input', () => mostrarErro(''));

  btnAtualizar.addEventListener('click', async () => {
    btnAtualizar.disabled = true;
    btnAtualizar.textContent = 'Atualizando…';
    try {
      const r = await bridge.call('ytdlp.atualizar');
      toast(r.mensagem);
      carregarVersao();
    } catch (ex) {
      toast(ex.message, 'erro');
    } finally {
      btnAtualizar.disabled = false;
      btnAtualizar.textContent = 'Atualizar yt-dlp';
    }
  });

  function carregarVersao() {
    bridge.call('ytdlp.versao').then((r) => { versao.textContent = r.versao ? `yt-dlp ${r.versao}` : ''; }).catch(() => {});
  }

  // Sem yt-dlp/ffmpeg o formulário fica desabilitado com uma explicação.
  bridge.call('sistema.ferramentas').then((f) => {
    const faltando = [!f.ytDlp && 'yt-dlp.exe', !f.ffmpeg && 'ffmpeg.exe', !f.deno && 'deno.exe'].filter(Boolean);
    if (faltando.length) {
      mostrarErro(`Faltam na pasta bin do MiniFlix: ${faltando.join(', ')}.`);
      if (!f.ytDlp || !f.ffmpeg) { baixar.disabled = true; url.disabled = true; }
    }
    if (f.ytDlp) carregarVersao();
  }).catch(() => {});

  return h('div', { class: 'painel flex gap-5 p-6' },
    h('div', { class: 'pastilha' }, icone('smart_display', 24)),
    h('div', { class: 'min-w-0 flex-1' },
      h('h2', { class: 'font-semibold' }, 'Adicionar do YouTube'),
      h('p', { class: 'mt-1 text-sm text-suave' },
        'Cole o link e escolha a categoria. O vídeo é baixado em até 1080p e fica disponível offline. Requer internet.'),
      form,
      h('p', { class: 'mt-3 flex flex-wrap gap-x-3 gap-y-1 text-xs' }, versao, btnAtualizar),
      h('p', { class: 'mt-2 text-xs text-suave' },
        'Baixe apenas conteúdo seu, licenciado ou com permissão do autor, respeitando os termos do YouTube.')));
}
