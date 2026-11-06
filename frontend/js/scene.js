// Scène 3D de la ligne de production (Three.js).
// Reçoit l'état envoyé par le backend et anime machines, convoyeur et produits.
import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';

export const STATION_X = [-18, -6, 6, 18];
const BELT_TOP = 1.0;      // hauteur du dessus du convoyeur
const Q_GAP = 0.8;         // espacement des produits en file
const UNIT_GAP = 2.6;      // écart entre machines parallèles
export const STATE_COLORS = { marche: 0x22c55e, attente: 0x94a3b8, bloque: 0xf59e0b, panne: 0xef4444 };
// couleur d'un produit selon le dernier poste franchi
const PRODUCT_COLORS = [0xb7793f, 0xb7793f, 0x4f7fe0, 0x2fb67c];
const CARTON = 0xc9a26b;

const mat = (color, o = {}) => new THREE.MeshStandardMaterial({ color, roughness: 0.6, metalness: 0.2, ...o });
const MATS = {
  frame: mat(0x3a4250, { metalness: 0.6, roughness: 0.4 }),
  body: mat(0xdfe4ea, { metalness: 0.3, roughness: 0.5 }),
  accent: mat(0x1b3a5c, { metalness: 0.4 }),  // bleu marine ENSAM
  dark: mat(0x1f2933, { roughness: 0.8 }),
  glass: new THREE.MeshStandardMaterial({ color: 0x9fd3ff, transparent: true, opacity: 0.25, roughness: 0.1 }),
  yellow: mat(0xf2c230),
};

function box(w, h, d, material, x = 0, y = 0, z = 0) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), material);
  m.position.set(x, y, z);
  m.castShadow = m.receiveShadow = true;
  return m;
}

function cyl(rt, rb, h, material, x = 0, y = 0, z = 0, seg = 20) {
  const m = new THREE.Mesh(new THREE.CylinderGeometry(rt, rb, h, seg), material);
  m.position.set(x, y, z);
  m.castShadow = true;
  return m;
}

function textSprite(lines, { width = 512, height = 160, bg = 'rgba(15,23,42,0.82)' } = {}) {
  const canvas = document.createElement('canvas');
  canvas.width = width; canvas.height = height;
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: tex, depthTest: false }));
  sprite.renderOrder = 10;
  sprite.scale.set(width / 100, height / 100, 1);
  sprite.userData.draw = (ls) => {
    const g = canvas.getContext('2d');
    g.clearRect(0, 0, width, height);
    g.fillStyle = bg;
    g.beginPath(); g.roundRect(4, 4, width - 8, height - 8, 22); g.fill();
    g.textAlign = 'center'; g.textBaseline = 'middle';
    ls.forEach((l, idx) => {
      g.font = idx === 0 ? '600 46px system-ui, sans-serif' : '400 36px system-ui, sans-serif';
      g.fillStyle = l.color || '#fff';
      g.fillText(l.text, width / 2, height / (ls.length + 1) * (idx + 1) + (idx === 0 ? 4 : 0));
    });
    tex.needsUpdate = true;
  };
  sprite.userData.draw(lines);
  return sprite;
}

