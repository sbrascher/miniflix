// Botão de favoritar (estrela), usado no player e na tabela da administração.
import { bridge } from './bridge.js';
import { h } from './util.js';
import { icone } from './icones.js';
import { toast } from './componentes.js';

/**
 * Estrela que marca/desmarca o vídeo como favorito. Atualiza na hora (otimista) e desfaz se falhar.
 * comTexto: mostra "Favorito" ao lado (player); sem texto, é um botão só com ícone (tabela).
 */
export function botaoFavorito(v, { comTexto = false, classe = comTexto ? 'btn btn-favorito' : 'btn-icone btn-favorito' } = {}) {
  let favorito = !!v.favorito;
  // data-foco: a tela é redesenhada após salvar e o foco volta para esta mesma estrela.
  const botao = h('button', { type: 'button', class: classe, 'data-foco': `favorito:${v.slug}` });

  function desenhar() {
    botao.setAttribute('aria-pressed', String(favorito));
    // Rótulo fixo + aria-pressed (o leitor de tela diz "Favorito, pressionado"); a dica muda conforme o estado.
    botao.title = favorito ? 'Remover dos favoritos' : 'Marcar como favorito';
    if (!comTexto) botao.setAttribute('aria-label', `Favorito: ${v.titulo}`);
    botao.replaceChildren(...[icone('star', comTexto ? 18 : 20, favorito), comTexto ? 'Favorito' : null].filter(Boolean));
  }

  // Sem "disabled" enquanto salva: um botão desabilitado perde o foco (ruim para quem usa o teclado).
  let ocupado = false;
  botao.addEventListener('click', async () => {
    if (ocupado) return;
    ocupado = true;
    favorito = !favorito;
    desenhar();
    try {
      await bridge.call('video.favoritar', { slug: v.slug, favorito });
      v.favorito = favorito;
    } catch (e) {
      favorito = !favorito;
      desenhar();
      toast(e.message, 'erro');
    } finally {
      ocupado = false;
    }
  });

  desenhar();
  return botao;
}
