using System.Collections.Generic;
using Twin.Net;
using UnityEngine;

namespace Twin.Scene
{
    /// <summary>
    /// Anime la ligne a partir du flux du backend. Rien n'est calcule ici : l'etat vient du serveur,
    /// les positions sont interpolees entre deux messages consecutifs (10 Hz -> cadence de l'ecran).
    /// Les produits sont recycles (pas d'allocation par image).
    /// </summary>
    public sealed class LineController : MonoBehaviour
    {
        [Header("References (posees par Twin > Construire la scene)")]
        public TwinClient client;
        public LineMaterials materials;
        public Transform[] stationRoots = new Transform[4];
        public WorldLabel[] stationLabels = new WorldLabel[4];
        public WorldLabel exitLabel;
        public WorldLabel rejectLabel;
        public Renderer belt;
        public Transform[] palletBoxes;
        public Transform productsRoot;

        sealed class ProductView
        {
            public Transform tr;
            public Renderer renderer;
            public int lastStation;
            public Vector3 lastPos;
            public bool leavingOk, leavingRej;
            public Vector3 leaveTarget;
            public float arc;
        }

        readonly List<StationUnit>[] _units = { new List<StationUnit>(), new List<StationUnit>(), new List<StationUnit>(), new List<StationUnit>() };
        readonly List<GameObject>[] _branches = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };
        readonly int[] _unitCounts = { 0, 0, 0, 0 };

        readonly Dictionary<int, ProductView> _views = new Dictionary<int, ProductView>(64);
        readonly Stack<ProductView> _pool = new Stack<ProductView>(64);
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly List<int> _toRemove = new List<int>(16);
        readonly Dictionary<int, ProductDto> _previousMap = new Dictionary<int, ProductDto>(64);
        StateMsg _previousMapSource;

        StateMsg _applied;
        float _lastTime = -1f;
        float _time;
        int _palletCount;
        int _lastProduced;
        bool _beltMoving;
        float _speed = 1f;
        Material _beltInstance;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        void Awake()
        {
            LineBuilder.Shared = materials;   // formes sans collider pendant l'execution
            if (belt != null) _beltInstance = belt.material;
            SetServers(new[] { 1, 1, 1, 1 });
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _time += dt;

            if (client != null && client.TryGetSample(out var from, out var to, out float alpha))
            {
                if (!ReferenceEquals(to, _applied)) ApplyState(to);
                UpdateProducts(from, to, alpha, dt);
            }
            else
            {
                UpdateLeaving(dt);
            }

            AnimateMachines(dt);
            if (_beltInstance != null && _beltMoving && _speed > 0f)
            {
                var off = _beltInstance.GetTextureOffset(BaseMap);
                off.x -= dt * _speed * 0.6f;
                _beltInstance.SetTextureOffset(BaseMap, off);
            }
        }

        // ------------------------------------------------------------------ nouvel etat
        void ApplyState(StateMsg s)
        {
            // remise a zero du backend (scenario, reset) : on repart de zero
            if (_applied != null && s.kpi != null && _applied.kpi != null && s.kpi.t < _applied.kpi.t - 0.5f)
                ClearProducts();
            _applied = s;

            if (s.config != null && s.config.servers != null) SetServers(s.config.servers);
            _speed = s.paused ? 0f : s.speed;

            if (s.stations != null)
            {
                for (int i = 0; i < s.stations.Length && i < 4; i++)
                {
                    var st = s.stations[i];
                    var color = LineLayout.StateColor(st.state);
                    if (stationLabels[i] != null)
                        stationLabels[i].Set(st.name, LineLayout.StateText(st.state) + " · " + st.time.ToString("0.#") + "s · file " + st.queue, color);
                    var list = _units[i];
                    for (int j = 0; j < list.Count; j++)
                    {
                        var sv = st.servers != null && j < st.servers.Length ? st.servers[j] : null;
                        list[j].Apply(st.state, sv != null ? sv.progress : 0f, sv != null && sv.busy, sv != null && sv.done);
                    }
                }
            }

            if (s.kpi != null)
            {
                if (s.kpi.produced < _lastProduced) { _palletCount = 0; RefreshPallet(); }
                _lastProduced = s.kpi.produced;
                if (exitLabel != null) exitLabel.Set("Produits finis : " + s.kpi.produced, "", Color.white);
                if (rejectLabel != null) rejectLabel.Set("Rebuts : " + s.kpi.rejected, "", Color.white);
            }

            _beltMoving = false;
            if (s.products != null)
                for (int i = 0; i < s.products.Length; i++)
                    if (s.products[i].s == "t") { _beltMoving = true; break; }
        }

