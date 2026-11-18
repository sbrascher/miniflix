import { bridge } from './bridge.js';
import { h } from './util.js';
import { icone } from './icones.js';
import { renderCatalogo } from './catalogo.js';
import { renderPlayer } from './player.js';
import { renderAdmin } from './admin.js';

const $ = (s) => document.querySelector(s);
const raiz = $('#app');
const campoBusca = $('#busca');
const campoCategoria = $('#categoria');

const estado = {
  videos: [],
  categorias: [],
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
  if (estado.categoria && !estado.categorias.includes(estado.categoria)) estado.categoria = '';

  campoCategoria.replaceChildren(
    h('option', { value: '' }, 'Todas as categorias'),
    ...estado.categorias.map((c) => h('option', { value: c, selected: c === estado.categoria }, c)));
}

function atualizarCatalogoNaTela() {
  if (rotaAtual().nome === 'catalogo') renderCatalogo(raiz, estado);
}

/** Depois de editar/excluir/recarregar: redesenha a tela atual com os dados novos. */
function atualizarTelaAtual() {
  const rota = rotaAtual().nome;
  if (rota === 'catalogo') renderCatalogo(raiz, estado);
  else if (rota === 'admin') renderAdmin(raiz, estado);
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
  if (e.key === '/' && !digitando && !e.ctrlKey && !e.altKey) {
    e.preventDefault();
    campoBusca.focus();
    campoBusca.select();
  }
});

bridge.on('catalogo.atualizado', async () => {
  await carregarCatalogo();
  atualizarTelaAtual();
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
    renderCatalogo(raiz, estado);
    window.scrollTo(0, anterior === 'player' ? rolagemCatalogo : 0);
  }
}

window.addEventListener('hashchange', navegar);

// ---------- Início ----------
$('#icone-busca').append(icone('search', 18));
$('#btn-admin').append(icone('settings', 20));
aplicarTema(estado.tema);

(async () => {
  try {
    const cfg = await bridge.call('config.obter');
    aplicarTema(cfg.tema);
    await carregarCatalogo();
    navegar();
  } catch (e) {
    raiz.replaceChildren(h('div', { class: 'mx-auto mt-24 max-w-md text-center' },
      h('div', { class: 'mx-auto mb-4 w-fit text-destaque' }, icone('warning', 40, true)),
      h('h1', { class: 'font-display text-xl font-bold' }, 'Não foi possível carregar o catálogo'),
      h('p', { class: 'mt-2 text-suave' }, e.message)));
  }
})();
