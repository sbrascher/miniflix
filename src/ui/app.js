'use strict';

// ---------- Ponte JS <-> C# ----------
const bridge = (() => {
  const pendentes = new Map();
  let seq = 0;
  window.chrome?.webview?.addEventListener('message', (ev) => {
    const msg = ev.data;
    const p = msg?.idRequisicao && pendentes.get(msg.idRequisicao);
    if (!p) return;
    pendentes.delete(msg.idRequisicao);
    msg.erro ? p.reject(new Error(msg.erro)) : p.resolve(msg.dados);
  });
  return {
    call(tipo, dados = {}) {
      if (!window.chrome?.webview) return Promise.reject(new Error('Fora do WebView2'));
      const idRequisicao = String(++seq);
      return new Promise((resolve, reject) => {
        pendentes.set(idRequisicao, { resolve, reject });
        window.chrome.webview.postMessage({ tipo, idRequisicao, dados });
      });
    }
  };
})();

// ---------- Utilidades ----------
const $ = (s) => document.querySelector(s);
const fmtBytes = (b) => {
  const u = ['B', 'KB', 'MB', 'GB', 'TB']; let i = 0;
  while (b >= 1024 && i < u.length - 1) { b /= 1024; i++; }
  return `${b.toFixed(i ? 1 : 0)} ${u[i]}`;
};
const fmtTempo = (s) => {
  s = Math.floor(s); const h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60), ss = s % 60;
  return (h ? `${h}:` : '') + `${String(m).padStart(h ? 2 : 1, '0')}:${String(ss).padStart(2, '0')}`;
};
const resultados = [];
function registrar(teste, ok, detalhe) {
  resultados.push({ teste, ok, detalhe });
  const tr = document.createElement('tr');
  tr.innerHTML = `<td></td><td class="${ok ? 'ok' : 'fail'}">${ok ? 'OK' : 'FALHOU'}</td><td></td>`;
  tr.cells[0].textContent = teste;
  tr.cells[2].textContent = detalhe;
  $('#resultados tbody').append(tr);
}
async function comBotao(btn, fn) {
  btn.disabled = true;
  try { await fn(); } catch (e) { registrar(btn.textContent, false, e.message); }
  finally { btn.disabled = false; }
}
function esperarEvento(el, nome, timeoutMs) {
  return new Promise((resolve, reject) => {
    const t = setTimeout(() => { el.removeEventListener(nome, h); reject(new Error(`timeout esperando "${nome}"`)); }, timeoutMs);
    const h = () => { clearTimeout(t); resolve(); };
    el.addEventListener(nome, h, { once: true });
  });
}

// ---------- 1. Ambiente ----------
async function carregarInfo() {
  const i = await bridge.call('sistema.info');
  const linhas = [
    ['App', i.versaoApp], ['.NET', i.dotnet],
    ['WebView2', `${i.webview2Versao} — ${i.webview2Origem}`],
    ['Raiz do app', i.raiz], ['Dados WebView2', i.pastaDadosWebView],
    ['Unidade', `${i.unidade} (${i.unidadeTipo})`], ['Sistema de arquivos', i.sistemaArquivos],
    ['Espaço livre', fmtBytes(i.espacoLivreBytes)], ['Origem da página', location.origin],
  ];
  const dl = $('#info'); dl.innerHTML = '';
  for (const [k, v] of linhas) {
    const dt = document.createElement('dt'); dt.textContent = k;
    const dd = document.createElement('dd'); dd.textContent = v;
    dl.append(dt, dd);
  }
  if (i.sistemaArquivos.toUpperCase() === 'FAT32') {
    const a = $('#aviso-fs');
    a.textContent = 'Unidade em FAT32: arquivos acima de 4 GB não podem ser gravados. Recomendado: exFAT ou NTFS.';
    a.hidden = false;
  }
}

// ---------- 2. Ping ----------
async function medirPing() {
  const n = 20; const t0 = performance.now();
  for (let k = 0; k < n; k++) await bridge.call('ping');
  $('#ping').textContent = `${((performance.now() - t0) / n).toFixed(2)} ms`;
}

// ---------- 3. Vídeo ----------
const player = $('#player');
let videos = [];
const atual = () => videos[$('#lista').selectedIndex];

async function listarVideos() {
  videos = await bridge.call('teste.listarVideos');
  const sel = $('#lista'); sel.innerHTML = '';
  for (const v of videos) {
    const o = document.createElement('option');
    o.textContent = `${v.nome} (${fmtBytes(v.tamanhoBytes)})`;
    sel.append(o);
  }
  $('#sem-videos').hidden = videos.length > 0;
}

