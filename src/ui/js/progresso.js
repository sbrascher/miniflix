// "Continuar assistindo": estado do progresso de cada vídeo (fonte da verdade no C#, data/progress.json).
import { bridge } from './bridge.js';

/** Abaixo disto (segundos) não vale a pena oferecer "continuar". */
const MINIMO_SEGUNDOS = 5;
/** A partir desta fração o vídeo conta como assistido (igual ao C#). */
export const FRACAO_ASSISTIDO = 0.95;

let mapa = {}; // slug -> { posicao, duracao, assistido, atualizadoEm }

export async function carregarProgresso() {
  mapa = await bridge.call('progresso.listar');
  return mapa;
}

export const obterProgresso = (slug) => mapa[slug] ?? null;

export const fracao = (p) => (p && p.duracao > 0 ? Math.min(p.posicao / p.duracao, 1) : 0);

/** Vídeo começado e não terminado: aparece em "Continuar assistindo" e com barra no card. */
export const emAndamento = (p) => !!p && p.posicao >= MINIMO_SEGUNDOS && fracao(p) < FRACAO_ASSISTIDO;

/** Lista para a fileira "Continuar assistindo", do mais recente para o mais antigo. */
export function listaContinuar(videos, limite = 12) {
  return videos
    .filter((v) => emAndamento(mapa[v.slug]))
    .sort((a, b) => new Date(mapa[b.slug].atualizadoEm) - new Date(mapa[a.slug].atualizadoEm))
    .slice(0, limite);
}

export async function registrarProgresso(slug, posicao, duracao) {
  if (!slug || !(duracao > 0) || !Number.isFinite(posicao)) return;

  // Atualização otimista (mesma regra do C#): a tela seguinte — ex.: o catálogo logo
  // após sair do player — já enxerga a posição nova sem esperar a resposta.
  const anterior = mapa[slug];
  const terminou = posicao / duracao >= FRACAO_ASSISTIDO;
  mapa[slug] = {
    posicao: terminou ? 0 : posicao,
    duracao,
    assistido: terminou || !!anterior?.assistido,
    atualizadoEm: new Date().toISOString(),
  };

  try {
    mapa[slug] = await bridge.call('progresso.salvar', { slug, posicao, duracao });
  } catch (e) {
    console.warn('Progresso não salvo:', e.message);
  }
}

export async function reiniciarProgresso(slug) {
  if (mapa[slug]) mapa[slug] = { ...mapa[slug], posicao: 0 };
  await bridge.call('progresso.reiniciar', { slug }).catch(() => {});
}

export function esquecerProgresso(slug) {
  delete mapa[slug];
}
