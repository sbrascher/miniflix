// Importação de vídeos do computador (etapa 4).
import { bridge } from './bridge.js';
import { h, fmtBytes, plural } from './util.js';
import { icone } from './icones.js';
import { abrirModal, toast } from './componentes.js';
import { campoTags } from './tags.js';

/** Card "Importar do computador" da tela de administração. */
export function cardImportar(estado) {
  const aviso = h('p', { class: 'alerta-erro mt-3 hidden text-xs', role: 'alert' });
  const botao = h('button', { type: 'button', class: 'btn-primario mt-5' }, icone('folder_open', 18), 'Escolher arquivos…');

  botao.addEventListener('click', async () => {
    botao.disabled = true;
    try {
      const arquivos = await bridge.call('importar.escolherArquivos');
      if (arquivos.length) await modalImportar(arquivos, estado);
    } catch (e) {
      toast(e.message, 'erro');
    } finally {
      botao.disabled = false;
    }
  });

  // Sem ffmpeg não há como importar: avisa logo, em vez de falhar depois.
  bridge.call('sistema.ferramentas').then((f) => {
    if (!f.ffmpeg || !f.ffprobe) {
      aviso.textContent = 'ffmpeg/ffprobe não encontrados. Coloque ffmpeg.exe e ffprobe.exe na pasta bin do MiniFlix.';
      aviso.classList.remove('hidden');
      botao.disabled = true;
    }
  }).catch(() => {});

  return h('div', { class: 'painel flex gap-5 p-6' },
    h('div', { class: 'pastilha' }, icone('upload_file', 24)),
    h('div', {},
      h('h2', { class: 'font-semibold' }, 'Importar do computador'),
      h('p', { class: 'mt-1 text-sm text-suave' },
        'Escolha um ou mais vídeos do PC. Vídeos em H.264, VP9 ou AV1 (inclusive os baixados do YouTube) são copiados em segundos; os demais são convertidos para MP4.'),
      botao, aviso));
}

async function modalImportar(arquivos, estado) {
  const linhas = arquivos.map((a, idx) => {
    const campo = h('input', {
      id: `imp-titulo-${idx}`, class: 'campo w-full', maxlength: 200, value: a.tituloSugerido, required: true,
    });
    return {
      a, campo,
      el: h('li', { class: 'py-3.5 first:pt-1 last:pb-1' },
        h('label', { for: `imp-titulo-${idx}`, class: 'mb-1.5 flex items-center gap-2 text-xs text-suave' },
          icone('movie', 14), h('span', { class: 'truncate' }, a.nome), h('span', { class: 'shrink-0' }, `· ${fmtBytes(a.tamanhoBytes)}`)),
        campo),
    };
  });

  const categoria = h('input', {
    id: 'imp-categoria', class: 'campo w-full', maxlength: 60, list: 'imp-categorias', autocomplete: 'off', placeholder: 'Sem categoria',
  });
  const tags = campoTags({ id: 'imp-tags', sugestoes: estado.tags ?? [] });
  const erro = h('p', { class: 'alerta-erro hidden', role: 'alert' });
  const enviar = h('button', { type: 'submit', form: 'form-importar', class: 'btn-primario' },
    icone('playlist_add', 18), `Adicionar ${plural(arquivos.length, 'vídeo', 'vídeos')} à fila`);

  const form = h('form', { id: 'form-importar', class: 'space-y-5', novalidate: true },
    h('div', {},
      h('p', { class: 'rotulo' }, arquivos.length > 1 ? 'Títulos' : 'Título'),
      h('ul', { class: 'lista-divisoria max-h-[40vh] overflow-y-auto px-1' }, linhas.map((l) => l.el))),
    h('div', {},
      h('label', { for: 'imp-categoria', class: 'rotulo' }, 'Categoria'),
      categoria,
      h('datalist', { id: 'imp-categorias' },
        estado.categorias.filter((c) => c !== 'Sem categoria').map((c) => h('option', { value: c }))),
      h('p', { class: 'mt-1 text-xs text-suave' },
        arquivos.length > 1 ? 'Aplicada a todos os vídeos. ' : '', 'Escolha uma existente ou digite um nome novo.')),
    h('div', {},
      h('label', { for: 'imp-tags', class: 'rotulo' }, 'Tags ', h('span', { class: 'font-normal text-suave' }, '(opcional)')),
      tags.el,
      arquivos.length > 1 ? h('p', { class: 'mt-1 text-xs text-suave' }, 'Aplicadas a todos os vídeos.') : null),
    erro);

  const ok = abrirModal({
    titulo: 'Importar vídeos',
    largura: 'max-w-xl',
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const vazio = linhas.find((l) => !l.campo.value.trim());
        if (vazio) {
          erro.textContent = 'Todos os vídeos precisam de um título.';
          erro.classList.remove('hidden');
          vazio.campo.focus();
          return;
        }
        enviar.disabled = true;
        try {
          await bridge.call('fila.adicionarLocal', {
            categoria: categoria.value,
            tags: tags.valores(),
            itens: linhas.map((l) => ({ token: l.a.token, titulo: l.campo.value })),
          });
          fechar(true);
        } catch (ex) {
          erro.textContent = ex.message;
          erro.classList.remove('hidden');
          enviar.disabled = false;
        }
      });
      return [h('button', { type: 'button', class: 'btn', onclick: () => fechar(false) }, 'Cancelar'), enviar];
    },
  });
  linhas[0].campo.focus();
  linhas[0].campo.select();
  if (await ok) toast(`${plural(arquivos.length, 'vídeo adicionado', 'vídeos adicionados')} à fila.`);
}
