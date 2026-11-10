using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Twin.Scene
{
    /// <summary>
    /// Camera orbitale : clic gauche tourner, clic droit deplacer, molette zoomer, clic sur une
    /// machine pour s'en approcher. Toutes les valeurs sont lissees pour un mouvement sans a-coup.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class OrbitCamera : MonoBehaviour
    {
        public float smoothing = 10f;
        public float rotateSpeed = 0.25f;
        public float zoomSpeed = 0.12f;
        public float minDistance = 8f, maxDistance = 140f;

        public event Action<int> StationClicked;

        Camera _cam;
        // etat courant (affiche) et etat voulu (commande)
        Vector3 _target = new Vector3(4, 1, 0), _targetGoal = new Vector3(4, 1, 0);
        float _yaw, _yawGoal, _pitch, _pitchGoal, _dist = 40f, _distGoal = 40f;
        bool _userMoved;
        float _lastAspect;
        Vector2 _downPos;

        void Awake() { _cam = GetComponent<Camera>(); }

        void Start()
        {
            ResetView(true);
        }

        /// <summary>Recule la camera pour que toute la ligne tienne dans la largeur de l'ecran.</summary>
        public void ResetView(bool instant = false)
        {
            _userMoved = false;
            var dir = new Vector3(0.1f, 0.55f, -0.83f).normalized;
            float vfov = _cam.fieldOfView * Mathf.Deg2Rad;
            float aspect = Mathf.Max(0.3f, _cam.aspect);
            float hfov = 2f * Mathf.Atan(Mathf.Tan(vfov / 2f) * aspect);
            float d = Mathf.Max(30f, 30f / Mathf.Tan(hfov / 2f), 14f / Mathf.Tan(vfov / 2f)); // 30 = demi-largeur de la ligne + etiquettes
            _targetGoal = new Vector3(4, 1, 0);
            SetFromDirection(dir, d);
            if (instant) { _target = _targetGoal; _yaw = _yawGoal; _pitch = _pitchGoal; _dist = _distGoal; Apply(); }
        }

        /// <summary>Approche la camera d'un poste (comme focus() de la version Three.js).</summary>
        public void FocusStation(int station)
        {
            _userMoved = true;
            float x = LineLayout.StationX[Mathf.Clamp(station, 0, 3)];
            _targetGoal = new Vector3(x, 1.5f, 0);
            var offset = new Vector3(4f, 7.5f, -13f); // camera en (x+4, 9, -13) vue depuis la cible a hauteur 1,5
            SetFromDirection(offset.normalized, offset.magnitude);
        }

        void SetFromDirection(Vector3 dirFromTarget, float distance)
        {
            _distGoal = Mathf.Clamp(distance, minDistance, maxDistance);
            _pitchGoal = Mathf.Asin(Mathf.Clamp(dirFromTarget.y, -1f, 1f)) * Mathf.Rad2Deg;
            float yawGoal = Mathf.Atan2(dirFromTarget.x, -dirFromTarget.z) * Mathf.Rad2Deg;
            // garde la rotation la plus courte vers la nouvelle valeur
            _yawGoal = _yaw + Mathf.DeltaAngle(_yaw, yawGoal);
        }

        void LateUpdate()
        {
            HandleInput();

            if (!_userMoved && !Mathf.Approximately(_lastAspect, _cam.aspect))
            {
                _lastAspect = _cam.aspect;
                ResetView(false);
            }

            float k = 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime);
            _target = Vector3.Lerp(_target, _targetGoal, k);
            _yaw = Mathf.Lerp(_yaw, _yawGoal, k);
            _pitch = Mathf.Lerp(_pitch, _pitchGoal, k);
            _dist = Mathf.Lerp(_dist, _distGoal, k);
            Apply();
        }

        void Apply()
        {
            // position = cible + direction (sin yaw cos pitch, sin pitch, -cos yaw cos pitch) * distance
            float cp = Mathf.Cos(_pitch * Mathf.Deg2Rad), sp = Mathf.Sin(_pitch * Mathf.Deg2Rad);
            float cy = Mathf.Cos(_yaw * Mathf.Deg2Rad), sy = Mathf.Sin(_yaw * Mathf.Deg2Rad);
            var pos = _target + new Vector3(sy * cp, sp, -cy * cp) * _dist;
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(_target - pos, Vector3.up);
            // le brouillard suit la distance de la camera : il ne doit jamais voiler la ligne (environ 60 m de long)
            RenderSettings.fogStartDistance = _dist + 45f;
            RenderSettings.fogEndDistance = _dist + 130f;
        }

        void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 pos = mouse.position.ReadValue();
            if (pos.x < 0 || pos.y < 0 || pos.x > Screen.width || pos.y > Screen.height) return;

            if (mouse.leftButton.wasPressedThisFrame) _downPos = pos;

            Vector2 delta = mouse.delta.ReadValue();
            if (mouse.leftButton.isPressed && delta.sqrMagnitude > 0f)
            {
                _userMoved = true;
                _yawGoal -= delta.x * rotateSpeed;
                _pitchGoal = Mathf.Clamp(_pitchGoal + delta.y * rotateSpeed * -1f, 3.6f, 88f);
            }
            if (mouse.rightButton.isPressed && delta.sqrMagnitude > 0f)
            {
                _userMoved = true;
                float scale = _distGoal * 0.0016f;
                var right = transform.right; right.y = 0; right.Normalize();
                var fwd = transform.forward; fwd.y = 0; fwd.Normalize();
                _targetGoal -= (right * delta.x + fwd * delta.y) * scale;
            }
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _userMoved = true;
                _distGoal = Mathf.Clamp(_distGoal * (1f - Mathf.Sign(scroll) * zoomSpeed), minDistance, maxDistance);
            }

            if (mouse.leftButton.wasReleasedThisFrame && (pos - _downPos).magnitude < 5f)
                TryPickStation(pos);
        }

        void TryPickStation(Vector2 screenPos)
        {
            var ray = _cam.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out var hit, 300f))
            {
                var unit = hit.collider.GetComponentInParent<StationUnit>();
                if (unit != null)
                {
                    FocusStation(unit.station);
                    StationClicked?.Invoke(unit.station);
                }
            }
        }
    }
}
