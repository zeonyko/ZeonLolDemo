using UnityEngine;

namespace Client.Battle
{
    public struct PlayerInputFrame
    {
        public Vector2 MoveAxes;

        public bool HasClickMove;
        public Vector3 ClickMovePoint;
        public bool ClickMoveHeld;

        public int BeginAimSkillId;
        public Vector3 AimDir;
        public bool HasAimDir;
        public Vector3 AimPoint;
        public bool HasAimPoint;
        public float AimStickMag;

        public bool Confirm;
        public bool Cancel;
        public bool CameraLock;
        public bool SkillPointerUp;

        public bool WorldTap;
        public Vector2 WorldTapScreen;

        public int JoystickFingerId;
        public int AimFingerId;
    }

    public interface IBattleInputSource
    {
        bool ShowsMovePad { get; }
        void Collect(Camera cam, ref PlayerInputFrame frame);
    }

    /// <summary>本帧玩家输入快照。HUD / 摇杆写入，平台源补充，InputPlayerComponent 消费。</summary>
    public static class PlayerInput
    {
        public static PlayerInputFrame Frame;
        static IBattleInputSource _source;

        public static IBattleInputSource Source
        {
            get
            {
                if (_source == null)
                    Bind(Application.isMobilePlatform
                        ? (IBattleInputSource)new MobileBattleInput()
                        : new PcBattleInput());
                return _source;
            }
        }

        public static bool ShowsMovePad => Source.ShowsMovePad;

        public static int ActiveAimSkillId;
        public static Vector2 Joystick;
        public static int JoystickFingerId = -1;
        public static int AimFingerId = -1;
        public static int LookFingerId = int.MinValue; // 空闲用 MinValue，避免和鼠标 pointerId=-1 撞上
        public static Vector2 SkillAimStick;

        static int _hudBeginSkill;
        static int _hudBeginPointer = int.MinValue;
        static Vector2 _hudStick;
        static bool _hudSkillUp;
        static bool _hudCancel;
        static bool _hudLock;
        static int _uiUpPointer = int.MinValue;
        static int _uiUpFrame = -1;

        public static void Bind(IBattleInputSource source)
        {
            _source = source ?? new PcBattleInput();
        }

        public static void Collect(Camera cam)
        {
            Vector2 move = Joystick;
            if (move.sqrMagnitude < 0.04f)
                move = Vector2.zero;
            else if (move.sqrMagnitude > 1f)
                move.Normalize();

            var frame = new PlayerInputFrame
            {
                MoveAxes = move,
                JoystickFingerId = JoystickFingerId,
                AimFingerId = AimFingerId,
                AimStickMag = SkillAimStick.magnitude
            };

            if (_hudBeginSkill > 0)
            {
                frame.BeginAimSkillId = _hudBeginSkill;
                _hudBeginSkill = 0;
            }

            if (_hudStick.sqrMagnitude > 0.0001f && !frame.HasAimDir)
            {
                Vector3 dir = LocalPlayerMoveMath.ToCameraRelativeMove(_hudStick, cam);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    frame.AimDir = dir.normalized;
                    frame.HasAimDir = true;
                    frame.AimStickMag = Mathf.Clamp01(_hudStick.magnitude);
                }
            }

            if (_hudCancel)
            {
                frame.Cancel = true;
                _hudCancel = false;
            }

            if (_hudLock)
            {
                frame.CameraLock = true;
                _hudLock = false;
            }

            if (_hudSkillUp)
            {
                frame.SkillPointerUp = true;
                _hudSkillUp = false;
            }

            Source?.Collect(cam, ref frame);
            Frame = frame;
        }

        public static void HudBeginAim(int skillId, int pointerId)
        {
            if (skillId <= 0) return;
            _hudBeginSkill = skillId;
            _hudBeginPointer = pointerId;
            AimFingerId = pointerId;
            ActiveAimSkillId = skillId;
            SkillAimStick = Vector2.zero;
            _hudStick = Vector2.zero;
            // 按下当帧就要能画出射程圈（普攻无目标时也要有圈）
            if (SkillAimRangeView.ShowIndicator)
                SkillAimRangeView.EnsureDrawer();
        }

        public static void HudSetStick(Vector2 stick)
        {
            if (stick.sqrMagnitude > 1f)
                stick.Normalize();
            SkillAimStick = stick;
            _hudStick = stick;
        }

        public static void HudSkillPointerUp(int pointerId, bool cancel)
        {
            MarkUiPointerUp(pointerId);
            if (AimFingerId != int.MinValue && pointerId != AimFingerId)
                return;
            AimFingerId = -1;
            _hudSkillUp = true;
            if (cancel)
                _hudCancel = true;
        }

        public static void HudCancel() => _hudCancel = true;
        /// <summary>空格：镜头立刻回到主角。</summary>
        public static void HudRecenterCamera() => _hudLock = true;

        public static void SetLookFinger(int fingerId) => LookFingerId = fingerId;
        public static void ClearLookFinger() => LookFingerId = int.MinValue;

        public static void SetJoystick(Vector2 axes, int fingerId)
        {
            Joystick = axes;
            JoystickFingerId = fingerId;
        }

        public static void ClearJoystick()
        {
            Joystick = Vector2.zero;
            JoystickFingerId = -1;
        }

        public static bool IsReservedFinger(int pointerId)
        {
            return pointerId == JoystickFingerId
                   || pointerId == AimFingerId
                   || pointerId == LookFingerId;
        }

        public static void MarkUiPointerUp(int pointerId)
        {
            _uiUpPointer = pointerId;
            _uiUpFrame = Time.frameCount;
        }

        public static bool WasUiPointerUp(int pointerId)
        {
            return _uiUpFrame == Time.frameCount && _uiUpPointer == pointerId;
        }

        public static bool IsPointerOverUi(int pointerId)
        {
            if (UnityEngine.EventSystems.EventSystem.current == null) return false;
            if (pointerId < 0)
                return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            return UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(pointerId);
        }

        public static void Clear()
        {
            Frame = default;
            _source = null;
            ActiveAimSkillId = 0;
            Joystick = Vector2.zero;
            JoystickFingerId = -1;
            AimFingerId = -1;
            LookFingerId = int.MinValue;
            SkillAimStick = Vector2.zero;
            _hudBeginSkill = 0;
            _hudBeginPointer = int.MinValue;
            _hudStick = Vector2.zero;
            _hudSkillUp = false;
            _hudCancel = false;
            _hudLock = false;
            _uiUpPointer = int.MinValue;
            _uiUpFrame = -1;
        }
    }
}
