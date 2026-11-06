// Dashboard + liaison WebSocket avec le backend Python.
import { LineScene, STATE_COLORS } from './scene.js';

const $ = (id) => document.getElementById(id);
const STATE_TXT = { marche: 'Marche', attente: 'Attente', bloque: 'Bloqué', panne: 'Panne' };
const pct = (v) => `${Math.round(v * 100)} %`;
const fmt = (v, d = 1) => Number(v).toLocaleString('fr-FR', { maximumFractionDigits: d, minimumFractionDigits: d });

let ws = null;
let state = null;
let selected = 2;
let compareRows = null;
let editing = null; // slider en cours de manipulation

const scene = new LineScene($('viewport'), { onSelect: (i) => select(i, true) });

// ------------------------------------------------------------- WebSocket
function connect() {
  const proto = location.protocol === 'https:' ? 'wss' : 'ws';
  ws = new WebSocket(`${proto}://${location.host}/ws`);
  ws.onopen = () => { $('conn').textContent = 'Connecté'; $('conn').className = 'conn on'; };
  ws.onclose = () => {
    $('conn').textContent = 'Déconnecté'; $('conn').className = 'conn off';
    setTimeout(connect, 1500);
  };
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.type === 'hello') onHello(m);
    else if (m.type === 'state') onState(m);
    else if (m.type === 'compare') onCompare(m);
    else if (m.type === 'error') console.warn('Backend :', m.message);
  };
}
const send = (cmd, extra = {}) => ws && ws.readyState === 1 && ws.send(JSON.stringify({ cmd, ...extra }));

function onHello(m) {
  $('presets').innerHTML = Object.entries(m.presets)
    .map(([k, label]) => `<button data-k="${k}">${label}</button>`).join('');
  $('sel-station').innerHTML = m.stations.map((s, i) => `<option value="${i}">${s.name}</option>`).join('');
  $('sel-station').value = selected;
}

// ---------------------------------------------------------------- rendu
function onState(m) {
  state = m;
  scene.update(m);
  const k = m.kpi;
  $('clock').textContent = `t = ${formatTime(k.t)}`;
  $('pause').textContent = m.paused ? '▶ Reprendre' : '⏸ Pause';
  setSeg('speed', m.speed);

  $('k-thr').textContent = fmt(k.throughput_now);
  $('k-theo').textContent = fmt(k.theoretical);
  $('k-theo-h').textContent = `${k.theoretical_hour} produits/h max`;
  $('k-prod').textContent = k.produced;
  $('k-avg').textContent = `moyenne ${fmt(k.throughput_avg)} /min`;
  $('k-rej').textContent = k.rejected;
  $('k-rej-pct').textContent = `${fmt(k.reject_pct)} % des sorties`;
  $('k-wip').textContent = k.wip;
  $('k-lead').textContent = `séjour moyen ${fmt(k.lead_time, 0)} s`;
  $('k-bn').textContent = k.bottleneck_observed;
  $('k-bn-t').textContent = `attente au contrôle : ${fmt(k.wait_controle, 0)} s`;

  $('st-body').innerHTML = m.stations.map((s, i) => `
    <tr data-i="${i}" class="${i === selected ? 'sel' : ''}">
      <td>${s.name}${s.servers.length > 1 ? ` ×${s.servers.length}` : ''}</td>
      <td><span class="pill ${s.state}">${STATE_TXT[s.state]}</span></td>
      <td>${s.queue}</td><td>${pct(s.util)}</td><td>${pct(s.avail)}</td><td>${fmt(s.wait, 0)} s</td>
    </tr>`).join('');

  const c = m.config;
  syncSlider('t-ctrl', c.times[2]);
  syncSlider('t-trait', c.times[1]);
  setSeg('srv-ctrl', c.servers[2]);
  $('adaptive').checked = c.adaptive;
  $('failures').checked = c.failures;
  document.querySelectorAll('#presets button').forEach((b) => b.classList.toggle('on', b.dataset.k === m.preset));

  const s = m.stations[selected];
  $('sel-info').textContent = s.state === 'panne'
    ? `En panne – réparation dans ${fmt(s.repair_in, 0)} s simulées.`
    : `${STATE_TXT[s.state]} · temps de cycle ${s.time} s · arrêts cumulés ${fmt(s.downtime, 0)} s · bloqué ${pct(s.blocked)} du temps`;

  $('events').innerHTML = m.events.map((e) =>
    `<li class="${e.kind}"><time>${formatTime(e.t)}</time>${e.text}</li>`).join('');

  drawChart(m.history, k.theoretical);
}

function formatTime(t) {
  const s = Math.floor(t), h = Math.floor(s / 3600), mn = Math.floor((s % 3600) / 60), sec = s % 60;
  return h ? `${h} h ${String(mn).padStart(2, '0')} min` : `${mn} min ${String(sec).padStart(2, '0')} s`;
}

function setSeg(id, v) {
  document.querySelectorAll(`#${id} button`).forEach((b) => b.classList.toggle('on', Number(b.dataset.v) === Number(v)));
}

function syncSlider(id, v) {
  if (editing !== id) $(id).value = v;
  $(`${id}-v`).textContent = `${editing === id ? $(id).value : v} s`;
}

function select(i, focus) {
  selected = i;
  $('sel-station').value = i;
  if (focus) scene.focus(i);
}

