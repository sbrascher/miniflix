// Painel "Pasta dos vídeos" da administração: mostra onde está o acervo e permite apontar para outra pasta.
import { bridge } from './bridge.js';
import { h, plural } from './util.js';
import { icone } from './icones.js';
import { toast, recarregarComAviso } from './componentes.js';

/** `estado.pasta` = { caminho, padrao, disponivel } (vem de catalogo.listar). */
export function painelPasta(estado) {
  const pasta = estado.pasta ?? { caminho: '', padrao: true, disponivel: true };
  const erro = h('p', { class: 'alerta-erro mt-3 hidden', role: 'alert' });
  const mostrarErro = (msg) => { erro.textContent = msg ?? ''; erro.classList.toggle('hidden', !msg); };

  /** Executa uma troca de pasta; a tela é redesenhada pelo evento catalogo.atualizado. */
  async function trocar(botao, comando) {
    mostrarErro('');
    botoes.forEach((b) => { b.disabled = true; });
    try {
      const r = await bridge.call(comando);
      if (!r) return; // diálogo cancelado
      const aviso = `Pasta alterada. ${plural(r.videos, 'vídeo encontrado', 'vídeos encontrados')}.`;
      if (r.recarregarPagina) { recarregarComAviso(aviso); return; }
      toast(aviso);
    } catch (e) {
      mostrarErro(e.message);
    } finally {
      botoes.forEach((b) => { if (b.isConnected) b.disabled = false; });
      if (botao.isConnected) botao.focus();
    }
  }

  const alterar = h('button', { type: 'button', class: 'btn-primario' }, icone('drive_folder_upload', 18), 'Escolher outra pasta…');
  alterar.addEventListener('click', () => trocar(alterar, 'pasta.escolher'));

  const abrir = h('button', { type: 'button', class: 'btn', disabled: !pasta.disponivel }, icone('folder_open', 18), 'Abrir no Explorer');
  abrir.addEventListener('click', () => bridge.call('pasta.abrir').catch((e) => toast(e.message, 'erro')));

  const padrao = pasta.padrao ? null
    : h('button', { type: 'button', class: 'btn' }, icone('restart_alt', 18), 'Voltar para a pasta padrão');
  padrao?.addEventListener('click', () => trocar(padrao, 'pasta.usarPadrao'));

  const botoes = [alterar, abrir, padrao].filter(Boolean);

  return h('section', { class: 'painel mt-6 flex gap-5 p-6', 'aria-labelledby': 'titulo-pasta' },
    h('div', { class: 'pastilha' }, icone(pasta.disponivel ? 'folder' : 'folder_off', 24)),
    h('div', { class: 'min-w-0 flex-1' },
      h('h2', { id: 'titulo-pasta', class: 'flex flex-wrap items-center gap-2 font-semibold' }, 'Pasta dos vídeos',
        pasta.padrao ? h('span', { class: 'chip font-medium text-suave' }, 'Padrão') : null),
      h('p', { class: 'mt-1 text-sm text-suave' },
        'É onde o MiniFlix lê o acervo e grava os vídeos importados ou baixados. Pode ser outra pasta, outra unidade ou um HD externo.'),
      h('p', { class: 'bandeja mt-4 break-all px-4 py-2.5 font-mono text-sm' }, pasta.caminho),
      pasta.disponivel ? null : h('p', { class: 'alerta-erro mt-3', role: 'alert' },
        'Pasta não encontrada. Se ela fica num HD externo ou pendrive, conecte-o e clique em “Recarregar pasta” no Acervo.'),
      h('div', { class: 'mt-4 flex flex-wrap gap-3' }, botoes),
      erro,
      h('p', { class: 'mt-3 text-xs text-suave' },
        'Trocar a pasta não move nem apaga nada: os vídeos da pasta atual continuam onde estão. ',
        'Só aparecem os vídeos organizados pelo MiniFlix — arquivos soltos na pasta nova devem ser trazidos por “Importar do computador”.')));
}
