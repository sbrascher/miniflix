// Adicionar vídeos do YouTube (etapa 5).
import { bridge } from './bridge.js';
import { h, plural } from './util.js';
import { icone } from './icones.js';
import { abrirModal, toast } from './componentes.js';
import { campoTags } from './tags.js';

/** Card "Adicionar do YouTube" da tela de administração. */
export function cardYoutube(estado) {
  // Um ou vários links: um por linha, separados por espaço/vírgula ou colados no meio de um texto.
  const links = h('textarea', {
    id: 'yt-url', class: 'campo w-full', rows: 3, autocomplete: 'off', spellcheck: 'false', 'aria-describedby': 'yt-ajuda yt-erro',
    placeholder: 'https://www.youtube.com/watch?v=…\nCole um ou vários links (um por linha)',
  });
  const categoria = h('input', {
    id: 'yt-categoria', class: 'campo w-full', maxlength: 60, list: 'yt-categorias', autocomplete: 'off', placeholder: 'Sem categoria',
  });
  const transcricao = h('input', { id: 'yt-transcricao', type: 'checkbox', class: 'caixa-selecao shrink-0', checked: true });
  const erro = h('p', { id: 'yt-erro', class: 'alerta-erro hidden', role: 'alert' });
  const baixar = h('button', { type: 'submit', class: 'btn-primario' }, icone('download', 18), 'Baixar');
  const versao = h('span', { class: 'text-suave' });
  const btnAtualizar = h('button', { type: 'button', class: 'font-medium text-destaque hover:underline disabled:opacity-50' }, 'Atualizar yt-dlp');

  const mostrarErro = (msg) => { erro.textContent = msg; erro.classList.toggle('hidden', !msg); };

  const form = h('form', { class: 'mt-5 space-y-4', novalidate: true },
    h('div', {},
      h('label', { for: 'yt-url', class: 'rotulo' }, 'Links dos vídeos'),
      links,
      h('p', { id: 'yt-ajuda', class: 'mt-1 text-xs text-suave' }, 'Um link baixa direto; com vários, você confere a lista antes de baixar.')),
    h('div', { class: 'flex flex-wrap items-end gap-3' },
      h('div', { class: 'min-w-48 flex-1' },
        h('label', { for: 'yt-categoria', class: 'rotulo' }, 'Categoria'),
        categoria,
        h('datalist', { id: 'yt-categorias' },
          estado.categorias.filter((c) => c !== 'Sem categoria').map((c) => h('option', { value: c })))),
      baixar),
    h('label', { for: 'yt-transcricao', class: 'flex cursor-pointer items-start gap-2.5 text-sm' }, transcricao,
      h('span', {}, 'Baixar a transcrição ',
        h('span', { class: 'text-suave' }, '(pelas legendas do vídeo: em português; se não houver, em inglês)'))),
    erro);

  form.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!links.value.trim()) { mostrarErro('Cole o link de um vídeo do YouTube.'); links.focus(); return; }
    mostrarErro('');
    baixar.disabled = true;
    try {
      const r = await bridge.call('youtube.analisarLinks', { texto: links.value });
      const valido = r.novos.length + r.existentes.length + r.naFila.length;
      if (r.total === 0) { mostrarErro('Nenhum link encontrado. Cole o endereço de um vídeo do YouTube.'); links.focus(); return; }

      // Um link só: mesmo comportamento de sempre (sem janela de conferência).
      if (r.total === 1 || (valido === 1 && r.repetidos.length === 0 && r.invalidos.length === 0)) {
        if (r.invalidos.length) { mostrarErro(r.invalidos[0].motivo); links.focus(); return; }
        if (r.existentes.length) { mostrarErro(`Este vídeo já está no catálogo: “${r.existentes[0].titulo}”.`); return; }
        if (r.naFila.length) { mostrarErro('Este vídeo já está na fila.'); return; }
        await bridge.call('fila.adicionarYoutubeLote', { ids: [r.novos[0].id], categoria: categoria.value, transcricao: transcricao.checked });
        links.value = '';
        toast('Download adicionado à fila.');
        return;
      }

      const res = await modalLote(r, categoria.value, transcricao.checked, estado);
      if (res) {
        links.value = '';
        toast(`${plural(res.adicionados, 'download adicionado', 'downloads adicionados')} à fila.`);
      }
    } catch (ex) {
      mostrarErro(ex.message);
    } finally {
      baixar.disabled = false;
    }
  });
  links.addEventListener('input', () => mostrarErro(''));

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
      if (!f.ytDlp || !f.ffmpeg) { baixar.disabled = true; links.disabled = true; }
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