// ---------------------------------------------------------------- graphe
function drawChart(history, theo) {
  const cv = $('chart');
  const dpr = window.devicePixelRatio || 1;
  const w = cv.clientWidth, h = 150;
  if (cv.width !== w * dpr) { cv.width = w * dpr; cv.height = h * dpr; }
  const g = cv.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, w, h);
  const pad = { l: 28, r: 6, t: 8, b: 18 };
  const maxY = Math.max(2, Math.ceil(Math.max(theo, ...history.map((p) => p[1])) + 1));
  const pw = w - pad.l - pad.r, ph = h - pad.t - pad.b;
  const n = 180;
  const t1 = history.length ? history[history.length - 1][0] : 0;
  const X = (t) => pad.l + pw * (1 - (t1 - t) / n);
  const Y = (v) => pad.t + ph * (1 - v / maxY);

  g.font = '10px system-ui'; g.fillStyle = '#64748b'; g.strokeStyle = '#e2e8f0'; g.lineWidth = 1;
  for (let v = 0; v <= maxY; v += maxY > 6 ? 2 : 1) {
    g.beginPath(); g.moveTo(pad.l, Y(v)); g.lineTo(w - pad.r, Y(v)); g.stroke();
    g.fillText(v, 6, Y(v) + 3);
  }
  g.fillText('-3 min', pad.l, h - 4);
  g.fillText('maintenant', w - pad.r - 52, h - 4);

  g.setLineDash([5, 4]); g.strokeStyle = '#f59e0b'; g.lineWidth = 1.5;
  g.beginPath(); g.moveTo(pad.l, Y(theo)); g.lineTo(w - pad.r, Y(theo)); g.stroke();
  g.setLineDash([]);

  if (history.length > 1) {
    g.strokeStyle = '#2f6690'; g.lineWidth = 2;
    g.beginPath();
    history.forEach(([t, v], i) => (i ? g.lineTo(X(t), Y(v)) : g.moveTo(X(t), Y(v))));
    g.stroke();
  }
}

// ----------------------------------------------------------- comparaison
function onCompare(m) {
  if (m.status === 'running') {
    $('compare').disabled = true;
    $('compare').textContent = 'Simulation en cours…';
    return;
  }
  $('compare').disabled = false;
  $('compare').textContent = 'Relancer la comparaison';
  compareRows = m.results;
  $('csv').disabled = false;
  const base = m.results[0];
  const cols = [
    ['Produits / h', (r) => r.produced, 'max', 0],
    ['Cadence moy. (/min)', (r) => r.throughput_avg, 'max', 2],
    ['Attente contrôle (s)', (r) => r.wait_controle, 'min', 0],
    ['En-cours moyen', (r) => r.avg_wip, 'min', 1],
    ['Séjour moyen (s)', (r) => r.lead_time, 'min', 0],
    ['Goulot', (r) => r.bottleneck_observed, null],
  ];
  const head = `<tr><th>Indicateur</th>${m.results.map((r) => `<th>${r.label}</th>`).join('')}</tr>`;
  const body = cols.map(([name, get, best, d]) => {
    const vals = m.results.map(get);
    const target = best === 'max' ? Math.max(...vals) : best === 'min' ? Math.min(...vals) : null;
    return `<tr><td>${name}</td>${vals.map((v, i) => {
      if (best === null) return `<td>${v}</td>`;
      const gain = i > 0 && get(base) ? Math.round((v / get(base) - 1) * 100) : null;
      return `<td class="${v === target ? 'best' : ''}">${fmt(v, d)}${gain ? `<div class="gain">${gain > 0 ? '+' : ''}${gain} %</div>` : ''}</td>`;
    }).join('')}</tr>`;
  }).join('');
  $('cmp-out').innerHTML = `<div style="overflow-x:auto"><table>${head}${body}</table></div>
    <p class="muted">Durée simulée : ${m.duration / 3600} h · pannes ${state?.config.failures ? 'activées' : 'désactivées'} · % = écart vs situation initiale.</p>`;
}

function exportCsv() {
  if (!compareRows) return;
  const keys = ['label', 'produced', 'rejected', 'throughput_avg', 'theoretical', 'wait_controle', 'avg_wip', 'lead_time', 'downtime_total', 'failures', 'bottleneck_observed'];
  const lines = [keys.join(';'), ...compareRows.map((r) => keys.map((k) => String(r[k]).replace('.', ',')).join(';'))];
  const blob = new Blob(['﻿' + lines.join('\n')], { type: 'text/csv;charset=utf-8' });
  const a = Object.assign(document.createElement('a'), { href: URL.createObjectURL(blob), download: 'comparaison_scenarios.csv' });
  a.click();
  URL.revokeObjectURL(a.href);
}

// ------------------------------------------------------------ contrôles
$('pause').onclick = () => send('pause', { value: !state?.paused });
$('reset').onclick = () => send('reset');
$('view').onclick = () => { scene._userMoved = false; scene.resetView(); };
$('speed').onclick = (e) => e.target.dataset.v && send('speed', { value: Number(e.target.dataset.v) });
$('presets').onclick = (e) => e.target.dataset.k && send('preset', { name: e.target.dataset.k });
$('srv-ctrl').onclick = (e) => e.target.dataset.v && send('set_servers', { station: 2, value: Number(e.target.dataset.v) });
$('adaptive').onchange = (e) => send('adaptive', { value: e.target.checked });
$('failures').onchange = (e) => send('failures', { value: e.target.checked });
for (const [id, station] of [['t-ctrl', 2], ['t-trait', 1]]) {
  const el = $(id);
  el.oninput = () => { editing = id; $(`${id}-v`).textContent = `${el.value} s`; };
  el.onchange = () => { send('set_time', { station, value: Number(el.value) }); editing = null; };
}
$('sel-station').onchange = (e) => select(Number(e.target.value), true);
$('st-body').onclick = (e) => { const tr = e.target.closest('tr'); if (tr) select(Number(tr.dataset.i), true); };
$('break').onclick = () => send('break', { station: selected });
$('repair').onclick = () => send('repair', { station: selected });
$('compare').onclick = () => send('compare', { duration: 3600 });
$('csv').onclick = exportCsv;

connect();
