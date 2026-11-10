// Dashboard + liaison WebSocket avec le backend Python.
// La scène 3D est un build Unity WebGL qui lit le même flux WebSocket que ce tableau de bord.

const $ = (id) => document.getElementById(id);
const STATE_TXT = { marche: 'Marche', attente: 'Attente', bloque: 'Bloqué', panne: 'Panne' };
const pct = (v) => `${Math.round(v * 100)} %`;
const fmt = (v, d = 1) => Number(v).toLocaleString('fr-FR', { maximumFractionDigits: d, minimumFractionDigits: d });

let ws = null;
let state = null;
let selected = 2;
let compareRows = null;
let editing = null; // slider en cours de manipulation

// ------------------------------------------------------------- scène Unity
let unity = null;
const scene = {
  focus: (i) => unity && unity.SendMessage('LineBridge', 'FocusStation', String(i)),
  resetView: () => unity && unity.SendMessage('LineBridge', 'ResetView', ''),
};
// appelé par Unity (WsBridge.jslib) quand on clique sur une machine
window.twinOnStationSelected = (i) => select(i, false);

function loadUnity() {
  const base = 'unity/Build/unity';
  const loading = $('unity-loading');
  const script = document.createElement('script');
  script.src = `${base}.loader.js`;
  script.onload = () => {
    createUnityInstance($('unity-canvas'), {
      // l'extension .unityweb est celle que Unity 6 utilise quand « Decompression Fallback » est activé
      dataUrl: `${base}.data.unityweb`,
      frameworkUrl: `${base}.framework.js.unityweb`,
      codeUrl: `${base}.wasm.unityweb`,
      streamingAssetsUrl: 'StreamingAssets',
      companyName: 'ENSAM Rabat',
      productName: 'JumeauNumerique',
      productVersion: '1.0',
      matchWebGLToCanvasSize: true,
      devicePixelRatio: Math.min(window.devicePixelRatio || 1, 2),
    }, (p) => { loading.textContent = `Chargement de la scène 3D… ${Math.round(p * 100)} %`; })
      .then((instance) => { unity = instance; loading.hidden = true; })
      .catch((err) => { loading.textContent = `Scène 3D indisponible : ${err}`; });
  };
  script.onerror = () => {
    loading.textContent = 'Build Unity introuvable. Dans Unity : menu Twin > Construire WebGL (voir README).';
  };
  document.body.appendChild(script);
}

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
    : `${STATE_TXT[s.state]} · temps de cycle ${s.time} s`;
  $('sel-stats').innerHTML = [
    ['Taux d\'utilisation', pct(s.util)],
    ['Disponibilité', pct(s.avail)],
    ['Temps bloqué', pct(s.blocked)],
    ['Nombre de pannes', s.failures],
    ['MTBF observé', s.mtbf_obs ? `${fmt(s.mtbf_obs, 0)} s` : '–'],
    ['MTTR observé', s.mttr_obs ? `${fmt(s.mttr_obs, 1)} s` : '–'],
    ['Arrêts cumulés', `${fmt(s.downtime, 0)} s`],
  ].map(([a, b]) => `<tr><td>${a}</td><td>${b}</td></tr>`).join('');

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
  if (cv.width !== w * dpr) { cv.width = w * dpr; cv.height = h * dpr; cv.style.height = `${h}px`; }
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
    g.strokeStyle = '#1B3A5C'; g.lineWidth = 2;
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
$('view').onclick = () => scene.resetView();
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

$('team-btn').onclick = () => $('team').showModal();

// --------------------------------------------------------------- onglets
let tab = 'live';
$('tabs').onclick = (e) => {
  const t = e.target.dataset.tab;
  if (!t) return;
  tab = t;
  document.querySelectorAll('#tabs button').forEach((b) => b.classList.toggle('on', b.dataset.tab === t));
  document.querySelectorAll('.tab').forEach((el) => { el.hidden = el.id !== `tab-${t}`; });
  if (t === 'history') loadRuns();
};

// ------------------------------------------------------------ historique
async function loadRuns() {
  const runs = await (await fetch('/api/runs')).json();
  const sel = $('run-select');
  const keep = sel.value;
  sel.innerHTML = runs.map((r) => {
    const d = new Date(r.started_at).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
    const live = state && r.id === state.run_id ? ' (en cours)' : '';
    return `<option value="${r.id}">#${r.id} · ${r.label} · ${d} · ${r.samples} min${live}</option>`;
  }).join('');
  if (keep && runs.some((r) => String(r.id) === keep)) sel.value = keep;
  if (runs.length) loadHistory(Number(sel.value));
}

