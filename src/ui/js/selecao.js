// Seleção de vários vídeos na tabela do Acervo + ações em massa (categoria, adicionar/remover tags).
import { bridge } from './bridge.js';
import { h, plural } from './util.js';
import { icone } from './icones.js';
import { abrirModal, toast } from './componentes.js';
import { campoTags } from './tags.js';

// Preservada entre re-renderizações da administração (ex.: após salvar ou filtrar).
const selecionados = new Set();
let ultimoClicado = null; // âncora do Shift+clique

/**
 * Cria o controle de seleção. `estado` é o estado global do app.
 * Devolve:
 *   barra                 — barra de ações (aparece com 1+ selecionado)
 *   caixaCabecalho(lista) — checkbox "selecionar todos" da lista visível
 *   caixaLinha(v, lista)  — checkbox de uma linha
 *   atualizar()           — sincroniza caixas, linhas e barra
 */
export function criarSelecao(estado) {
  // Vídeos excluídos (ou de outra pasta) saem da seleção.
  const existentes = new Set(estado.videos.map((v) => v.slug));
  for (const s of [...selecionados]) if (!existentes.has(s)) selecionados.delete(s);

  let listaVisivel = [];
  const caixas = new Map(); // slug -> checkbox da linha
  let caixaTodos = null;

  const contador = h('span', { class: 'font-semibold', 'aria-live': 'polite' });
  const ocultos = h('span', { class: 'text-sm text-suave' });
  const btnAdicionar = h('button', { type: 'button', class: 'btn-primario' }, icone('new_label', 18), 'Adicionar tags…');
  const btnRemover = h('button', { type: 'button', class: 'btn' }, icone('label_off', 18), 'Remover tags…');
  const btnCategoria = h('button', { type: 'button', class: 'btn' }, icone('folder_special', 18), 'Mudar categoria…');
  btnCategoria.addEventListener('click', () => mudarCategoria(estado));
  const btnLimpar = h('button', { type: 'button', class: 'btn' }, icone('close', 18), 'Limpar seleção');
  btnAdicionar.addEventListener('click', () => adicionarTags(estado));
  btnRemover.addEventListener('click', () => removerTags(estado));
  btnLimpar.addEventListener('click', () => { selecionados.clear(); atualizar(); caixaTodos?.focus(); });

  const barra = h('div', { class: 'barra-selecao hidden', role: 'region', 'aria-label': 'Ações para os vídeos selecionados' },
    h('span', { class: 'pastilha h-9 w-9' }, icone('checklist', 20)),
    h('div', { class: 'mr-auto flex flex-wrap items-baseline gap-x-2' }, contador, ocultos),
    btnCategoria, btnAdicionar, btnRemover, btnLimpar);

  function atualizar() {
    const n = selecionados.size;
    barra.classList.toggle('hidden', n === 0);
    contador.textContent = n ? `${plural(n, 'vídeo selecionado', 'vídeos selecionados')}` : '';
    const fora = [...selecionados].filter((s) => !listaVisivel.some((v) => v.slug === s)).length;
    ocultos.textContent = fora ? `(${fora} fora do filtro atual)` : '';

    for (const [slug, caixa] of caixas) {
      caixa.checked = selecionados.has(slug);
      caixa.closest('tr')?.classList.toggle('linha-selecionada', caixa.checked);
    }
    if (caixaTodos) {
      const marcados = listaVisivel.filter((v) => selecionados.has(v.slug)).length;
      caixaTodos.checked = marcados > 0 && marcados === listaVisivel.length;
      caixaTodos.indeterminate = marcados > 0 && marcados < listaVisivel.length;
    }
  }

  function caixaCabecalho(lista) {
    listaVisivel = lista;
    caixas.clear();
    caixaTodos = h('input', { type: 'checkbox', class: 'caixa-selecao', 'aria-label': 'Selecionar todos os vídeos da lista' });
    caixaTodos.addEventListener('change', () => {
      const marcar = caixaTodos.checked;
      for (const v of listaVisivel) marcar ? selecionados.add(v.slug) : selecionados.delete(v.slug);
      atualizar();
    });
    return caixaTodos;
  }

  function caixaLinha(v) {
    const caixa = h('input', { type: 'checkbox', class: 'caixa-selecao', 'aria-label': `Selecionar “${v.titulo}”` });
    caixa.addEventListener('click', (e) => {
      const marcar = caixa.checked;
      // Shift+clique: aplica o mesmo estado a todo o intervalo desde o último clicado.
      const i = listaVisivel.findIndex((x) => x.slug === v.slug);
      const j = listaVisivel.findIndex((x) => x.slug === ultimoClicado);
      const intervalo = e.shiftKey && j >= 0 ? listaVisivel.slice(Math.min(i, j), Math.max(i, j) + 1) : [v];
      for (const x of intervalo) marcar ? selecionados.add(x.slug) : selecionados.delete(x.slug);
      ultimoClicado = v.slug;
      atualizar();
    });
    caixas.set(v.slug, caixa);
    return caixa;
  }

  return { barra, caixaCabecalho, caixaLinha, atualizar };
}

