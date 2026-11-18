// Utilidades de DOM e formatação. Dados do usuário SEMPRE entram como texto (nunca innerHTML).

/**
 * h('a', { href: '#/', class: 'btn', onclick: fn }, 'texto', outroNo)
 * Atributos null/false são ignorados; "on*" viram listeners.
 */
export function h(tag, attrs = {}, ...filhos) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs ?? {})) {
    if (v == null || v === false) continue;
    if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2), v);
    else if (k === 'style' && typeof v === 'object') {
      // Aceita propriedades CSS customizadas: { '--matiz': 120 }
      for (const [prop, valor] of Object.entries(v)) {
        if (prop.startsWith('--')) el.style.setProperty(prop, String(valor));
        else el.style[prop] = valor;
      }
    }
    else el.setAttribute(k, v === true ? '' : String(v));
  }
  for (const f of filhos.flat(Infinity)) {
    if (f == null || f === false) continue;
    el.append(f instanceof Node ? f : document.createTextNode(String(f)));
  }
  return el;
}

/** Remove acentos e caixa: "Introdução" -> "introducao". */
export const normalizar = (s) => (s ?? '').normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();

/** 1834 -> "30:34"; 4000 -> "1:06:40". */
export function fmtDuracao(seg) {
  if (!seg || !isFinite(seg)) return '';
  seg = Math.round(seg);
  const hh = Math.floor(seg / 3600), mm = Math.floor((seg % 3600) / 60), ss = seg % 60;
  const p = (n) => String(n).padStart(2, '0');
  return hh ? `${hh}:${p(mm)}:${p(ss)}` : `${mm}:${p(ss)}`;
}

/** "2026-03-10" -> "10 de mar. de 2026" (sem problemas de fuso). */
export function fmtData(iso) {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso ?? '');
  if (!m) return '';
  return new Date(+m[1], +m[2] - 1, +m[3]).toLocaleDateString('pt-BR', { day: 'numeric', month: 'short', year: 'numeric' });
}

/** Matiz estável (0–359) a partir de um texto — usado nas capas substitutas. */
export function matiz(texto) {
  let x = 0;
  for (const c of texto ?? '') x = (x * 31 + c.charCodeAt(0)) >>> 0;
  return x % 360;
}

/** 1536 -> "1,5 KB". */
export function fmtBytes(b) {
  const u = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  while (b >= 1024 && i < u.length - 1) { b /= 1024; i++; }
  return `${b.toLocaleString('pt-BR', { maximumFractionDigits: i ? 1 : 0 })} ${u[i]}`;
}

/** "2026-11-18T09:20:00" -> "18/11/2026". */
export function fmtDataHora(iso) {
  const d = new Date(iso);
  return isNaN(d) || d.getFullYear() < 2000 ? '' : d.toLocaleDateString('pt-BR');
}

export const plural =(n, um, varios) => `${n} ${n === 1 ? um : varios}`;