/** Conferência de vários links antes de baixar: novos (marcados), já no catálogo, na fila, repetidos e inválidos. */
async function modalLote(r, categoriaInicial, transcricaoInicial, estado) {
  const linhas = r.novos.map((n) => {
    const marcar = h('input', { type: 'checkbox', class: 'caixa-selecao mt-0.5 shrink-0', checked: true, 'aria-label': `Baixar ${n.texto}` });
    marcar.addEventListener('change', atualizar);
    return {
      n, marcar,
      el: h('li', { class: 'flex items-start gap-3 py-2.5 first:pt-1 last:pb-1' }, marcar,
        h('div', { class: 'min-w-0 flex-1' },
          h('p', { class: 'truncate font-mono text-sm', title: n.texto }, n.texto),
          h('p', { class: 'text-xs text-suave' }, `Vídeo ${n.id} · o título aparece na fila quando o download começar`))),
    };
  });

  const categoria = h('input', {
    id: 'lote-categoria', class: 'campo w-full', maxlength: 60, list: 'lote-categorias', autocomplete: 'off', placeholder: 'Sem categoria',
    value: categoriaInicial,
  });
  const tags = campoTags({ id: 'lote-tags', sugestoes: estado.tags ?? [] });
  const transcricao = h('input', { id: 'lote-transcricao', type: 'checkbox', class: 'caixa-selecao shrink-0', checked: transcricaoInicial });
  const erro = h('p', { class: 'alerta-erro hidden', role: 'alert' });
  const enviar = h('button', { type: 'submit', form: 'form-lote', class: 'btn-primario' });
  function atualizar() {
    const n = linhas.filter((l) => l.marcar.checked).length;
    enviar.replaceChildren(icone('download', 18), n ? `Baixar ${plural(n, 'vídeo', 'vídeos')}` : 'Nada selecionado');
    enviar.disabled = n === 0;
  }

  const lista = (titulo, itens, desenhar, aberto = false, classe = '') => itens.length
    ? h('details', { class: 'text-sm', open: aberto },
        h('summary', { class: `cursor-pointer font-semibold ${classe}` }, `${titulo} (${itens.length})`),
        h('ul', { class: 'mt-2 max-h-40 space-y-1 overflow-y-auto text-xs text-suave' }, itens.map((i) => h('li', { class: 'break-all' }, desenhar(i)))))
    : null;

  const form = h('form', { id: 'form-lote', class: 'space-y-6', novalidate: true },
    linhas.length
      ? h('section', { class: 'space-y-2' },
          h('div', { class: 'flex items-center gap-2' }, h('h3', { class: 'rotulo mb-0' }, 'Novos'),
            h('span', { class: 'chip font-medium text-suave' }, String(linhas.length))),
          h('ul', { class: 'lista-divisoria max-h-[34vh] overflow-y-auto px-1' }, linhas.map((l) => l.el)))
      : h('p', { class: 'text-suave' }, 'Nenhum vídeo novo para baixar.'),
    lista('Já estão no catálogo', r.existentes, (e) => [h('span', { class: 'font-medium text-texto' }, `“${e.titulo}”`), ` — ${e.texto}`], true),
    lista('Já estão na fila', r.naFila, (e) => e.texto),
    lista('Repetidos na lista — entram uma vez só', r.repetidos, (e) => e.texto),
    lista('Links que não servem', r.invalidos, (e) => `${e.texto} — ${e.motivo}`, true, 'text-perigo'),
    linhas.length ? h('div', { class: 'space-y-4' },
      h('div', {},
        h('label', { for: 'lote-categoria', class: 'rotulo' }, 'Categoria'),
        categoria,
        h('datalist', { id: 'lote-categorias' },
          estado.categorias.filter((c) => c !== 'Sem categoria').map((c) => h('option', { value: c }))),
        h('p', { class: 'mt-1 text-xs text-suave' }, 'Aplicada a todos os vídeos do lote.')),
      h('div', {},
        h('label', { for: 'lote-tags', class: 'rotulo' }, 'Tags ', h('span', { class: 'font-normal text-suave' }, '(opcional)')),
        tags.el),
      h('label', { for: 'lote-transcricao', class: 'flex cursor-pointer items-start gap-2.5 text-sm' }, transcricao,
        h('span', {}, 'Baixar a transcrição de cada vídeo ',
          h('span', { class: 'text-suave' }, '(em português; se não houver, em inglês)')))) : null,
    h('p', { class: 'text-xs text-suave' },
      'Os vídeos são baixados um de cada vez, em até 1080p. Se algum falhar (privado, removido, sem internet), os outros continuam.'),
    erro);

  atualizar();
  const resultado = abrirModal({
    titulo: 'Baixar vários vídeos',
    largura: 'max-w-2xl',
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (enviar.disabled) return;
        enviar.disabled = true;
        try {
          fechar(await bridge.call('fila.adicionarYoutubeLote', {
            ids: linhas.filter((l) => l.marcar.checked).map((l) => l.n.id),
            categoria: categoria.value,
            tags: tags.valores(),
            transcricao: transcricao.checked,
          }));
        } catch (ex) {
          erro.textContent = ex.message;
          erro.classList.remove('hidden');
          atualizar();
        }
      });
      return [h('button', { type: 'button', class: 'btn', onclick: () => fechar(null) }, 'Cancelar'), enviar];
    },
  });
  enviar.focus();
  return resultado;
}