// ---------------------------------------------------------------- machines
function buildUnit(kind) {
  const g = new THREE.Group();
  // tunnel : deux flancs + capot, le convoyeur passe au milieu
  g.add(box(2.4, 2.2, 0.25, MATS.body, 0, 1.1, 0.85));
  g.add(box(2.4, 2.2, 0.25, MATS.body, 0, 1.1, -0.85));
  g.add(box(2.6, 0.5, 2.0, MATS.accent, 0, 2.45, 0));
  g.add(box(2.42, 0.08, 0.27, MATS.yellow, 0, 0.3, 0.86));
  g.add(box(2.42, 0.08, 0.27, MATS.yellow, 0, 0.3, -0.86));
  const anim = {};

  if (kind === 0) { // alimentation : trémie
    const hopper = new THREE.Mesh(new THREE.CylinderGeometry(1.0, 0.35, 1.2, 4, 1, true), mat(0x7c8796, { side: THREE.DoubleSide, metalness: 0.5 }));
    hopper.rotation.y = Math.PI / 4; hopper.position.set(0, 3.3, 0); hopper.castShadow = true;
    g.add(hopper);
    for (let k = 0; k < 4; k++) g.add(box(0.35, 0.3, 0.35, mat(PRODUCT_COLORS[0]), -0.3 + (k % 2) * 0.5, 3.6 + Math.floor(k / 2) * 0.25, -0.2 + (k % 2) * 0.3));
  } else if (kind === 1) { // traitement : broche
    const spindle = new THREE.Group();
    spindle.add(cyl(0.18, 0.18, 0.8, MATS.frame, 0, 0, 0));
    spindle.add(cyl(0.05, 0.12, 0.35, mat(0xc0c7d0, { metalness: 0.9, roughness: 0.2 }), 0, -0.55, 0, 12));
    spindle.position.set(0, 1.85, 0);
    g.add(spindle);
    g.add(box(0.9, 0.6, 0.9, MATS.frame, 0, 3.0, 0));
    anim.spindle = spindle;
  } else if (kind === 2) { // contrôle qualité : caméra + faisceau
    g.add(box(0.7, 0.45, 0.6, MATS.dark, 0, 2.0, 0));
    g.add(cyl(0.16, 0.16, 0.2, MATS.glass, 0, 1.72, 0));
    const beam = new THREE.Mesh(new THREE.PlaneGeometry(0.12, 1.6), new THREE.MeshBasicMaterial({ color: 0x38bdf8, transparent: true, opacity: 0.55, side: THREE.DoubleSide }));
    beam.position.set(0, 1.25, 0); beam.rotation.y = Math.PI / 2;
    g.add(beam);
    g.add(box(2.6, 0.12, 0.12, mat(0x38bdf8, { emissive: 0x0ea5e9, emissiveIntensity: 0.6 }), 0, 2.75, 1.0));
    anim.beam = beam;
  } else { // emballage : cartonneuse
    g.add(box(1.4, 0.9, 1.4, mat(CARTON), 0, 3.15, 0));
    const press = box(1.2, 0.15, 1.0, MATS.frame, 0, 2.0, 0);
    g.add(press);
    anim.press = press;
  }

  // voyant d'état
  g.add(cyl(0.05, 0.05, 1.0, MATS.frame, 1.05, 3.2, 0.85, 8));
  const lampMat = new THREE.MeshStandardMaterial({ color: 0x94a3b8, emissive: 0x94a3b8, emissiveIntensity: 0.8 });
  const lamp = new THREE.Mesh(new THREE.SphereGeometry(0.2, 16, 12), lampMat);
  lamp.position.set(1.05, 3.8, 0.85);
  g.add(lamp);
  // barre de progression (face avant)
  g.add(box(2.0, 0.16, 0.04, MATS.dark, 0, 2.45, 1.02));
  const bar = box(2.0, 0.12, 0.05, mat(0x22c55e, { emissive: 0x22c55e, emissiveIntensity: 0.4 }), 0, 2.45, 1.03);
  bar.geometry.translate(1.0, 0, 0); bar.position.x = -1.0; bar.scale.x = 0.001;
  g.add(bar);
  // vitre de protection
  g.add(box(2.2, 1.0, 0.04, MATS.glass, 0, 1.6, 0.98));

  g.userData = { lampMat, bar, anim };
  return g;
}