async function loadHistory(id) {
  $('run-csv').href = `/api/export.csv?run=${id}`;
  const h = await (await fetch(`/api/history?run=${id}`)).json();
  const sm = h.samples;
  const last = sm[sm.length - 1];
  const failures = h.events.filter((e) => e.kind === 'panne').length;
  const avgThr = sm.length ? sm.reduce((a, s) => a + s.throughput, 0) / sm.length : 0;
  const maxWip = sm.length ? Math.max(...sm.map((s) => s.wip)) : 0;
  $('h-kpis').innerHTML = [
    ['Durée enregistrée', `${sm.length} min`, 'relevés SQLite'],
    ['Produits finis', last ? last.produced : 0, last ? `${last.rejected} rejets` : '–'],
    ['Cadence moyenne', fmt(last ? last.throughput_avg : 0, 2), 'produits/min'],
    ['Débit moyen (relevés)', fmt(avgThr, 2), 'produits/min'],
    ['En-cours max', maxWip, 'produits'],
    ['Pannes', failures, last ? `goulot : ${last.bottleneck}` : '–'],
  ].map(([a, b, c]) => `<div class="kpi"><span>${a}</span><b>${b}</b><small>${c}</small></div>`).join('');

  const names = ['Alimentation', 'Traitement', 'Contrôle qualité', 'Emballage'];
  const util = last ? last.util : [0, 0, 0, 0];
  const top = util.indexOf(Math.max(...util));
  $('h-util').innerHTML = names.map((n, i) => `
    <div class="bar ${i === top ? 'top' : ''}"><span>${n}</span>
      <div class="track"><div class="fill" style="width:${Math.round(util[i] * 100)}%"></div></div>
      <b>${pct(util[i])}</b></div>`).join('');
  $('h-events').innerHTML = h.events.length
    ? h.events.slice().reverse().map((e) => `<li class="${e.kind}"><time>${formatTime(e.t)}</time>${e.text}</li>`).join('')
    : '<li class="muted">Aucun événement.</li>';
  drawHistory(sm);
}

function drawHistory(sm) {
  const cv = $('h-chart');
  const dpr = window.devicePixelRatio || 1;
  const w = cv.clientWidth, h = 170;
  cv.width = w * dpr; cv.height = h * dpr; cv.style.height = `${h}px`;
  const g = cv.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.clearRect(0, 0, w, h);
  g.font = '10px system-ui'; g.fillStyle = '#64748b';
  if (sm.length < 2) { g.fillText('Pas encore assez de relevés (1 par minute simulée).', 10, h / 2); return; }
  const pad = { l: 28, r: 28, t: 8, b: 18 };
  const pw = w - pad.l - pad.r, ph = h - pad.t - pad.b;
  const t0 = sm[0].t, t1 = sm[sm.length - 1].t;
  const maxThr = Math.max(2, Math.ceil(Math.max(...sm.map((s) => s.throughput)) + 1));
  const maxWip = Math.max(5, Math.ceil(Math.max(...sm.map((s) => s.wip)) * 1.1));
  const X = (t) => pad.l + pw * (t - t0) / (t1 - t0 || 1);
  g.strokeStyle = '#e2e8f0';
  for (let k = 0; k <= 4; k++) {
    const y = pad.t + ph * k / 4;
    g.beginPath(); g.moveTo(pad.l, y); g.lineTo(w - pad.r, y); g.stroke();
    g.fillText(fmt(maxThr * (1 - k / 4), 0), 4, y + 3);
    g.fillText(Math.round(maxWip * (1 - k / 4)), w - pad.r + 4, y + 3);
  }
  g.fillText(formatTime(t0), pad.l, h - 4);
  g.fillText(formatTime(t1), w - pad.r - 60, h - 4);
  const line = (get, max, color) => {
    g.strokeStyle = color; g.lineWidth = 2; g.beginPath();
    sm.forEach((s, i) => { const x = X(s.t), y = pad.t + ph * (1 - get(s) / max); i ? g.lineTo(x, y) : g.moveTo(x, y); });
    g.stroke();
  };
  line((s) => s.wip, maxWip, '#B22A2E');
  line((s) => s.throughput, maxThr, '#1B3A5C');
}

$('run-select').onchange = (e) => loadHistory(Number(e.target.value));
$('run-refresh').onclick = loadRuns;

connect();
loadUnity();