const videosSelecionados = (estado) => estado.videos.filter((v) => selecionados.has(v.slug));

async function aplicar(estado, adicionar, remover) {
  const slugs = [...selecionados];
  const r = await bridge.call('videos.alterarTags', { slugs, adicionar, remover });
  return r.alterados;
}

async function adicionarTags(estado) {
  const videos = videosSelecionados(estado);
  const tags = campoTags({ id: 'massa-tags', sugestoes: estado.tags ?? [] });
  const erro = h('p', { class: 'alerta-erro hidden', role: 'alert' });
  const salvar = h('button', { type: 'submit', form: 'form-massa-tags', class: 'btn-primario' }, 'Adicionar');
  const form = h('form', { id: 'form-massa-tags', class: 'space-y-4', novalidate: true },
    h('p', { class: 'text-sm text-suave' },
      `As tags serão adicionadas a ${plural(videos.length, 'vídeo selecionado', 'vídeos selecionados')}. As tags que eles já têm continuam.`),
    h('div', {}, h('label', { for: 'massa-tags', class: 'rotulo' }, 'Tags'), tags.el),
    erro);

  const r = abrirModal({
    titulo: 'Adicionar tags',
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const lista = tags.valores();
        const mostrarErro = (msg) => { erro.textContent = msg; erro.classList.remove('hidden'); };
        if (!lista.length) { mostrarErro('Digite ao menos uma tag.'); document.querySelector('#massa-tags')?.focus(); return; }
        salvar.disabled = true;
        try { fechar({ n: await aplicar(estado, lista, []), lista }); } catch (ex) { mostrarErro(ex.message); salvar.disabled = false; }
      });
      return [h('button', { type: 'button', class: 'btn', onclick: () => fechar() }, 'Cancelar'), salvar];
    },
  });
  document.querySelector('#massa-tags')?.focus();
  const res = await r;
  if (!res) return;
  toast(res.n
    ? `${plural(res.lista.length, 'tag adicionada', 'tags adicionadas')} em ${plural(res.n, 'vídeo', 'vídeos')}.`
    : 'Os vídeos selecionados já tinham essas tags.');
}

async function removerTags(estado) {
  const videos = videosSelecionados(estado);
  // Tags presentes nos selecionados, com quantos vídeos têm cada uma.
  const contagem = new Map();
  for (const v of videos) for (const t of v.tags ?? []) {
    const chave = [...contagem.keys()].find((k) => k.toLowerCase() === t.toLowerCase()) ?? t;
    contagem.set(chave, (contagem.get(chave) ?? 0) + 1);
  }
  if (contagem.size === 0) { toast('Os vídeos selecionados não têm tags.', 'erro'); return; }

  const marcadas = new Set();
  const salvar = h('button', { type: 'submit', form: 'form-massa-remover', class: 'btn-perigo', disabled: true }, 'Remover');
  const chips = [...contagem.entries()]
    .sort(([a], [b]) => a.localeCompare(b, 'pt-BR', { sensitivity: 'base' }))
    .map(([t, n]) => {
      const b = h('button', { type: 'button', class: 'chip chip-filtro', 'aria-pressed': 'false' });
      const desenhar = () => {
        const on = marcadas.has(t);
        b.setAttribute('aria-pressed', String(on));
        b.replaceChildren(...[on ? icone('check', 14) : null, t, h('span', { class: 'opacity-75' }, `· ${n}`)].filter(Boolean));
      };
      b.addEventListener('click', () => {
        marcadas.has(t) ? marcadas.delete(t) : marcadas.add(t);
        desenhar();
        salvar.disabled = marcadas.size === 0;
      });
      desenhar();
      return b;
    });
  const erro = h('p', { class: 'alerta-erro hidden', role: 'alert' });
  const form = h('form', { id: 'form-massa-remover', class: 'space-y-4', novalidate: true },
    h('p', { class: 'text-sm text-suave' },
      `Marque as tags que serão tiradas de ${plural(videos.length, 'vídeo selecionado', 'vídeos selecionados')}. O número indica quantos deles têm a tag.`),
    h('div', { class: 'flex flex-wrap gap-2.5', role: 'group', 'aria-label': 'Tags para remover' }, chips),
    erro);

  const r = abrirModal({
    titulo: 'Remover tags',
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        if (!marcadas.size) return;
        salvar.disabled = true;
        try { fechar({ n: await aplicar(estado, [], [...marcadas]), lista: [...marcadas] }); } catch (ex) {
          erro.textContent = ex.message; erro.classList.remove('hidden'); salvar.disabled = false;
        }
      });
      return [h('button', { type: 'button', class: 'btn', onclick: () => fechar() }, 'Cancelar'), salvar];
    },
  });
  chips[0]?.focus();
  const res = await r;
  if (res) toast(`${plural(res.lista.length, 'tag removida', 'tags removidas')} de ${plural(res.n, 'vídeo', 'vídeos')}.`);
}

