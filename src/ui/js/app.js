import { bridge } from './bridge.js';
import { h, plural } from './util.js';
import { icone } from './icones.js';
import { renderCatalogo, focarAposRemocao } from './catalogo.js';
import { renderPlayer } from './player.js';
import { renderAdmin } from './admin.js';
import { atualizarFila } from './fila.js';
import { confirmar, toast, recarregarComAviso, mostrarAvisoPendente } from './componentes.js';
import { carregarProgresso } from './progresso.js';

const $ = (s) => document.querySelector(s);
const raiz = $('#app');
const campoBusca = $('#busca');
const campoCategoria = $('#categoria');

const estado = {
  videos: [],
  categorias: [],
  tags: [],              // todas as tags do acervo
  tagsSelecionadas: [],  // filtro do catálogo (o vídeo precisa ter todas)
  pasta: null,           // { caminho, padrao, disponivel }
  busca: '',
  categoria: '',
  tema: document.documentElement.dataset.theme || 'escuro',
};
let limparTela = () => {};
let rolagemCatalogo = 0;

// ---------- Tema ----------
function aplicarTema(tema) {
  estado.tema = tema;
  document.documentElement.dataset.theme = tema;
  const btn = $('#btn-tema');
  const paraClaro = tema === 'escuro';
  btn.replaceChildren(icone(paraClaro ? 'light_mode' : 'dark_mode'));
  btn.setAttribute('aria-label', paraClaro ? 'Ativar modo claro' : 'Ativar modo escuro');
  btn.title = btn.getAttribute('aria-label');
}

$('#btn-tema').addEventListener('click', async () => {
  const novo = estado.tema === 'escuro' ? 'claro' : 'escuro';
  aplicarTema(novo);
  try { await bridge.call('config.salvar', { tema: novo }); } catch (e) { console.error(e); }
});

// ---------- Catálogo ----------
async function carregarCatalogo() {
  const r = await bridge.call('catalogo.listar');
  estado.videos = r.videos;
  estado.categorias = r.categorias;
  estado.tags = r.tags;
  estado.descricoesCategorias = r.descricoesCategorias ?? {};
  estado.pasta = r.pasta;
  if (estado.categoria && !estado.categorias.includes(estado.categoria)) estado.categoria = '';
  estado.tagsSelecionadas = estado.tagsSelecionadas.filter((t) => estado.tags.includes(t));

  campoCategoria.replaceChildren(
    h('option', { value: '' }, 'Todas as categorias'),
    ...estado.categorias.map((c) => h('option', { value: c, selected: c === estado.categoria }, c)));
}

/** Desenha o catálogo; ao tirar/devolver um vídeo de "Continuar assistindo", redesenha mantendo a rolagem. */
function desenharCatalogo() {
  renderCatalogo(raiz, estado, {
    aoAlterarProgresso: ({ focarIndice } = {}) => {
      const rolagem = window.scrollY;
      desenharCatalogo();
      window.scrollTo(0, rolagem);
      if (focarIndice !== undefined) focarAposRemocao(raiz, focarIndice);
    },
    // tag = null limpa todas. O foco volta ao mesmo chip depois de redesenhar.
    aoAlternarTag: (tag) => {
      const sel = estado.tagsSelecionadas;
      estado.tagsSelecionadas = tag === null ? [] : sel.includes(tag) ? sel.filter((t) => t !== tag) : [...sel, tag];
      const rolagem = window.scrollY;
      desenharCatalogo();
      window.scrollTo(0, rolagem);
      const alvo = tag === null ? raiz.querySelector('.chip-filtro') : raiz.querySelector(`.chip-filtro[data-tag="${CSS.escape(tag)}"]`);
      alvo?.focus({ preventScroll: true });
    },
    aoRecarregar: async () => {
      try {
        const r = await bridge.call('catalogo.recarregar');
        if (r?.recarregarPagina) recarregarComAviso('Pasta de vídeos encontrada.');
        else if (!r?.pasta?.disponivel) toast('A pasta ainda não foi encontrada.', 'erro');
      } catch (e) { toast(e.message, 'erro'); }
    },
  });
}