async function carregarVideo() {
  const v = atual(); if (!v) throw new Error('Nenhum vídeo selecionado');
  const t0 = performance.now();
  player.src = v.url;
  await esperarEvento(player, 'loadedmetadata', 15000);
  registrar('Carregar metadados', true,
    `${v.nome} — duração ${fmtTempo(player.duration)}, ${player.videoWidth}x${player.videoHeight}, ${Math.round(performance.now() - t0)} ms`);
}

async function testarRange() {
  const v = atual(); if (!v) throw new Error('Nenhum vídeo selecionado');
  const inicio = Math.max(0, v.tamanhoBytes - 1024);   // últimos 1 KB do arquivo
  const r = await fetch(v.url, { headers: { Range: `bytes=${inicio}-${inicio + 99}` } });
  const corpo = await r.arrayBuffer();
  const ok = r.status === 206 && corpo.byteLength === 100;
  registrar('Requisição Range', ok,
    `status ${r.status}, Content-Range: ${r.headers.get('Content-Range') ?? '—'}, ${corpo.byteLength} bytes recebidos`);
}

async function testarSeek() {
  if (!player.src || !isFinite(player.duration)) await carregarVideo();
  const d = player.duration;
  const alvos = [0.5, 0.9, 0.1, 0.75, 0.25, 0.98, 0.02];
  player.muted = true;
  await player.play().catch(() => {});
  for (const f of alvos) {
    const alvo = d * f;
    const t0 = performance.now();
    player.currentTime = alvo;
    try {
      await esperarEvento(player, 'seeked', 10000);
      const ms = Math.round(performance.now() - t0);
      const erro = Math.abs(player.currentTime - alvo);
      registrar(`Seek para ${Math.round(f * 100)}%`, erro < 1.5,
        `alvo ${fmtTempo(alvo)}, posição ${fmtTempo(player.currentTime)}, ${ms} ms`);
    } catch (e) {
      registrar(`Seek para ${Math.round(f * 100)}%`, false, e.message);
    }
  }
  // Depois do último seek, confirma que a reprodução continua avançando.
  const antes = player.currentTime;
  await new Promise((r) => setTimeout(r, 1500));
  registrar('Reprodução após seek', player.currentTime > antes,
    `${fmtTempo(antes)} → ${fmtTempo(player.currentTime)}`);
  player.pause();
  player.muted = false;
}

async function testarLiberacao() {
  const v = atual(); if (!v) throw new Error('Nenhum vídeo selecionado');
  if (!player.src) await carregarVideo();
  const carregado = await bridge.call('teste.arquivoTravado', { caminho: v.caminho });
  registrar('Arquivo com vídeo carregado', true,
    carregado.travado ? 'travado pelo player (esperado)' : 'não travado (o navegador não mantém handle exclusivo)');

  // Descarrega o vídeo como o app fará ao sair do player.
  player.pause();
  player.removeAttribute('src');
  player.load();
  const depois = await bridge.call('teste.arquivoTravado', { caminho: v.caminho });
  registrar('Liberação após descarregar', !depois.travado,
    depois.travado ? `ainda travado após ${depois.ms} ms` : `liberado em ${depois.ms} ms (${depois.tentativas} tentativa(s))`);
}

// ---------- Inicialização ----------
$('#btn-carregar').addEventListener('click', (e) => comBotao(e.currentTarget, carregarVideo));
$('#btn-range').addEventListener('click', (e) => comBotao(e.currentTarget, testarRange));
$('#btn-seek').addEventListener('click', (e) => comBotao(e.currentTarget, testarSeek));
$('#btn-liberar').addEventListener('click', (e) => comBotao(e.currentTarget, testarLiberacao));

(async () => {
  try {
    await carregarInfo();
    await medirPing();
    await listarVideos();
    if (new URLSearchParams(location.search).has('autoteste')) {
      for (const [nome, fn] of [['Carregar', carregarVideo], ['Range', testarRange],
                                ['Seek', testarSeek], ['Liberação', testarLiberacao]]) {
        try { await fn(); } catch (e) { registrar(nome, false, e.message); }
      }
      await bridge.call('teste.relatorio', {
        info: await bridge.call('sistema.info'), ping: $('#ping').textContent, resultados
      });
    }
  } catch (e) {
    $('#info').innerHTML = '';
    const p = document.createElement('dt'); p.className = 'fail'; p.textContent = 'Erro: ' + e.message;
    $('#info').append(p);
  }
})();