async function mudarCategoria(estado) {
  const videos = videosSelecionados(estado);
  // Onde os selecionados estão hoje, para a pessoa saber o que vai mudar.
  const hoje = new Map();
  for (const v of videos) hoje.set(v.categoria, (hoje.get(v.categoria) ?? 0) + 1);
  const resumoHoje = [...hoje.entries()].sort((a, b) => b[1] - a[1]).map(([c, n]) => `${c} (${n})`).join(', ');
  const unica = hoje.size === 1 ? [...hoje.keys()][0] : null;

  const campo = h('input', {
    id: 'massa-categoria', class: 'campo w-full', maxlength: 60, list: 'massa-categorias', autocomplete: 'off',
    placeholder: 'Sem categoria', value: unica && unica !== 'Sem categoria' ? unica : '', 'aria-describedby': 'massa-categoria-aviso',
  });
  const aviso = h('p', { id: 'massa-categoria-aviso', class: 'mt-2 text-sm text-suave', 'aria-live': 'polite' });
  const erro = h('p', { class: 'alerta-erro hidden', role: 'alert' });
  const salvar = h('button', { type: 'submit', form: 'form-massa-categoria', class: 'btn-primario' }, 'Mudar categoria');
  const n = plural(videos.length, 'vídeo selecionado', 'vídeos selecionados');

  function atualizarAviso() {
    const nome = campo.value.trim();
    const existente = estado.categorias.find((c) => c !== 'Sem categoria' && c.toLowerCase() === nome.toLowerCase());
    aviso.textContent = !nome || nome.toLowerCase() === 'sem categoria'
      ? `Os ${n} ficarão sem categoria.`
      : existente ? `Os ${n} vão para “${existente}” (categoria que já existe).`
      : `Os ${n} vão para a nova categoria “${nome}”.`;
  }
  campo.addEventListener('input', atualizarAviso);

  const form = h('form', { id: 'form-massa-categoria', class: 'space-y-4', novalidate: true },
    h('p', { class: 'text-sm text-suave' }, 'Hoje: ', resumoHoje, '. Só a categoria muda; título, tags, favorito e capa continuam.'),
    h('div', {},
      h('label', { for: 'massa-categoria', class: 'rotulo' }, 'Nova categoria'),
      campo,
      h('datalist', { id: 'massa-categorias' }, estado.categorias.filter((c) => c !== 'Sem categoria').map((c) => h('option', { value: c }))),
      aviso),
    erro);

  const r = abrirModal({
    titulo: 'Mudar categoria',
    corpo: form,
    acoes: (fechar) => {
      form.addEventListener('submit', async (e) => {
        e.preventDefault();
        salvar.disabled = true;
        try {
          const res = await bridge.call('videos.alterarCategoria', { slugs: [...selecionados], categoria: campo.value });
          fechar({ alterados: res.alterados, nome: campo.value.trim() });
        } catch (ex) {
          erro.textContent = ex.message;
          erro.classList.remove('hidden');
          salvar.disabled = false;
        }
      });
      return [h('button', { type: 'button', class: 'btn', onclick: () => fechar() }, 'Cancelar'), salvar];
    },
  });
  atualizarAviso();
  campo.focus();
  campo.select();
  const res = await r;
  if (!res) return;
  toast(res.alterados
    ? `Categoria alterada em ${plural(res.alterados, 'vídeo', 'vídeos')}.`
    : 'Os vídeos selecionados já estavam nessa categoria.');
}