/** Clique numa tag fora do catálogo (ex.: no player): mostra o catálogo filtrado só por ela. */
document.addEventListener('miniflix:filtrar-tag', (e) => {
  estado.tagsSelecionadas = [e.detail];
  estado.busca = ''; campoBusca.value = '';
  estado.categoria = ''; campoCategoria.value = '';
  rolagemCatalogo = 0;
  if (rotaAtual().nome === 'catalogo') atualizarCatalogoNaTela();
  else location.hash = '#/';
});
function atualizarCatalogoNaTela() {
  if (rotaAtual().nome === 'catalogo') desenharCatalogo();
}

/** Depois de editar/excluir/recarregar: redesenha a tela atual com os dados novos. */
function atualizarTelaAtual() {
  const rota = rotaAtual().nome;
  // Elementos com data-foco recebem o foco de volta depois de redesenhar (ex.: a estrela clicada).
  const foco = document.activeElement?.dataset?.foco;
  const rolagem = window.scrollY;
  if (rota === 'catalogo') desenharCatalogo();
  else if (rota === 'admin') renderAdmin(raiz, estado);
  else return;
  window.scrollTo(0, rolagem);
  if (foco) raiz.querySelector(`[data-foco="${CSS.escape(foco)}"]`)?.focus({ preventScroll: true });
}

campoBusca.addEventListener('input', () => { estado.busca = campoBusca.value; irParaCatalogoSeNecessario(); atualizarCatalogoNaTela(); });
campoCategoria.addEventListener('change', () => { estado.categoria = campoCategoria.value; irParaCatalogoSeNecessario(); atualizarCatalogoNaTela(); });
campoBusca.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') { campoBusca.value = ''; estado.busca = ''; atualizarCatalogoNaTela(); campoBusca.blur(); }
});

function irParaCatalogoSeNecessario() {
  if (rotaAtual().nome !== 'catalogo') location.hash = '#/';
}

// "/" foca a busca (quando não se está digitando em outro campo).
document.addEventListener('keydown', (e) => {
  const alvo = e.target;
  const digitando = alvo instanceof HTMLElement && (alvo.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(alvo.tagName));
  if (e.key === '/' && !digitando && !e.ctrlKey && !e.altKey && rotaAtual().nome === 'catalogo') {
    e.preventDefault();
    campoBusca.focus();
    campoBusca.select();
  }
});

bridge.on('catalogo.atualizado', async () => {
  await Promise.all([carregarCatalogo(), carregarProgresso()]);
  atualizarTelaAtual();
});

// ---------- Fila ----------
const estadosAnteriores = new Map();
// "Rodada" = tudo o que passou pela fila desde a última vez que ela ficou parada.
// Com um item só, avisa ao terminar; com vários (lote do YouTube, pasta inteira), um resumo no fim.
const rodada = { itens: new Set(), concluidos: 0, erros: 0, tipos: new Set() };