// ------------------------------------------------------------------ scène
export class LineScene {
  constructor(container, { onSelect } = {}) {
    this.container = container;
    this.onSelect = onSelect;
    this.renderer = new THREE.WebGLRenderer({ antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    container.appendChild(this.renderer.domElement);

    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0xe8ecf1);
    this.scene.fog = new THREE.Fog(0xe8ecf1, 60, 120);
    this.camera = new THREE.PerspectiveCamera(45, 1, 0.1, 300);
    this.camera.position.set(4, 20, 32);
    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.target.set(2, 1, 0);
    this.controls.enableDamping = true;
    this.controls.maxPolarAngle = Math.PI * 0.48;
    this.controls.minDistance = 8; this.controls.maxDistance = 140;
    this.controls.addEventListener('start', () => { this._userMoved = true; });

    this._lights();
    this._floor();
    this._conveyor();
    this._exitArea();
    this._rejectBin();

    this.stations = STATION_X.map((x, i) => {
      const group = new THREE.Group();
      group.position.x = x;
      this.scene.add(group);
      const label = textSprite([{ text: '' }]);
      label.position.set(x, 5.6, 0);
      this.scene.add(label);
      return { group, units: [], branches: [], label, n: 0, state: 'attente', lastLabel: '' };
    });
    this.setServers([1, 1, 1, 1]);

    this.products = new Map();   // id -> { mesh, target, leaving }
    this.productGeo = new THREE.BoxGeometry(0.5, 0.42, 0.5);
    this.cartonGeo = new THREE.BoxGeometry(0.62, 0.5, 0.62);
    this.palletCount = 0;
    this.lastProduced = 0;
    this.state = null;
    this.clock = new THREE.Clock();
    this.time = 0;

    this.raycaster = new THREE.Raycaster();
    this.renderer.domElement.addEventListener('pointerdown', (e) => { this._down = [e.clientX, e.clientY]; });
    this.renderer.domElement.addEventListener('pointerup', (e) => this._click(e));
    new ResizeObserver(() => this._resize()).observe(container);
    this._resize();
    this.renderer.setAnimationLoop(() => this._frame());
  }

  _lights() {
    this.scene.add(new THREE.HemisphereLight(0xffffff, 0x8a96a8, 1.4));
    const sun = new THREE.DirectionalLight(0xffffff, 2.2);
    sun.position.set(12, 30, 18);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    Object.assign(sun.shadow.camera, { left: -32, right: 32, top: 18, bottom: -18, near: 1, far: 80 });
    sun.shadow.bias = -0.0005;
    this.scene.add(sun);
  }

  _floor() {
    const floor = new THREE.Mesh(new THREE.PlaneGeometry(140, 80), mat(0xcfd6de, { roughness: 0.95, metalness: 0 }));
    floor.rotation.x = -Math.PI / 2;
    floor.receiveShadow = true;
    this.scene.add(floor);
    const grid = new THREE.GridHelper(140, 70, 0xb4bec9, 0xc3ccd6);
    grid.position.y = 0.01;
    this.scene.add(grid);
    // marquage sécurité autour de la ligne
    const lineMat = new THREE.MeshBasicMaterial({ color: 0xf2c230 });
    for (const z of [-4.6, 4.6]) {
      const m = new THREE.Mesh(new THREE.PlaneGeometry(52, 0.18), lineMat);
      m.rotation.x = -Math.PI / 2; m.position.set(2, 0.02, z);
      this.scene.add(m);
    }
  }

  _conveyor() {
    const len = 46, x0 = -22, cx = x0 + len / 2;
    // bande animée (texture rayée)
    const c = document.createElement('canvas'); c.width = 64; c.height = 16;
    const g = c.getContext('2d');
    g.fillStyle = '#2b3139'; g.fillRect(0, 0, 64, 16);
    g.fillStyle = '#3a414b'; g.fillRect(0, 0, 8, 16);
    this.beltTex = new THREE.CanvasTexture(c);
    this.beltTex.wrapS = THREE.RepeatWrapping;
    this.beltTex.repeat.set(len * 1.5, 1);
    const belt = box(len, 0.12, 1.0, new THREE.MeshStandardMaterial({ map: this.beltTex, roughness: 0.9 }), cx, BELT_TOP - 0.06, 0);
    this.scene.add(belt);
    for (const z of [-0.58, 0.58]) this.scene.add(box(len, 0.22, 0.12, MATS.frame, cx, BELT_TOP - 0.05, z));
    for (let x = x0 + 1; x < x0 + len; x += 3) {
      for (const z of [-0.5, 0.5]) this.scene.add(box(0.12, BELT_TOP - 0.15, 0.12, MATS.frame, x, (BELT_TOP - 0.15) / 2, z));
    }
  }

  _exitArea() {
    const pallet = new THREE.Group();
    pallet.position.set(26.5, 0, 0);
    for (const z of [-0.9, 0, 0.9]) pallet.add(box(2.6, 0.15, 0.4, mat(0xa0784a), 0, 0.08, z));
    pallet.add(box(2.6, 0.08, 2.4, mat(0xb88b56), 0, 0.2, 0));
    this.scene.add(pallet);
    this.pallet = pallet;
    this.palletBoxes = [];
    const cm = mat(CARTON);
    for (let k = 0; k < 24; k++) {
      const b = box(0.62, 0.5, 0.62, cm);
      const lx = k % 4, lz = Math.floor(k / 4) % 3, ly = Math.floor(k / 12);
      b.position.set(-0.95 + lx * 0.64, 0.5 + ly * 0.52, -0.66 + lz * 0.66);
      b.visible = false;
      pallet.add(b);
      this.palletBoxes.push(b);
    }
    const lbl = textSprite([{ text: 'Produits finis' }], { width: 420, height: 110 });
    lbl.position.set(26.5, 3.4, 0); this.scene.add(lbl);
    this.exitLabel = lbl;
  }

  _rejectBin() {
    const bin = new THREE.Group();
    bin.position.set(STATION_X[2] + 2.4, 0, -3.4);
    const red = mat(0xd9483b);
    bin.add(box(1.6, 1.0, 0.1, red, 0, 0.5, 0.75));
    bin.add(box(1.6, 1.0, 0.1, red, 0, 0.5, -0.75));
    bin.add(box(0.1, 1.0, 1.5, red, 0.8, 0.5, 0));
    bin.add(box(0.1, 1.0, 1.5, red, -0.8, 0.5, 0));
    bin.add(box(1.6, 0.1, 1.5, red, 0, 0.05, 0));
    this.scene.add(bin);
    const lbl = textSprite([{ text: 'Rebuts' }], { width: 260, height: 100, bg: 'rgba(185,28,28,0.85)' });
    lbl.position.set(bin.position.x, 2.0, bin.position.z); this.scene.add(lbl);
    this.rejectLabel = lbl;
    this.binPos = bin.position.clone();
  }

  unitZ(j, n) { return (j - (n - 1) / 2) * UNIT_GAP; }

  setServers(servers) {
    servers.forEach((n, i) => {
      const st = this.stations[i];
      if (st.n === n) return;
      for (const u of [...st.units, ...st.branches]) st.group.remove(u);
      st.units = []; st.branches = [];
      for (let j = 0; j < n; j++) {
        const u = buildUnit(i);
        u.position.z = this.unitZ(j, n);
        u.userData.station = i;
        st.group.add(u);
        st.units.push(u);
        if (n > 1) { // aiguillage vers la machine parallèle
          const z = this.unitZ(j, n);
          const br = box(1.0, 0.1, Math.abs(z) + 0.5, MATS.dark, -1.8, BELT_TOP - 0.06, z / 2);
          st.group.add(br); st.branches.push(br);
        }
      }
      st.n = n;
    });
  }

  // ------------------------------------------------------------ état reçu
  update(state) {
    this.state = state;
    this.setServers(state.config.servers);
    const stations = state.stations;

    stations.forEach((s, i) => {
      const st = this.stations[i];
      st.state = s.state;
      const color = STATE_COLORS[s.state];
      st.units.forEach((u, j) => {
        const sv = s.servers[j] || { progress: 0, busy: false };
        u.userData.lampMat.color.setHex(color);
        u.userData.lampMat.emissive.setHex(color);
        u.userData.bar.scale.x = Math.max(0.001, sv.busy ? sv.progress : 0);
        u.userData.bar.material.color.setHex(sv.done ? STATE_COLORS.bloque : STATE_COLORS.marche);
        u.userData.busy = sv.busy && !sv.done && s.state !== 'panne';
      });
      const stateTxt = { marche: 'En marche', attente: 'En attente', bloque: 'Bloqué', panne: 'EN PANNE' }[s.state];
      const key = `${s.name}|${stateTxt}|${s.queue}|${s.time}`;
      if (key !== st.lastLabel) {
        st.lastLabel = key;
        st.label.userData.draw([
          { text: s.name },
          { text: `${stateTxt} · ${s.time}s · file ${s.queue}`, color: '#' + color.toString(16).padStart(6, '0') },
        ]);
      }
    });

    // produits
    const seen = new Set();
    for (const p of state.products) {
      seen.add(p.id);
      let item = this.products.get(p.id);
      const target = this._target(p, stations);
      if (!item) {
        const mesh = new THREE.Mesh(this.productGeo, mat(PRODUCT_COLORS[p.st]));
        mesh.castShadow = true;
        mesh.position.copy(target);
        if (p.st === 0 && p.s === 'p') mesh.position.y += 1.6; // sort de la trémie
        this.scene.add(mesh);
        item = { mesh, target };
        this.products.set(p.id, item);
      }
      item.target = target;
      item.mesh.material.color.setHex(p.rej && p.p >= 1 ? 0xdc2626 : PRODUCT_COLORS[p.st]);
    }
    const exits = new Map(state.exits.map(([id, kind]) => [id, kind]));
    for (const [id, item] of this.products) {
      if (seen.has(id) || item.leaving) continue;
      const kind = exits.get(id);
      if (kind === 'ok') {
        item.leaving = 'ok';
        item.mesh.geometry = this.cartonGeo;
        item.mesh.material.color.setHex(CARTON);
        item.target = new THREE.Vector3(24.5, BELT_TOP + 0.25, 0);
      } else if (kind === 'rej') {
        item.leaving = 'rej';
        item.mesh.material.color.setHex(0xdc2626);
        item.target = this.binPos.clone().setY(0.4);
        item.arc = 0;
      } else {
        this._remove(id);
      }
    }

    // palette et compteurs
    const k = state.kpi;
    if (k.produced < this.lastProduced) this.palletCount = 0;
    this.lastProduced = k.produced;
    this.palletBoxes.forEach((b, idx) => { b.visible = idx < this.palletCount; });
    const ek = `${k.produced}`;
    if (this.exitLabel.userData.key !== ek) {
      this.exitLabel.userData.key = ek;
      this.exitLabel.userData.draw([{ text: `Produits finis : ${k.produced}` }]);
    }
    const rk = `${k.rejected}`;
    if (this.rejectLabel.userData.key !== rk) {
      this.rejectLabel.userData.key = rk;
      this.rejectLabel.userData.draw([{ text: `Rebuts : ${k.rejected}` }]);
    }
    this.speed = state.paused ? 0 : state.speed;
    this.beltMoving = state.products.some((p) => p.s === 't');
  }

  _target(p, stations) {
    const x0 = STATION_X[p.st];
    const y = BELT_TOP + 0.21;
    if (p.s === 'q') return new THREE.Vector3(x0 - 2.2 - p.k * Q_GAP, y, 0);
    if (p.s === 'p') {
      const n = this.stations[p.st].n;
      return new THREE.Vector3(x0 - 0.9 + 1.8 * p.p, y, this.unitZ(p.k, n));
    }
    const from = STATION_X[p.st - 1] + 1.6;
    const to = x0 - 2.2 - stations[p.st].queue * Q_GAP;
    return new THREE.Vector3(from + (to - from) * p.p, y, 0);
  }

  _remove(id) {
    const item = this.products.get(id);
    if (!item) return;
    this.scene.remove(item.mesh);
    item.mesh.material.dispose();
    this.products.delete(id);
  }

  // ----------------------------------------------------------- animation
  _frame() {
    const dt = Math.min(this.clock.getDelta(), 0.1);
    this.time += dt;
    const k = 1 - Math.exp(-dt * 10);
    for (const [id, item] of this.products) {
      const m = item.mesh;
      if (item.leaving === 'rej') {
        item.arc = Math.min(1, item.arc + dt * 1.6);
        m.position.lerp(item.target, k);
        m.position.y = item.target.y + Math.sin(item.arc * Math.PI) * 1.2 + (1 - item.arc) * 0.6;
        m.rotation.x += dt * 6;
        if (item.arc >= 1) this._remove(id);
      } else if (item.leaving === 'ok') {
        m.position.lerp(item.target, k * 0.6);
        if (m.position.distanceTo(item.target) < 0.15) {
          this.palletCount = this.palletCount >= 24 ? 1 : this.palletCount + 1;
          this.palletBoxes.forEach((b, idx) => { b.visible = idx < this.palletCount; });
          this._remove(id);
        }
      } else {
        m.position.lerp(item.target, k);
      }
    }
    if (this.beltMoving && this.speed) this.beltTex.offset.x -= dt * this.speed * 0.6;

    const blink = Math.sin(this.time * 10) > 0;
    this.stations.forEach((st) => {
      st.units.forEach((u) => {
        const { lampMat, anim } = u.userData;
        lampMat.emissiveIntensity = st.state === 'panne' ? (blink ? 1.6 : 0.1) : 0.8;
        const active = u.userData.busy && this.speed;
        if (anim.spindle) {
          if (active) anim.spindle.rotation.y += dt * 12;
          anim.spindle.position.y = 1.85 + (active ? Math.sin(this.time * 3) * 0.12 : 0.2);
        }
        if (anim.beam) {
          anim.beam.visible = !!active;
          anim.beam.position.x = Math.sin(this.time * 4) * 0.8;
        }
        if (anim.press) anim.press.position.y = active ? 1.85 + Math.abs(Math.sin(this.time * 4)) * 0.3 : 2.1;
      });
    });

    this.controls.update();
    this.renderer.render(this.scene, this.camera);
  }

  _resize() {
    const w = this.container.clientWidth, h = this.container.clientHeight;
    if (!w || !h) return;
    this.renderer.setSize(w, h);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
    if (!this._userMoved) this.resetView();
  }

  _click(e) {
    if (!this._down || Math.hypot(e.clientX - this._down[0], e.clientY - this._down[1]) > 5) return;
    const r = this.renderer.domElement.getBoundingClientRect();
    const v = new THREE.Vector2(((e.clientX - r.left) / r.width) * 2 - 1, -((e.clientY - r.top) / r.height) * 2 + 1);
    this.raycaster.setFromCamera(v, this.camera);
    const hits = this.raycaster.intersectObjects(this.stations.flatMap((s) => s.units), true);
    if (!hits.length) return;
    let o = hits[0].object;
    while (o && o.userData.station === undefined) o = o.parent;
    if (o && this.onSelect) this.onSelect(o.userData.station);
  }

  focus(i) {
    this._userMoved = true;
    const x = STATION_X[i];
    this.controls.target.set(x, 1.5, 0);
    this.camera.position.set(x + 4, 9, 13);
  }

  resetView() {
    // recule la caméra pour que toute la ligne (≈ 52 unités) tienne dans la largeur
    const vfov = THREE.MathUtils.degToRad(this.camera.fov);
    const hfov = 2 * Math.atan(Math.tan(vfov / 2) * this.camera.aspect);
    const d = Math.max(30, 27 / Math.tan(hfov / 2), 14 / Math.tan(vfov / 2));
    const dir = new THREE.Vector3(0.1, 0.55, 0.83).normalize();
    this.controls.target.set(4, 1, 0);
    this.camera.position.copy(dir.multiplyScalar(d)).add(this.controls.target);
  }
}
