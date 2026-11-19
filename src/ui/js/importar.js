// Importação de vídeos do computador: arquivos soltos ou uma pasta inteira (com subpastas).
// Antes de confirmar, cada arquivo é analisado: o que já está no catálogo (mesmo arquivo, mesmo com
// outro nome), o que se repete na seleção e o que será copiado sem perda ou convertido.
import { bridge } from './bridge.js';
import { h, fmtBytes, plural } from './util.js';
import { icone } from './icones.js';
import { abrirModal, toast } from './componentes.js';
import { campoTags } from './tags.js';

/** Card "Importar do computador" da tela de administração. */
export function cardImportar(estado) {
  const aviso = h('p', { class: 'alerta-erro mt-3 hidden text-xs', role: 'alert' });
  const btnArquivos = h('button', { type: 'button', class: 'btn-primario' }, icone('video_file', 18), 'Escolher arquivos…');
  const btnPasta = h('button', { type: 'button', class: 'btn' }, icone('folder_open', 18), 'Importar pasta…');
  const botoes = [btnArquivos, btnPasta];

  async function escolher(comando) {
    botoes.forEach((b) => { b.disabled = true; });
    const progresso = indicadorProgresso();
    try {
      const r = await bridge.call(comando);
      progresso.fechar();
      if (r?.arquivos?.length) await modalImportar(r, estado);
    } catch (e) {
      progresso.fechar();
      toast(e.message, 'erro');
    } finally {
      botoes.forEach((b) => { b.disabled = false; });
    }
  }
  btnArquivos.addEventListener('click', () => escolher('importar.escolherArquivos'));
  btnPasta.addEventListener('click', () => escolher('importar.escolherPasta'));

  // Sem ffmpeg não há como importar: avisa logo, em vez de falhar depois.
  bridge.call('sistema.ferramentas').then((f) => {
    if (!f.ffmpeg || !f.ffprobe) {
      aviso.textContent = 'ffmpeg/ffprobe não encontrados. Coloque ffmpeg.exe e ffprobe.exe na pasta bin do MiniFlix.';
      aviso.classList.remove('hidden');
      botoes.forEach((b) => { b.disabled = true; });
    }
  }).catch(() => {});

  return h('div', { class: 'painel flex gap-5 p-6' },
    h('div', { class: 'pastilha' }, icone('upload_file', 24)),
    h('div', {},
      h('h2', { class: 'font-semibold' }, 'Importar do computador'),
      h('p', { class: 'mt-1 text-sm text-suave' },
        'Escolha vídeos ou uma pasta inteira (com subpastas). Vídeos que já estão no catálogo são reconhecidos, mesmo com outro nome. ',
        'Vídeos em H.264, VP9 ou AV1 são copiados sem perda de qualidade; os demais são convertidos em alta qualidade.'),
      h('div', { class: 'mt-5 flex flex-wrap gap-3' }, botoes),
      aviso));
}

/**
 * Janela "Analisando vídeos…" com barra de progresso. Só aparece se a análise demorar
 * (pastas grandes); com um ou dois arquivos não chega a piscar na tela.
 */
function indicadorProgresso() {
  let dlg = null;
  let barra, texto;
  const desligar = bridge.on('importar.progresso', ({ feitos, total }) => {
    if (!dlg && feitos < total) {
      barra = h('div', { class: 'trilho-preenchimento', style: { width: '0%' } });
      texto = h('p', { class: 'mt-3 text-sm tabular-nums text-suave', 'aria-live': 'polite' });
      dlg = h('dialog', { class: 'modal max-w-md', 'aria-labelledby': 'titulo-analise' },
        h('div', { class: 'px-6 py-6' },
          h('h2', { id: 'titulo-analise', class: 'flex items-center gap-2 font-display text-lg font-bold' },
            h('span', { class: 'flex animate-spin text-destaque' }, icone('progress_activity', 22)), 'Analisando vídeos…'),
          h('p', { class: 'mt-2 text-sm text-suave' }, 'Verificando quais já estão no catálogo e o que cada um vai precisar.'),
          h('div', { class: 'trilho mt-5', role: 'progressbar', 'aria-label': 'Progresso da análise' }, barra),
          texto));
      dlg.addEventListener('cancel', (e) => e.preventDefault()); // não fecha com Esc no meio da análise
      document.body.append(dlg);
      dlg.showModal();
    }
    if (dlg) {
      const pct = Math.round((feitos / total) * 100);
      barra.style.width = `${pct}%`;
      barra.parentElement.setAttribute('aria-valuenow', String(pct));
      texto.textContent = `${feitos} de ${total} vídeos`;
    }
  });
  return {
    fechar() {
      desligar();
      if (dlg) { dlg.close(); dlg.remove(); dlg = null; }
    },
  };
}

