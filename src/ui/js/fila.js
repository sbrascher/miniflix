// Painel da fila de processamento (importações e, na etapa 5, downloads).
import { bridge } from './bridge.js';
import { h, plural } from './util.js';
import { icone } from './icones.js';
import { toast } from './componentes.js';

let painel = null;   // elemento montado na tela de administração (ou null)
let itensAtuais = [];

const ativo = (i) => i.estado === 'aguardando' || i.estado === 'processando';

/** Cria o painel (vazio até haver itens). Atualizado depois por atualizarFila(). */
export function criarPainelFila() {
  painel = h('section', { class: 'mt-12', 'aria-labelledby': 'titulo-fila' });
  desenhar();
  return painel;
}

export function atualizarFila(itens) {
  itensAtuais = itens;
  if (painel?.isConnected) desenhar();
}

function desenhar() {
  if (!itensAtuais.length) { painel.replaceChildren(); painel.hidden = true; return; }
  painel.hidden = false;
  const emAndamento = itensAtuais.filter(ativo).length;
  const finalizados = itensAtuais.length - emAndamento;

  painel.replaceChildren(
    h('div', { class: 'mb-5 flex flex-wrap items-center gap-3' },
      h('h2', { id: 'titulo-fila', class: 'flex items-center gap-3 font-display text-xl font-bold' }, 'Fila',
        h('span', { class: 'chip font-medium text-suave' },
          emAndamento ? `${plural(emAndamento, 'item', 'itens')} em andamento` : 'tudo pronto')),
      finalizados ? h('button', {
        type: 'button', class: 'btn ml-auto',
        onclick: () => bridge.call('fila.limparFinalizados').catch((e) => toast(e.message, 'erro')),
      }, icone('clear_all', 16), 'Limpar finalizados') : null),
    h('ul', { class: 'painel lista-divisoria overflow-hidden' },
      itensAtuais.map(linhaFila)));
}

function linhaFila(i) {
  const pct = Math.round((i.progresso ?? 0) * 100);
  let status;
  switch (i.estado) {
    case 'processando':
      status = h('div', { class: 'mt-2' },
        h('div', { class: 'flex justify-between gap-3 text-xs text-suave' },
          h('span', { class: 'truncate' }, i.etapa, i.detalhe ? h('span', { class: 'opacity-80' }, ` · ${i.detalhe}`) : null),
          h('span', { class: 'shrink-0 tabular-nums' }, `${pct}%`)),
        h('div', {
          class: 'trilho mt-1.5', role: 'progressbar',
          'aria-valuemin': 0, 'aria-valuemax': 100, 'aria-valuenow': pct, 'aria-label': `${i.titulo}: ${i.etapa}`,
        }, h('div', { class: 'trilho-preenchimento', style: { width: `${pct}%` } })));
      break;
    case 'aguardando':
      status = h('p', { class: 'mt-1 text-xs text-suave' }, 'Aguardando na fila');
      break;
    case 'concluido':
      status = h('p', { class: 'mt-1 flex items-center gap-1.5 text-xs text-sucesso' },
        icone('check_circle', 14, true), 'Concluído', i.mensagem ? h('span', { class: 'text-suave' }, `· ${i.mensagem}`) : null);
      break;
    case 'erro':
      status = h('p', { class: 'mt-1 flex items-start gap-1.5 text-xs text-perigo' }, icone('error', 14, true), i.mensagem || 'Erro');
      break;
    default:
      status = h('p', { class: 'mt-1 text-xs text-suave' }, 'Cancelado');
  }

  const acoes = [];
  if (i.estado === 'concluido' && i.slug) {
    acoes.push(h('a', { href: `#/video/${encodeURIComponent(i.slug)}`, class: 'btn-icone', title: 'Assistir', 'aria-label': `Assistir ${i.titulo}` },
      icone('play_arrow', 20, true)));
  }
  acoes.push(ativo(i)
    ? h('button', {
        type: 'button', class: 'btn-icone btn-icone-perigo', title: 'Cancelar', 'aria-label': `Cancelar ${i.titulo}`,
        onclick: () => bridge.call('fila.cancelar', { id: i.id }),
      }, icone('cancel', 20))
    : h('button', {
        type: 'button', class: 'btn-icone', title: 'Remover da lista', 'aria-label': `Remover ${i.titulo} da lista`,
        onclick: () => bridge.call('fila.remover', { id: i.id }),
      }, icone('close', 20)));

  return h('li', { class: 'flex items-center gap-4 px-5 py-4' },
    h('div', { class: 'pastilha' },
      i.estado === 'processando'
        ? h('span', { class: 'flex animate-spin' }, icone('progress_activity', 22))
        : icone(i.tipo === 'youtube' ? 'smart_display' : i.tipo === 'transcricao' ? 'subtitles' : 'upload_file', 22)),
    h('div', { class: 'min-w-0 flex-1' },
      h('p', { class: 'truncate font-medium' }, i.titulo),
      h('p', { class: 'truncate text-xs text-suave' }, [i.origem, i.categoria].filter(Boolean).join(' · ')),
      status),
    h('div', { class: 'flex shrink-0 gap-1' }, acoes));
}
