import { initialState, reduce, desiredHeight } from './chat-core.js';

const host = window.chrome?.webview;
const post = msg => host?.postMessage(msg);
const $ = id => document.getElementById(id);
const els = { app: $('app'), messages: $('messages'), chips: $('chips'), input: $('input'), send: $('send'),
  plus: $('plus'), menu: $('menu'), backend: $('backend'), toast: $('toast'), theme: $('theme') };

const md = window.markdownit({ html: false, linkify: true, breaks: false,
  highlight: (code, lang) => {
    try { return lang && hljs.getLanguage(lang) ? hljs.highlight(code, { language: lang }).value : hljs.highlightAuto(code).value; }
    catch { return ''; }
  } });

let state = initialState();
const rendered = new Map(); // message id -> element

function dispatch(msg) {
  const prev = state;
  state = reduce(state, msg);
  if (state === prev) return;
  render(msg);
}

function escapeHtml(s) { return s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

function renderMessage(m) {
  let el = rendered.get(m.id);
  if (!el) { el = document.createElement('div'); rendered.set(m.id, el); els.messages.appendChild(el); }
  el.className = `msg ${m.role} ${m.status ?? ''}`;
  if (m.role === 'user') {
    const atts = (m.attachments ?? []).map(a => `📎 ${escapeHtml(a.name)}`).join('  ');
    el.innerHTML = escapeHtml(m.text) + (atts ? `<div class="att">${atts}</div>` : '');
    return;
  }
  const meta = m.backend ? `<div class="meta">${escapeHtml(m.backend)}</div>` : '';
  const err = m.status === 'error'
    ? `<div class="error">⚠ ${escapeHtml(m.error.message)}<button data-retry>Retry</button></div>` : '';
  el.innerHTML = `${meta}<div class="body">${md.render(m.text || '')}</div>${err}`;
  el.querySelectorAll('pre').forEach(pre => {
    const b = document.createElement('button'); b.className = 'copy'; b.textContent = 'Copy';
    b.onclick = () => navigator.clipboard.writeText(pre.innerText.replace(/Copy$/, ''));
    pre.appendChild(b);
  });
}

function render(msg) {
  const nearBottom = els.messages.scrollHeight - els.messages.scrollTop - els.messages.clientHeight < 40;
  if (msg.type === 'reset') { rendered.clear(); els.messages.innerHTML = ''; }
  for (const m of state.messages) if (!rendered.has(m.id) || m.id === msg.id) renderMessage(m);
  els.app.classList.toggle('has-messages', state.messages.length > 0);
  els.chips.innerHTML = state.attachments.map(a =>
    `<div class="chip" data-id="${a.id}">${a.thumb ? `<img src="${a.thumb}" alt="">` : '📄'}<span>${escapeHtml(a.name)}</span><button title="Remove" data-remove="${a.id}">×</button></div>`).join('');
  const sel = state.backends.find(b => b.id === state.selectedBackend);
  els.backend.textContent = sel ? sel.name : '';
  els.backend.hidden = state.backends.length === 0;
  els.send.classList.toggle('stop', state.busy);
  els.send.textContent = state.busy ? '■' : '↑';
  els.send.title = state.busy ? 'Stop' : 'Send (Enter)';
  if (nearBottom) els.messages.scrollTop = els.messages.scrollHeight;
  reportHeight();
}

let lastHeight = 0;
function reportHeight() {
  requestAnimationFrame(() => {
    // #messages stretches to fill the window, so measure its children, not its scrollHeight.
    const h = desiredHeight({
      messageHeights: [...els.messages.children].map(c => c.offsetHeight),
      gap: 10, listPadding: 14, composer: $('composer').offsetHeight, chrome: 30, // +8 slack: collapsed markdown margins aren't in offsetHeight
    });
    if (Math.abs(h - lastHeight) > 1) { lastHeight = h; post({ type: 'height', value: h }); }
  });
}

function showToast(text) {
  els.toast.textContent = text; els.toast.hidden = false;
  clearTimeout(showToast.t); showToast.t = setTimeout(() => (els.toast.hidden = true), 4000);
}

function autoGrow() {
  els.input.style.height = 'auto';
  const h = els.input.scrollHeight;
  els.input.style.height = h + 'px';
  els.input.style.overflowY = h > els.input.clientHeight + 1 ? 'auto' : 'hidden'; // no stray scroll arrows on one line
  reportHeight();
}

function send() {
  if (state.busy) { post({ type: 'cancel' }); return; }
  const text = els.input.value.trim();
  if (!text && state.attachments.length === 0) return;
  post({ type: 'send', text });
  els.input.value = ''; autoGrow();
}

function readFile(file, type) {
  const reader = new FileReader();
  reader.onload = () => post({ type, name: file.name || 'pasted.png', mime: file.type, base64: String(reader.result).split(',')[1] ?? '' });
  reader.readAsDataURL(file);
}

// --- events -------------------------------------------------------------------------------
els.input.addEventListener('input', autoGrow);
els.input.addEventListener('keydown', e => {
  if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); send(); }
});
els.send.addEventListener('click', send);
els.plus.addEventListener('click', () => { els.menu.hidden = !els.menu.hidden; });
els.menu.addEventListener('click', e => {
  const cmd = e.target.closest('[data-cmd]')?.dataset.cmd;
  if (cmd) { els.menu.hidden = true; post({ type: cmd }); }
});
els.chips.addEventListener('click', e => { const id = e.target.dataset.remove; if (id) post({ type: 'removeAttachment', id }); });
els.messages.addEventListener('click', e => {
  if (e.target.matches('[data-retry]')) { post({ type: 'retry' }); return; }
  const a = e.target.closest('a[href]');
  if (a) { e.preventDefault(); post({ type: 'openLink', url: a.href }); }
});
els.backend.addEventListener('click', () => {
  const avail = state.backends.filter(b => b.available);
  if (avail.length < 2) { showToast(state.backends.length > 1 ? 'More backends arrive in a later update.' : 'Only one AI is set up.'); return; }
  const i = avail.findIndex(b => b.id === state.selectedBackend);
  post({ type: 'selectBackend', id: avail[(i + 1) % avail.length].id });
});
document.addEventListener('keydown', e => {
  if (e.key === 'Escape') { e.preventDefault(); if (!els.menu.hidden) els.menu.hidden = true; else post({ type: 'escape' }); }
  else if (e.key.toLowerCase() === 'n' && e.ctrlKey) { e.preventDefault(); post({ type: 'newChat' }); }
});
document.addEventListener('paste', e => {
  const files = [...(e.clipboardData?.files ?? [])];
  if (files.length) { e.preventDefault(); files.forEach(f => readFile(f, 'pasteImage')); }
});
document.addEventListener('dragover', e => { e.preventDefault(); document.body.classList.add('dragging'); });
document.addEventListener('dragleave', e => { if (!e.relatedTarget) document.body.classList.remove('dragging'); });
document.addEventListener('drop', e => {
  e.preventDefault(); document.body.classList.remove('dragging');
  [...(e.dataTransfer?.files ?? [])].forEach(f => readFile(f, 'dropFile'));
});
new ResizeObserver(reportHeight).observe(els.messages);
new ResizeObserver(reportHeight).observe($('composer'));

host?.addEventListener('message', e => {
  const msg = e.data;
  if (msg.type === 'theme') { els.theme.textContent = msg.css; return; }
  if (msg.type === 'toast') { showToast(msg.message); return; }
  if (msg.type === 'focus') { els.input.focus(); return; }
  dispatch(msg);
});
post({ type: 'ready' });
els.input.focus();
