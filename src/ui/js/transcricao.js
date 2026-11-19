// Transcrição no player: painel com o texto do .md (horários clicáveis, trecho atual destacado, busca)
// e, para vídeos do YouTube sem transcrição, o botão para baixá-la.
import { bridge } from './bridge.js';
import { h, normalizar } from './util.js';
import { icone } from './icones.js';
import { toast } from './componentes.js';

/** "01:23" / "1:02:03" → segundos. */
function segundos(horario) {
  return horario.split(':').map(Number).reduce((acc, n) => acc * 60 + n, 0);
}

/** Lê o .md gerado pelo MiniFlix: cabeçalho (Origem/Idioma) e parágrafos "**[mm:ss]** texto". */
export function lerMarkdown(md) {
  const info = {};
  const paragrafos = [];
  for (const linha of md.replace(/\r\n/g, '\n').split('\n')) {
    const cab = /^- \*\*(Origem|Idioma):\*\* (.+)$/.exec(linha);
    if (cab) { info[cab[1].toLowerCase()] = cab[2]; continue; }
    const p = /^\*\*\[(\d{1,2}(?::\d{2}){1,2})\]\*\* (.+)$/.exec(linha);
    if (p) paragrafos.push({ horario: p[1], segundos: segundos(p[1]), texto: p[2] });
  }
  return { info, paragrafos };
}

/**
 * Seção de transcrição do player. Devolve { el, limpar }.
 * `video` é o elemento <video> (para pular e destacar o trecho atual).
 */
export function secaoTranscricao(v, video) {
  const raiz = h('div');
  const desligar = [];

  function painel(url, abrir) {
    const corpo = h('div', { class: 'px-6 pb-5' }, h('p', { class: 'text-sm text-suave' }, 'Carregando…'));
    const detalhes = h('details', { class: 'painel transcricao mt-6 max-w-3xl', open: abrir },
      h('summary', { class: 'flex cursor-pointer items-center gap-2 px-6 py-4 text-sm font-semibold' },
        icone('subtitles', 18), 'Transcrição',
        h('span', { class: 'font-normal text-suave' }, v.transcricaoAutomatica ? '· legenda automática' : '· legenda do autor')),
      corpo);

    let carregado = false;
    async function carregar() {
      if (carregado) return;
      carregado = true;
      try {
        const md = await (await fetch(url)).text();
        mostrar(lerMarkdown(md));
      } catch {
        corpo.replaceChildren(h('p', { class: 'alerta-erro' }, 'Não foi possível abrir a transcrição.'));
        carregado = false;
      }
    }

    function mostrar({ info, paragrafos }) {
      const busca = h('input', {
        type: 'search', class: 'campo w-full text-sm', placeholder: 'Buscar na transcrição…', 'aria-label': 'Buscar na transcrição',
      });
      const vazio = h('p', { class: 'hidden py-4 text-sm text-suave' }, 'Nenhum trecho com essas palavras.');
      const itens = paragrafos.map((p) => {
        const el = h('li', { class: 'transcricao-paragrafo' },
          h('button', {
            type: 'button', class: 'transcricao-tempo', title: 'Ir para este trecho', 'aria-label': `Ir para ${p.horario}`,
            onclick: () => { video.currentTime = p.segundos; video.play().catch(() => {}); },
          }, p.horario),
          h('span', {}, p.texto));
        return { ...p, el, normalizado: normalizar(p.texto) };
      });
      busca.addEventListener('input', () => {
        const termos = normalizar(busca.value).split(/\s+/).filter(Boolean);
        let algum = false;
        for (const i of itens) {
          const ok = termos.every((t) => i.normalizado.includes(t));
          i.el.hidden = !ok;
          algum ||= ok;
        }
        vazio.classList.toggle('hidden', algum);
      });

      // Destaca o parágrafo que está tocando agora.
      let atual = null;
      const destacar = () => {
        const t = video.currentTime;
        let achado = null;
        for (const i of itens) { if (i.segundos <= t) achado = i; else break; }
        if (achado !== atual) {
          atual?.el.classList.remove('atual');
          achado?.el.classList.add('atual');
          atual = achado;
        }
      };
      video.addEventListener('timeupdate', destacar);
      desligar.push(() => video.removeEventListener('timeupdate', destacar));
      destacar();

      corpo.replaceChildren(
        h('p', { class: 'mb-3 text-xs text-suave' }, [info.idioma, info.origem].filter(Boolean).join(' · ')),
        busca,
        h('ol', { class: 'mt-3 max-h-[28rem] space-y-1 overflow-y-auto pr-1' }, itens.map((i) => i.el)),
        vazio);
    }

    detalhes.addEventListener('toggle', () => { if (detalhes.open) carregar(); });
    if (abrir) carregar();
    return detalhes;
  }

  function botaoBaixar() {
    const status = h('span', { class: 'text-sm text-suave', 'aria-live': 'polite' });
    const botao = h('button', { type: 'button', class: 'btn' }, icone('subtitles', 18), 'Baixar transcrição');
    botao.addEventListener('click', async () => {
      botao.disabled = true;
      try {
        await bridge.call('transcricao.gerar', { slugs: [v.slug] });
        status.textContent = 'Baixando a transcrição…';
        aguardar(status, botao);
      } catch (e) {
        botao.disabled = false;
        toast(e.message, 'erro');
      }
    });
    return h('div', { class: 'mt-6 flex flex-wrap items-center gap-3' }, botao, status);
  }

  /** Espera a fila terminar: se deu certo, troca o botão pelo painel; se falhou, mostra o motivo. */
  function aguardar(status, botao) {
    const parar = bridge.on('fila.atualizada', async (itens) => {
      const item = itens.find((i) => i.tipo === 'transcricao' && i.origem === v.urlOrigem && i.finalizado);
      if (!item) return;
      parar();
      if (item.estado === 'concluido') {
        const novo = await bridge.call('video.obter', { slug: v.slug });
        Object.assign(v, novo);
        if (v.transcricaoUrl) raiz.replaceChildren(painel(v.transcricaoUrl, true));
      } else {
        status.textContent = item.mensagem || 'Não foi possível baixar a transcrição.';
        botao.disabled = false;
      }
    });
    desligar.push(parar);
  }

  if (v.transcricaoUrl) raiz.append(painel(v.transcricaoUrl, false));
  else if (v.origem === 'youtube' && v.urlOrigem) raiz.append(botaoBaixar());

  return { el: raiz, limpar: () => desligar.forEach((f) => f()) };
}