function aoMudarFila(itens) {
  const ocupada = itens.some((i) => i.estado === 'aguardando' || i.estado === 'processando');
  for (const i of itens) {
    if (i.estado === 'aguardando' || i.estado === 'processando') { rodada.itens.add(i.id); rodada.tipos.add(i.tipo); }
  }
  const emLote = rodada.itens.size > 1;

  for (const i of itens) {
    const antes = estadosAnteriores.get(i.id);
    if (antes && antes !== i.estado) {
      if (i.estado === 'concluido') {
        rodada.concluidos++;
        if (!emLote) toast(i.tipo === 'transcricao' ? `Transcrição de “${i.titulo}” pronta.` : `“${i.titulo}” foi adicionado ao catálogo.`);
      } else if (i.estado === 'erro') {
        rodada.erros++;
        if (!emLote) toast(`Falha em “${i.titulo}”: ${i.mensagem}`, 'erro');
      }
    }
    estadosAnteriores.set(i.id, i.estado);
  }
  atualizarFila(itens);

  if (!ocupada && rodada.itens.size) {
    if (emLote && rodada.concluidos + rodada.erros > 0) {
      const soTranscricoes = rodada.tipos.size === 1 && rodada.tipos.has('transcricao');
      const partes = [soTranscricoes
        ? `${plural(rodada.concluidos, 'transcrição pronta', 'transcrições prontas')}`
        : `${plural(rodada.concluidos, 'vídeo adicionado', 'vídeos adicionados')} ao catálogo`];
      if (rodada.erros) partes.push(`${plural(rodada.erros, 'com erro', 'com erro')} (detalhes na fila da Administração)`);
      toast(`Fila concluída: ${partes.join(', ')}.`, rodada.erros ? 'erro' : 'ok');
    }
    rodada.itens.clear();
    rodada.tipos.clear();
    rodada.concluidos = 0;
    rodada.erros = 0;
  }

  // Indicador na engrenagem enquanto há trabalho em andamento.
  const btn = $('#btn-admin');
  btn.querySelector('.indicador')?.remove();
  if (ocupada) btn.append(h('span', { class: 'indicador animate-pulse', 'aria-hidden': 'true' }));
  btn.title = ocupada ? 'Área administrativa — processando vídeos' : 'Área administrativa';
}

bridge.on('fila.atualizada', aoMudarFila);

// Fechar a janela com a fila trabalhando: o C# pergunta por aqui.
bridge.on('app.confirmarSaida', async () => {
  const sair = await confirmar({
    titulo: 'Sair do MiniFlix?',
    mensagem: 'Há vídeos sendo importados ou baixados. Se sair agora, o processamento será cancelado e os arquivos incompletos serão descartados.',
    textoConfirmar: 'Cancelar e sair',
    perigoso: true,
  });
  if (sair) bridge.call('app.sair');
});

// ---------- Rotas ----------
function rotaAtual() {
  const partes = location.hash.replace(/^#\/?/, '').split('/');
  if (partes[0] === 'video' && partes[1]) return { nome: 'player', slug: decodeURIComponent(partes[1]) };
  if (partes[0] === 'admin') return { nome: 'admin' };
  return { nome: 'catalogo' };
}

function navegar() {
  const anterior = estado.rotaNome;
  if (anterior === 'catalogo') rolagemCatalogo = window.scrollY;
  limparTela();
  limparTela = () => {};

  const rota = rotaAtual();
  estado.rotaNome = rota.nome;
  $('#filtros').classList.toggle('invisible', rota.nome !== 'catalogo');

  if (rota.nome === 'player') {
    document.title = 'MiniFlix';
    const v = estado.videos.find((x) => x.slug === rota.slug);
    limparTela = renderPlayer(raiz, v);
    if (v) document.title = `${v.titulo} — MiniFlix`;
    window.scrollTo(0, 0);
  } else if (rota.nome === 'admin') {
    document.title = 'Administração — MiniFlix';
    renderAdmin(raiz, estado);
    window.scrollTo(0, 0);
  } else {
    document.title = 'MiniFlix';
    desenharCatalogo();
    window.scrollTo(0, anterior === 'player' ? rolagemCatalogo : 0);
  }
}

window.addEventListener('hashchange', navegar);

// ---------- Início ----------
aplicarTema(estado.tema);

(async () => {
  try {
    const cfg = await bridge.call('config.obter');
    aplicarTema(cfg.tema);
    await Promise.all([carregarCatalogo(), carregarProgresso()]);
    aoMudarFila(await bridge.call('fila.listar'));
    navegar();
    mostrarAvisoPendente();
  } catch (e) {
    raiz.replaceChildren(h('div', { class: 'mx-auto mt-24 max-w-md text-center' },
      h('div', { class: 'mx-auto mb-4 w-fit text-destaque' }, icone('warning', 40, true)),
      h('h1', { class: 'font-display text-xl font-bold' }, 'Não foi possível carregar o catálogo'),
      h('p', { class: 'mt-2 text-suave' }, e.message)));
  }
})();
