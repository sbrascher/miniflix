// Ponte JS <-> C# (WebView2).
//   await bridge.call('catalogo.listar')        -> resposta do C#
//   bridge.on('catalogo.atualizado', fn)        -> eventos espontâneos do C#

const pendentes = new Map();
const ouvintes = new Map();
let seq = 0;

window.chrome?.webview?.addEventListener('message', (ev) => {
  const msg = ev.data;
  if (!msg) return;
  if (msg.evento) {
    for (const fn of ouvintes.get(msg.tipo) ?? []) {
      try { fn(msg.dados); } catch (e) { console.error(e); }
    }
    return;
  }
  const p = msg.idRequisicao && pendentes.get(msg.idRequisicao);
  if (!p) return;
  pendentes.delete(msg.idRequisicao);
  msg.erro ? p.reject(new Error(msg.erro)) : p.resolve(msg.dados);
});

export const bridge = {
  disponivel: Boolean(window.chrome?.webview),

  call(tipo, dados = {}) {
    if (!window.chrome?.webview) return Promise.reject(new Error('Esta página precisa ser aberta pelo MiniFlix.exe'));
    const idRequisicao = String(++seq);
    return new Promise((resolve, reject) => {
      pendentes.set(idRequisicao, { resolve, reject });
      window.chrome.webview.postMessage({ tipo, idRequisicao, dados });
    });
  },

  on(tipo, fn) {
    if (!ouvintes.has(tipo)) ouvintes.set(tipo, new Set());
    ouvintes.get(tipo).add(fn);
    return () => ouvintes.get(tipo).delete(fn);
  },
};