        // ------------------------------------------------------------------ machines
        void SetServers(int[] counts)
        {
            for (int i = 0; i < 4 && i < counts.Length; i++)
            {
                int n = Mathf.Clamp(counts[i], 1, 3);
                if (_unitCounts[i] == n) continue;
                foreach (var u in _units[i]) if (u != null) Destroy(u.gameObject);
                foreach (var b in _branches[i]) if (b != null) Destroy(b);
                _units[i].Clear(); _branches[i].Clear();
                for (int j = 0; j < n; j++)
                {
                    var unit = LineBuilder.BuildUnit(i, i, materials, stationRoots[i]);
                    float z = LineLayout.UnitZ(j, n);
                    unit.transform.localPosition = new Vector3(0, 0, z);
                    _units[i].Add(unit);
                    if (n > 1)
                    {
                        // aiguillage vers la machine parallele
                        var br = LineBuilder.Box(stationRoots[i], "Aiguillage", 1.0f, 0.1f, Mathf.Abs(z) + 0.5f, materials.dark, -1.8f, LineLayout.BeltTop - 0.06f, z / 2f);
                        _branches[i].Add(br);
                    }
                }
                _unitCounts[i] = n;
            }
        }

        void AnimateMachines(float dt)
        {
            bool running = _speed > 0f;
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < _units[i].Count; j++)
                    _units[i][j].Animate(_time, dt, running);
        }

        // ------------------------------------------------------------------ produits
        void UpdateProducts(StateMsg from, StateMsg to, float alpha, float dt)
        {
            if (to.products == null) { UpdateLeaving(dt); return; }

            // index des produits de l'etat precedent, reconstruit seulement quand il change
            if (!ReferenceEquals(from, _previousMapSource))
            {
                _previousMapSource = from;
                _previousMap.Clear();
                if (from != null && from.products != null)
                    for (int i = 0; i < from.products.Length; i++) _previousMap[from.products[i].id] = from.products[i];
            }

            _seen.Clear();
            for (int i = 0; i < to.products.Length; i++)
            {
                var p = to.products[i];
                _seen.Add(p.id);
                if (!_views.TryGetValue(p.id, out var view) || view.leavingOk || view.leavingRej)
                {
                    if (view != null) continue;          // deja en train de sortir
                    view = Acquire(p, to);
                    _views[p.id] = view;
                }
                Vector3 target = LineLayout.ProductTarget(p, to, _unitCounts);
                Vector3 pos = target;
                if (from != null && !ReferenceEquals(from, to) && _previousMap.TryGetValue(p.id, out var before))
                    pos = Vector3.Lerp(LineLayout.ProductTarget(before, from, _unitCounts), target, alpha);
                view.tr.position = pos;
                view.lastPos = pos;
                view.lastStation = p.st;
                view.renderer.sharedMaterial = MaterialFor(p);
            }

            // produits disparus de la liste : sortis de la ligne
            _toRemove.Clear();
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (_seen.Contains(kv.Key) || v.leavingOk || v.leavingRej) continue;
                if (v.lastStation == 3) StartLeavingOk(v);
                else if (v.lastStation == 2) StartLeavingRej(v);
                else _toRemove.Add(kv.Key);
            }
            for (int i = 0; i < _toRemove.Count; i++) Release(_toRemove[i]);

            UpdateLeaving(dt);
        }

        void StartLeavingOk(ProductView v)
        {
            v.leavingOk = true;
            v.tr.localScale = new Vector3(0.62f, 0.5f, 0.62f);
            v.renderer.sharedMaterial = materials.carton;
            v.leaveTarget = new Vector3(24.5f, LineLayout.BeltTop + 0.25f, 0f);
        }

        void StartLeavingRej(ProductView v)
        {
            v.leavingRej = true;
            v.renderer.sharedMaterial = materials.productRejected;
            v.leaveTarget = new Vector3(LineLayout.BinPos.x, 0.4f, LineLayout.BinPos.z);
            v.arc = 0f;
        }

        void UpdateLeaving(float dt)
        {
            float k = 1f - Mathf.Exp(-dt * 10f);
            _toRemove.Clear();
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (v.leavingRej)
                {
                    v.arc = Mathf.Min(1f, v.arc + dt * 1.6f);
                    var p = Vector3.Lerp(v.tr.position, v.leaveTarget, k);
                    p.y = v.leaveTarget.y + Mathf.Sin(v.arc * Mathf.PI) * 1.2f + (1f - v.arc) * 0.6f;
                    v.tr.position = p;
                    v.tr.Rotate(dt * 6f * Mathf.Rad2Deg, 0f, 0f, Space.World);
                    if (v.arc >= 1f) _toRemove.Add(kv.Key);
                }
                else if (v.leavingOk)
                {
                    v.tr.position = Vector3.Lerp(v.tr.position, v.leaveTarget, k * 0.6f);
                    if (Vector3.Distance(v.tr.position, v.leaveTarget) < 0.15f)
                    {
                        _palletCount = _palletCount >= LineLayout.PalletCapacity ? 1 : _palletCount + 1;
                        RefreshPallet();
                        _toRemove.Add(kv.Key);
                    }
                }
            }
            for (int i = 0; i < _toRemove.Count; i++) Release(_toRemove[i]);
        }

        void RefreshPallet()
        {
            if (palletBoxes == null) return;
            for (int i = 0; i < palletBoxes.Length; i++)
                if (palletBoxes[i] != null) palletBoxes[i].gameObject.SetActive(i < _palletCount);
        }

        Material MaterialFor(ProductDto p)
        {
            if (p.rej && p.p >= 1f) return materials.productRejected;
            if (p.st == 2) return materials.productProcessed;
            if (p.st >= 3) return materials.productChecked;
            return materials.productRaw;
        }

        ProductView Acquire(ProductDto p, StateMsg state)
        {
            ProductView v;
            if (_pool.Count > 0)
            {
                v = _pool.Pop();
                v.tr.gameObject.SetActive(true);
            }
            else
            {
                var go = LineBuilder.Prim(PrimitiveType.Cube, productsRoot, "Produit", Vector3.one, Vector3.zero, materials.productRaw);
                v = new ProductView { tr = go.transform, renderer = go.GetComponent<Renderer>() };
            }
            v.tr.localScale = new Vector3(0.5f, 0.42f, 0.5f);
            v.tr.rotation = Quaternion.identity;
            v.leavingOk = v.leavingRej = false;
            v.arc = 0f;
            v.lastStation = p.st;
            var start = LineLayout.ProductTarget(p, state, _unitCounts);
            if (p.st == 0 && p.s == "p") start.y += 1.6f; // sort de la tremie
            v.tr.position = start;
            return v;
        }

        void Release(int id)
        {
            if (!_views.TryGetValue(id, out var v)) return;
            v.tr.gameObject.SetActive(false);
            _pool.Push(v);
            _views.Remove(id);
        }

        void ClearProducts()
        {
            _toRemove.Clear();
            foreach (var kv in _views) _toRemove.Add(kv.Key);
            for (int i = 0; i < _toRemove.Count; i++) Release(_toRemove[i]);
            _previousMapSource = null;
            _palletCount = 0;
            RefreshPallet();
        }
    }
}
