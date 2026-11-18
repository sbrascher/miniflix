// Ícones da fonte Material Symbols Rounded (arquivo local em ui/fonts — funciona offline).
// Use qualquer nome de https://fonts.google.com/icons, por exemplo: icone('search'), icone('play_arrow', 24, true).

/**
 * @param {string} nome   nome do ícone no Material Symbols
 * @param {number} px     tamanho em pixels
 * @param {boolean} cheio versão preenchida
 */
export function icone(nome, px = 20, cheio = false) {
  const el = document.createElement('span');
  el.className = cheio ? 'icone icone-cheio' : 'icone';
  el.style.fontSize = `${px}px`;
  el.setAttribute('aria-hidden', 'true');
  el.textContent = nome;
  return el;
}
