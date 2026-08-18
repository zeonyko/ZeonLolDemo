using Shared;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Client.Battle
{
    /// <summary>
    /// 俯视镜头：默认跟主角。按住小眼睛或中键拖地图，松手回到主角。空格立刻复位。
    /// </summary>
    public class CameraFollowComponent : Component
    {
        private Camera _mainCamera;
        private TransformComponent _transformComp;
        private ViewComponent _viewComp;
        private TransformComponent TransformComp =>
            _transformComp ??= Owner?.GetComponent<TransformComponent>();
        private ViewComponent ViewComp =>
            _viewComp ??= Owner?.GetComponent<ViewComponent>();

        private const float FixedPitch = 55f;
        private const float FixedYaw = 0f;
        private const float PanSlopPx = 14f;
        private const float MmbPanPixelsToWorld = 0.045f;
        private static readonly Plane GroundPlane = new Plane(Vector3.up, Vector3.zero);

        private float _height = 14f;
        private float _minHeight = 8f;
        private float _maxHeight = 28f;
        private float _zoomSpeed = 6f;
        private float _smoothSpeed = 28f;

        private bool _locked = true;
        private Vector3 _focusPoint;
        private bool _focusInited;

        private bool _lookHeld;
        private bool _mmbDragging;
        private Vector2 _lookLastScreen;

        private bool _pressValid;
        private bool _dragging;
        private bool _panned;
        private int _pressPointerId = int.MinValue;
        private Vector2 _pressScreen;
        private Vector3 _dragAnchorWorld;

        private float _shakeTimer;
        private float _shakeAmplitude;

        public bool IsLocked => _locked && !_lookHeld && !_mmbDragging;
        public bool IsLooking => _lookHeld || _mmbDragging;
        public bool BlockWorldTap => _dragging || _panned || IsLooking;

        public void PlayShake(float duration = 0.12f, float amplitude = 0.18f)
        {
            _shakeTimer = Mathf.Max(_shakeTimer, duration);
            _shakeAmplitude = Mathf.Max(_shakeAmplitude, amplitude);
        }

        public override void OnStart()
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
                Debug.LogError("[CameraFollow] 场景中没有 MainCamera，请在 MainBattle 里放好相机");
            Game.ZeonAsset.DevicePerf.ApplyToCamera(_mainCamera);
        }

        public override void OnLateUpdate(float dt)
        {
            var transformComp = TransformComp;
            if (transformComp == null || _mainCamera == null) return;

            Vector3 playerPos = ViewComp != null ? ViewComp.DisplayFeet : transformComp.Position;
            if (!_focusInited)
            {
                _focusPoint = Flatten(playerPos);
                _focusInited = true;
                ClampFocusToMap();
                SnapCameraToFocus();
                return;
            }

            HandleZoom();
            HandleLockToggle(playerPos);
            HandleMiddleMousePan();
            HandleTouchOrLeftPan();

            if (_locked && !IsLooking)
                _focusPoint = Vector3.Lerp(_focusPoint, Flatten(playerPos), 1f - Mathf.Exp(-_smoothSpeed * dt));

            ClampFocusToMap();
            ApplyCameraTransform(dt);
        }

        private void ClampFocusToMap()
        {
            float fx = _focusPoint.x;
            float fz = _focusPoint.z;
            AramMap.ClampCameraFocus(ref fx, ref fz);
            _focusPoint = new Vector3(fx, 0f, fz);
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _height -= scroll * _zoomSpeed;
                _height = Mathf.Clamp(_height, _minHeight, _maxHeight);
            }
        }

        private void HandleLockToggle(Vector3 playerPos)
        {
            if (!PlayerInput.Frame.CameraLock) return;
            SnapToPlayer(playerPos);
        }

        public void BeginLook(Vector2 screenPos)
        {
            _lookHeld = true;
            _lookLastScreen = screenPos;
            _locked = false;
            ClearLeftPress();
        }

        public void DragLook(Vector2 screenPos)
        {
            if (!_lookHeld && !_mmbDragging) return;
            Vector2 delta = screenPos - _lookLastScreen;
            _lookLastScreen = screenPos;
            if (delta.sqrMagnitude <= 0.0001f) return;
            ApplyScreenPan(delta);
            ClampFocusToMap();
            SnapCameraToFocus();
        }

        public void EndLook()
        {
            _lookHeld = false;
            _mmbDragging = false;
            var transformComp = TransformComp;
            if (transformComp != null)
                SnapToPlayer(ViewComp != null ? ViewComp.DisplayFeet : transformComp.Position);
            else
                _locked = true;
        }

        void SnapToPlayer(Vector3 playerPos)
        {
            _lookHeld = false;
            _mmbDragging = false;
            _locked = true;
            _focusPoint = Flatten(playerPos);
            ClearLeftPress();
        }

        /// <summary>PC 中键：按住拖地图，松手回到主角。</summary>
        private void HandleMiddleMousePan()
        {
            if (Application.isMobilePlatform)
                return;

            Vector2 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(2))
            {
                _mmbDragging = true;
                BeginLook(mouse);
            }

            if (_mmbDragging && Input.GetMouseButton(2))
                DragLook(mouse);

            if (Input.GetMouseButtonUp(2) && _mmbDragging)
            {
                _mmbDragging = false;
                if (PlayerInput.LookFingerId == int.MinValue)
                    EndLook();
            }
        }

        private void ApplyScreenPan(Vector2 screenDelta)
        {
            // 屏幕右/上 → 地面右/前；高度越大拖得越快
            float scale = MmbPanPixelsToWorld * (_height / 14f);
            Vector3 right = _mainCamera.transform.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
            right.Normalize();

            Vector3 forward = _mainCamera.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            _focusPoint -= right * (screenDelta.x * scale);
            _focusPoint -= forward * (screenDelta.y * scale);
        }

        /// <summary>触屏 / 解锁后的左键：地面射线拖拽。</summary>
        private void HandleTouchOrLeftPan()
        {
            if (IsLooking)
                return;

            if (_locked)
            {
                ClearLeftPress();
                return;
            }

            if (!TryReadLeftOrTouch(
                    out int pointerId, out Vector2 pos, out bool down, out bool hold, out bool up))
            {
                if (!hold && !down)
                    ClearLeftPress();
                return;
            }

            if (PlayerInput.IsReservedFinger(pointerId))
            {
                if (pointerId == _pressPointerId)
                    ClearLeftPress();
                return;
            }

            if (down)
            {
                _pressPointerId = pointerId;
                _pressScreen = pos;
                _pressValid = !IsPointerOverUi(pointerId) && TryRaycastGround(pos, out _dragAnchorWorld);
                _dragging = false;
                _panned = false;
            }

            if (_pressValid && hold && pointerId == _pressPointerId)
            {
                if (!_panned && (pos - _pressScreen).sqrMagnitude > PanSlopPx * PanSlopPx)
                {
                    _panned = true;
                    _dragging = true;
                }

                if (_dragging && TryRaycastGround(pos, out Vector3 hit))
                {
                    Vector3 delta = _dragAnchorWorld - hit;
                    delta.y = 0f;
                    if (delta.sqrMagnitude > 0.000001f)
                    {
                        _focusPoint += delta;
                        ClampFocusToMap();
                        SnapCameraToFocus();
                        if (TryRaycastGround(pos, out Vector3 hitAfter))
                            _dragAnchorWorld = hitAfter;
                    }
                }
            }

            if (up && pointerId == _pressPointerId)
                ClearLeftPress();
        }

        private void ClearLeftPress()
        {
            _pressValid = false;
            _dragging = false;
            _panned = false;
            _pressPointerId = int.MinValue;
        }

        private static bool TryReadLeftOrTouch(
            out int pointerId, out Vector2 pos, out bool down, out bool hold, out bool up)
        {
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    Touch t = Input.GetTouch(i);
                    if (PlayerInput.IsReservedFinger(t.fingerId)) continue;
                    pointerId = t.fingerId;
                    pos = t.position;
                    down = t.phase == TouchPhase.Began;
                    hold = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
                    up = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                    return true;
                }

                pointerId = int.MinValue;
                pos = default;
                down = hold = up = false;
                return false;
            }

            pointerId = -1;
            pos = Input.mousePosition;
            down = Input.GetMouseButtonDown(0);
            hold = Input.GetMouseButton(0);
            up = Input.GetMouseButtonUp(0);
            return down || hold || up;
        }

        private bool TryRaycastGround(Vector3 screenPos, out Vector3 hit)
        {
            hit = Vector3.zero;
            Ray ray = _mainCamera.ScreenPointToRay(screenPos);
            if (!GroundPlane.Raycast(ray, out float enter)) return false;
            hit = ray.GetPoint(enter);
            return true;
        }

        private void SnapCameraToFocus()
        {
            Quaternion rot = Quaternion.Euler(FixedPitch, FixedYaw, 0f);
            float dist = _height / Mathf.Sin(FixedPitch * Mathf.Deg2Rad);
            Vector3 lookAt = _focusPoint + Vector3.up * 0.5f;
            _mainCamera.transform.SetPositionAndRotation(lookAt - rot * Vector3.forward * dist, rot);
        }

        private void ApplyCameraTransform(float dt)
        {
            if (_shakeTimer > 0f)
                _shakeTimer -= dt;

            Quaternion rot = Quaternion.Euler(FixedPitch, FixedYaw, 0f);
            float dist = _height / Mathf.Sin(FixedPitch * Mathf.Deg2Rad);
            Vector3 lookAt = _focusPoint + Vector3.up * 0.5f;
            Vector3 desiredPos = lookAt - rot * Vector3.forward * dist;

            if (_shakeTimer > 0f)
            {
                float t = Mathf.Clamp01(_shakeTimer / 0.12f);
                desiredPos += new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-0.4f, 0.4f),
                    UnityEngine.Random.Range(-1f, 1f)) * (_shakeAmplitude * t);
            }

            bool hard = _dragging || IsLooking;
            float lerpT = hard ? 1f : (1f - Mathf.Exp(-_smoothSpeed * dt));
            _mainCamera.transform.position = Vector3.Lerp(_mainCamera.transform.position, desiredPos, lerpT);
            _mainCamera.transform.rotation = Quaternion.Slerp(_mainCamera.transform.rotation, rot, lerpT);
        }

        private static Vector3 Flatten(Vector3 p) => new Vector3(p.x, 0f, p.z);

        private static bool IsPointerOverUi(int pointerId)
        {
            if (EventSystem.current == null) return false;
            if (pointerId < 0)
                return EventSystem.current.IsPointerOverGameObject();
            return EventSystem.current.IsPointerOverGameObject(pointerId);
        }
    }
}