/** O que vai acontecer com o arquivo (vem da análise), com ícone e cor. */
function seloTratamento(analise) {
  if (!analise) return null;
  const [icon, cor, texto] = {
    copiar: ['check_circle', 'text-sucesso', 'Sem perda: copiado como está'],
    audio: ['check_circle', 'text-sucesso', 'Vídeo sem perda; áudio vira FLAC (sem perda)'],
    converter: ['autorenew', 'text-aviso', `Precisa converter (${analise.codec}): alta qualidade, pode demorar`],
  }[analise.tratamento];
  return h('span', { class: `inline-flex items-center gap-1 font-medium ${cor}` }, icone(icon, 14, true), texto);
}

/** Linha de detalhes: caminho (no modo pasta) · tamanho · tratamento. */
function detalhes(a) {
  return h('p', { class: 'mt-1 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-suave' },
    h('span', { class: 'min-w-0 truncate', title: a.caminhoRelativo ?? a.nome }, a.caminhoRelativo ?? a.nome),
    h('span', {}, `· ${fmtBytes(a.tamanhoBytes)}`),
    seloTratamento(a.analise));
}

async function modalImportar({ pasta, arquivos }, estado) {
  const comErro = arquivos.filter((a) => a.erro);
  const repetidos = arquivos.filter((a) => !a.erro && a.repetidoDe);
  const validos = arquivos.filter((a) => !a.erro && !a.repetidoDe);
  const novos = validos.filter((a) => !a.existente);
  const existentes = validos.filter((a) => a.existente);
  const modoPasta = !!pasta;

  // ---------- Novos: título editável + caixa para (des)marcar ----------
  const linhasNovas = novos.map((a, idx) => {
    const marcar = h('input', { type: 'checkbox', class: 'caixa-selecao mt-3 shrink-0', checked: true, 'aria-label': `Importar ${a.nome}` });
    const campo = h('input', { id: `imp-titulo-${idx}`, class: 'campo w-full', maxlength: 200, value: a.tituloSugerido, required: true,
      'aria-label': `Título de ${a.nome}` });
    marcar.addEventListener('change', () => { campo.disabled = !marcar.checked; atualizarTotal(); });
    return {
      a, marcar, campo,
      el: h('li', { class: 'flex gap-3 py-3 first:pt-1 last:pb-1' }, marcar,
        h('div', { class: 'min-w-0 flex-1' }, campo, detalhes(a))),
    };
  });

  // ---------- Já no catálogo: "Reimportar" desmarcado por padrão ----------
  const linhasExistentes = existentes.map((a) => {
    const marcar = h('input', { type: 'checkbox', class: 'caixa-selecao mt-0.5 shrink-0', 'aria-label': `Reimportar ${a.nome}` });
    marcar.addEventListener('change', atualizarTotal);
    return {
      a, marcar,
      el: h('li', { class: 'flex gap-3 py-3 first:pt-1 last:pb-1' }, marcar,
        h('div', { class: 'min-w-0 flex-1' },
          h('p', { class: 'text-sm' }, 'Igual a ', h('span', { class: 'font-semibold' }, `“${a.existente.titulo}”`),
            h('span', { class: 'text-suave' }, ` (${a.existente.categoria})`)),
          detalhes(a))),
    };
  });

  const marcarTodosExistentes = h('button', { type: 'button', class: 'text-sm font-medium text-destaque hover:underline' }, 'Marcar todos');
  marcarTodosExistentes.addEventListener('click', () => {
    const marcar = !linhasExistentes.every((l) => l.marcar.checked);
    linhasExistentes.forEach((l) => { l.marcar.checked = marcar; });
    marcarTodosExistentes.textContent = marcar ? 'Desmarcar todos' : 'Marcar todos';
    atualizarTotal();
  });

  // ---------- Opções que valem para todos os novos ----------
  const temSubpastas = novos.some((a) => a.subpasta);
  const usarSubpasta = h('input', { id: 'imp-subpasta', type: 'checkbox', class: 'caixa-selecao mt-0.5 shrink-0', checked: temSubpastas });
  const categoria = h('input', {
    id: 'imp-categoria', class: 'campo w-full', maxlength: 60, list: 'imp-categorias', autocomplete: 'off', placeholder: 'Sem categoria',
  });
  const tags = campoTags({ id: 'imp-tags', sugestoes: estado.tags ?? [] });
  const grandes = [...novos, ...existentes].filter((a) => a.analise?.tratamento === 'converter' && (a.analise.largura > 1920 || a.analise.altura > 1080));
  const reduzir = h('input', { id: 'imp-reduzir', type: 'checkbox', class: 'caixa-selecao mt-0.5 shrink-0' });

  const erro = h('p', { class: 'alerta-erro hidden', role: 'alert' });
  const enviar = h('button', { type: 'submit', form: 'form-importar', class: 'btn-primario' });

  function selecionados() {
    return {
      novos: linhasNovas.filter((l) => l.marcar.checked),
      reimportar: linhasExistentes.filter((l) => l.marcar.checked),
    };
  }
  function atualizarTotal() {
    const s = selecionados();
    const n = s.novos.length + s.reimportar.length;
    enviar.replaceChildren(icone('playlist_add', 18), n ? `Adicionar ${plural(n, 'vídeo', 'vídeos')} à fila` : 'Nada selecionado');
    enviar.disabled = n === 0;
  }

  const secao = (titulo, contagem, extra, lista, ajuda) => h('section', { class: 'space-y-2' },
    h('div', { class: 'flex flex-wrap items-center gap-2' },
      h('h3', { class: 'rotulo mb-0' }, titulo),
      h('span', { class: 'chip font-medium text-suave' }, String(contagem)),
      extra ? h('span', { class: 'ml-auto' }, extra) : null),
    ajuda ? h('p', { class: 'text-xs text-suave' }, ajuda) : null,
    h('ul', { class: 'lista-divisoria max-h-[34vh] overflow-y-auto px-1' }, lista));

  const form = h('form', { id: 'form-importar', class: 'space-y-6', novalidate: true },
    modoPasta ? h('p', { class: 'bandeja break-all px-4 py-2.5 text-sm' },
      icone('folder', 16), ' ', h('span', { class: 'font-mono' }, pasta),
      h('span', { class: 'text-suave' }, ` — ${plural(arquivos.length, 'vídeo encontrado', 'vídeos encontrados')}, incluindo subpastas`)) : null,

    novos.length ? secao('Novos', novos.length, null, linhasNovas.map((l) => l.el)) : null,

    existentes.length ? secao('Já estão no catálogo', existentes.length, existentes.length > 1 ? marcarTodosExistentes : null,
      linhasExistentes.map((l) => l.el),
      'São os mesmos arquivos já importados (mesmo com outro nome). Marque “reimportar” só se quiser trocar o arquivo de vídeo: título, categoria, tags, favorito e capa continuam como estão.') : null,

    repetidos.length ? h('details', { class: 'text-sm' },
      h('summary', { class: 'cursor-pointer font-semibold' }, `Repetidos na seleção (${repetidos.length}) — entram uma vez só`),
      h('ul', { class: 'mt-2 space-y-1 text-xs text-suave' },
        repetidos.map((a) => h('li', {}, `${a.caminhoRelativo ?? a.nome} = ${a.repetidoDe}`)))) : null,

    comErro.length ? h('details', { class: 'text-sm' },
      h('summary', { class: 'cursor-pointer font-semibold text-perigo' }, `Não podem ser importados (${comErro.length})`),
      h('ul', { class: 'mt-2 space-y-1 text-xs text-suave' },
        comErro.map((a) => h('li', {}, `${a.caminhoRelativo ?? a.nome}: ${a.erro}`)))) : null,

    novos.length ? h('div', { class: 'space-y-4' },
      h('div', {},
        h('label', { for: 'imp-categoria', class: 'rotulo' }, 'Categoria'),
        categoria,
        h('datalist', { id: 'imp-categorias' },
          estado.categorias.filter((c) => c !== 'Sem categoria').map((c) => h('option', { value: c }))),
        h('p', { class: 'mt-1 text-xs text-suave' },
          novos.length > 1 ? 'Aplicada a todos os vídeos novos. ' : '', 'Escolha uma existente ou digite um nome novo.')),
      temSubpastas ? h('div', { class: 'bandeja flex gap-3 px-4 py-3' }, usarSubpasta,
        h('label', { for: 'imp-subpasta', class: 'cursor-pointer text-sm' },
          h('span', { class: 'font-semibold' }, 'Usar o nome da subpasta como categoria'),
          h('span', { class: 'block text-xs text-suave' },
            'Ex.: Cursos\\Python\\aula1.mp4 entra na categoria “Python”. Vídeos soltos na pasta escolhida usam a categoria acima.'))) : null,
      h('div', {},
        h('label', { for: 'imp-tags', class: 'rotulo' }, 'Tags ', h('span', { class: 'font-normal text-suave' }, '(opcional)')),
        tags.el,
        novos.length > 1 ? h('p', { class: 'mt-1 text-xs text-suave' }, 'Aplicadas a todos os vídeos novos.') : null)) : null,

    grandes.length ? h('div', { class: 'bandeja flex gap-3 px-4 py-3' }, reduzir,
      h('label', { for: 'imp-reduzir', class: 'cursor-pointer text-sm' },
        h('span', { class: 'font-semibold' }, 'Reduzir para 1080p'),
        h('span', { class: 'block text-xs text-suave' },
          `${plural(grandes.length, 'vídeo precisa', 'vídeos precisam')} de conversão e ${grandes.length > 1 ? 'são maiores' : 'é maior'} que 1080p. `,
          'Reduzir deixa o arquivo menor e a conversão mais rápida, mas perde detalhe. Desmarcado = mantém a resolução original.'))) : null,
    erro);

  atualizarTotal();

  const ok = abrirModal({
    titulo: modoPasta ? 'Importar pasta' : 'Importar vídeos',
    largura: 'max-w-3xl',
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const s = selecionados();
        const vazio = s.novos.find((l) => !l.campo.value.trim());
        if (vazio) {
          erro.textContent = 'Todos os vídeos novos precisam de um título.';
          erro.classList.remove('hidden');
          vazio.campo.focus();
          return;
        }
        enviar.disabled = true;
        try {
          const r = await bridge.call('fila.adicionarLocal', {
            categoria: categoria.value,
            tags: tags.valores(),
            reduzirPara1080: reduzir.checked,
            itens: [
              ...s.novos.map((l) => ({
                token: l.a.token,
                titulo: l.campo.value,
                ...(usarSubpasta.checked && l.a.subpasta ? { categoria: l.a.subpasta } : {}),
              })),
              ...s.reimportar.map((l) => ({ token: l.a.token, reimportar: l.a.existente.slug })),
            ],
          });
          fechar({ adicionados: r.adicionados, reimportados: s.reimportar.length });
        } catch (ex) {
          erro.textContent = ex.message;
          erro.classList.remove('hidden');
          atualizarTotal();
        }
      });
      return [h('button', { type: 'button', class: 'btn', onclick: () => fechar(null) }, 'Cancelar'), enviar];
    },
  });

  if (linhasNovas[0]) { linhasNovas[0].campo.focus(); linhasNovas[0].campo.select(); } else enviar.focus();
  const r = await ok;
  if (r) {
    const partes = [`${plural(r.adicionados, 'vídeo adicionado', 'vídeos adicionados')} à fila`];
    if (r.reimportados) partes.push(`${plural(r.reimportados, 'será reimportado', 'serão reimportados')} mantendo título e tags`);
    toast(partes.join('; ') + '.');
  }
}
