using System.Collections;
using Shared;
using UnityEngine;
using UnityEngine.UI;
using Launch;

namespace Client.Battle
{
    /// <summary>战斗面板：摇杆、技能键、小眼睛拖镜头、瞄准取消。布局在 Prefabs/UI/BattlePanel。</summary>
    public class BattlePanel : MonoBehaviour
    {
        struct SkillSlot
        {
            public int SkillId;
            public Image CdOverlay;
            public Text CdText;
        }

        const string PrefabKey = "Prefabs/UI/BattlePanel";

        [SerializeField] Text matchClockText;
        [SerializeField] Text respawnText;
        [SerializeField] GameObject joystickRoot;
        [SerializeField] VirtualJoystick joystick;
        [SerializeField] RectTransform joystickHandle;
        [SerializeField] Image lookImage;
        [SerializeField] RectTransform lookPupil;
        [SerializeField] SkillHudSlot[] slots;
        [SerializeField] RectTransform aimCancelRt;
        [SerializeField] RectTransform skillStickRt;
        [SerializeField] RectTransform skillStickHandle;

        static readonly Color LookIdle = new Color(0.086f, 0.094f, 0.122f, 0.90f);
        static readonly Color LookActive = new Color(0.28f, 0.36f, 0.32f, 0.95f);
        static readonly Color SlotIdle = new Color(0.176f, 0.220f, 0.235f, 0.94f);
        static readonly Color SlotEmpty = new Color(0.14f, 0.15f, 0.17f, 0.45f);

        static BattlePanel _instance;
        SkillSlot[] _runtimeSlots;
        Camera _uiCam;
        RectTransform _cancelRt;
        RectTransform _stickRt;
        RectTransform _stickHandle;

        public static CameraFollowComponent LocalFollow =>
            GetLocalPlayer()?.GetComponent<CameraFollowComponent>();

        public static BattlePanel Ensure()
        {
            if (_instance != null)
            {
                _instance.BindHotkeys();
                return _instance;
            }

            // 同步路径：仅缓存命中 / 编辑器直读；否则请走 EnsureAsync
            if (!TryInstantiateFromLoadedPrefab())
                BattleAssets.Run(CoEnsure(null));
            return _instance;
        }

        public static IEnumerator EnsureAsync()
        {
            if (_instance != null)
            {
                _instance.BindHotkeys();
                yield break;
            }

            if (TryInstantiateFromLoadedPrefab())
                yield break;

            GameObject prefab = null;
            yield return BattleAssets.LoadAsync<GameObject>(PrefabKey, p => prefab = p);
            if (prefab == null)
            {
                Debug.LogError("[BattlePanel] 找不到 " + PrefabKey);
                yield break;
            }

            FinishInstantiate(prefab);
        }

        static IEnumerator CoEnsure(System.Action<BattlePanel> onDone)
        {
            yield return EnsureAsync();
            onDone?.Invoke(_instance);
        }

        static bool TryInstantiateFromLoadedPrefab()
        {
            if (_instance != null) return true;
            var existing = Object.FindObjectOfType<BattlePanel>();
            if (existing != null)
            {
                _instance = existing;
                _instance.BindHotkeys();
                return true;
            }

            var prefab = BattleAssets.Load<GameObject>(PrefabKey);
            if (prefab == null) return false;
            FinishInstantiate(prefab);
            return true;
        }

        static void FinishInstantiate(GameObject prefab)
        {
            var go = Object.Instantiate(prefab);
            go.name = "BattlePanel";
            _instance = go.GetComponent<BattlePanel>();
            if (_instance == null)
                Debug.LogError("[BattlePanel] Prefab 根上缺少 BattlePanel");
            else
                _instance.BindHotkeys();
        }

        public static void Shutdown()
        {
            PlayerInput.Clear();
            _instance = null;
        }

        public static bool IsOverAimCancel(Vector2 screenPos)
        {
            if (_instance == null || _instance._cancelRt == null)
                return false;
            if (!_instance._cancelRt.gameObject.activeSelf)
                return false;
            return RectTransformUtility.RectangleContainsScreenPoint(
                _instance._cancelRt, screenPos, _instance._uiCam);
        }

        void Awake()
        {
            _instance = this;
            AppUiRoot.AttachMain(transform);
            CacheUiCamera();
            _cancelRt = aimCancelRt;
            _stickRt = skillStickRt;
            _stickHandle = skillStickHandle;
            if (_cancelRt == null)
                _cancelRt = transform.Find("AimCancel") as RectTransform;
            if (_stickRt == null)
                _stickRt = transform.Find("SkillStick") as RectTransform;
            if (_stickHandle == null && _stickRt != null)
                _stickHandle = _stickRt.Find("Handle") as RectTransform;
            if (_cancelRt != null)
                _cancelRt.gameObject.SetActive(false);
            if (_stickRt != null)
                _stickRt.gameObject.SetActive(false);
            SetupMovePad();
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        void CacheUiCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            _uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
        }

        void SetupMovePad()
        {
            if (joystickRoot == null || joystick == null || joystickHandle == null)
                return;

            if (!PlayerInput.ShowsMovePad)
            {
                joystickRoot.SetActive(false);
                PlayerInput.ClearJoystick();
                return;
            }

            joystickRoot.SetActive(true);
            var pad = (RectTransform)joystickRoot.transform;
            var visual = pad.Find("Bg") as RectTransform;
            if (visual == null)
                visual = joystickHandle.parent as RectTransform;
            if (visual == null)
                return;

            joystick.Bind(pad, visual, joystickHandle, 80f);
        }

        void BindHotkeys()
        {
            var defs = SkillHotkeyTable.All;
            EnsureSlotsMatchHotkeys(defs);
            _runtimeSlots = slots != null && slots.Length > 0
                ? new SkillSlot[slots.Length]
                : System.Array.Empty<SkillSlot>();

            if (slots == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;

                if (!TryFindHotkey(defs, slot.Hotkey, out var def))
                {
                    slot.gameObject.SetActive(false);
                    continue;
                }

                slot.gameObject.SetActive(true);
                ApplySlotLayout(slot, def);
                bool isInterrupt = def.Hotkey == KeyCode.S && def.SkillId == 0;
                Color tint = def.HasIconTint
                    ? def.IconTint
                    : !def.IsBound
                        ? SlotEmpty
                        : SlotIdle;

                if (slot.IconBg != null)
                    slot.IconBg.color = tint;
                if (slot.LabelText != null)
                    slot.LabelText.text = $"{def.Hotkey}\n{def.Label}";
                if (slot.Button != null)
                {
                    slot.Button.interactable = def.IsBound || isInterrupt;
                    slot.Button.navigation = new Navigation { mode = Navigation.Mode.None };
                    slot.Button.onClick.RemoveAllListeners();
                    if (isInterrupt)
                        slot.Button.onClick.AddListener(PlayerInput.HudCancel);
                }

                if (slot.AimButton != null)
                {
                    slot.AimButton.SkillId = def.IsBound && !isInterrupt ? def.SkillId : 0;
                    slot.AimButton.enabled = slot.AimButton.SkillId > 0;
                }

                if (slot.CdOverlay != null)
                    slot.CdOverlay.fillAmount = 0f;
                if (slot.CdText != null)
                    slot.CdText.text = "";

                _runtimeSlots[i] = new SkillSlot
                {
                    SkillId = def.SkillId,
                    CdOverlay = slot.CdOverlay,
                    CdText = slot.CdText
                };
            }
        }

        /// <summary>热键表是布局源：缺格就克隆，位置/尺寸按配置。</summary>
        void EnsureSlotsMatchHotkeys(SkillHotkeyTable.Entry[] defs)
        {
            if (defs == null || defs.Length == 0 || slots == null || slots.Length == 0)
                return;

            SkillHudSlot template = null;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                {
                    template = slots[i];
                    break;
                }
            }
            if (template == null) return;

            var list = new System.Collections.Generic.List<SkillHudSlot>(slots.Length + 4);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null)
                    list.Add(slots[i]);
            }

            var parent = template.transform.parent;
            for (int i = 0; i < defs.Length; i++)
            {
                var def = defs[i];
                if (def.Hotkey == KeyCode.None) continue;

                SkillHudSlot found = null;
                for (int s = 0; s < list.Count; s++)
                {
                    if (list[s] != null && list[s].Hotkey == def.Hotkey)
                    {
                        found = list[s];
                        break;
                    }
                }
                if (found != null) continue;

                var go = Object.Instantiate(template.gameObject, parent);
                go.name = "Skill_" + def.Hotkey;
                found = go.GetComponent<SkillHudSlot>();
                if (found == null)
                {
                    Object.Destroy(go);
                    continue;
                }
                found.Hotkey = def.Hotkey;
                list.Add(found);
            }

            slots = list.ToArray();
        }

        static void ApplySlotLayout(SkillHudSlot slot, SkillHotkeyTable.Entry def)
        {
            var rt = slot != null ? slot.transform as RectTransform : null;
            if (rt == null) return;
            rt.anchoredPosition = new Vector2(def.AnchorX, def.AnchorY);
            if (def.Size > 1f)
                rt.sizeDelta = new Vector2(def.Size, def.Size);
        }

        static bool TryFindHotkey(SkillHotkeyTable.Entry[] defs, KeyCode hotkey, out SkillHotkeyTable.Entry def)
        {
            def = default;
            if (defs == null) return false;
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i].Hotkey != hotkey) continue;
                def = defs[i];
                return true;
            }
            return false;
        }

        void Update()
        {
            RefreshCooldowns();
            RefreshTopInfo();
            RefreshLookButton();
            RefreshAimChrome();
        }

        void RefreshLookButton()
        {
            if (lookImage == null) return;
            var follow = LocalFollow;
            bool looking = follow != null && follow.IsLooking;
            lookImage.color = looking ? LookActive : LookIdle;
            if (lookPupil != null)
                lookPupil.localScale = looking ? new Vector3(1.15f, 1.15f, 1f) : Vector3.one;
        }

        void RefreshAimChrome()
        {
            int aimId = PlayerInput.ActiveAimSkillId;
            bool aimingDir = aimId > 0 && SkillRules.NeedsAimDirection(aimId);
            bool showCancel = aimingDir;
            bool showStick = aimingDir && PlayerInput.ShowsMovePad;

            if (_cancelRt != null && _cancelRt.gameObject.activeSelf != showCancel)
                _cancelRt.gameObject.SetActive(showCancel);
            if (_stickRt != null && _stickRt.gameObject.activeSelf != showStick)
                _stickRt.gameObject.SetActive(showStick);
            if (!aimingDir) return;

            if (_cancelRt != null)
                _cancelRt.SetAsLastSibling();

            if (!showStick) return;
            var slotRt = FindAimSlot(aimId);
            if (slotRt != null)
                _stickRt.position = slotRt.position;
            if (_stickHandle != null)
                _stickHandle.anchoredPosition = PlayerInput.SkillAimStick * 70f;
        }

        RectTransform FindAimSlot(int skillId)
        {
            if (slots == null) return null;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || slot.AimButton == null) continue;
                if (slot.AimButton.SkillId != skillId) continue;
                return slot.transform as RectTransform;
            }
            return null;
        }

        void RefreshTopInfo()
        {
            var session = BattleSystem.Instance?.Session;
            if (session == null)
            {
                SetChipActive(matchClockText, false);
                SetChipActive(respawnText, false);
                return;
            }

            if (matchClockText != null)
            {
                bool show = session.MatchPlaying || session.MatchEnded;
                SetChipActive(matchClockText, show);

                float t = !session.MatchPlaying
                    ? (session.MatchEnded ? session.MatchDuration : 0f)
                    : session.MatchElapsed;
                int m = Mathf.FloorToInt(t / 60f);
                int s = Mathf.FloorToInt(t % 60f);
                matchClockText.text = $"{m:00}:{s:00}";
            }

            if (respawnText == null) return;
            if (session.LocalPlayerDead && session.LocalRespawnEndsAt > 0f)
            {
                float remain = Mathf.Max(0f, session.LocalRespawnEndsAt - Time.time);
                bool show = remain > 0.05f;
                SetChipActive(respawnText, show);
                respawnText.text = show ? $"复活 {remain:0.0}s" : "";
            }
            else
            {
                SetChipActive(respawnText, false);
                respawnText.text = "";
            }
        }

        void RefreshCooldowns()
        {
            if (_runtimeSlots == null) return;
            var local = GetLocalPlayer();
            var skillComp = local?.GetComponent<SkillCastComponent>();

            for (int i = 0; i < _runtimeSlots.Length; i++)
            {
                var s = _runtimeSlots[i];
                if (s.SkillId <= 0 || s.CdOverlay == null) continue;

                float remain = skillComp != null ? skillComp.GetCooldownRemaining(s.SkillId) : 0f;
                float cd = SkillCatalog.Get(s.SkillId)?.Cooldown ?? 0f;
                float ratio = cd > 0.01f ? Mathf.Clamp01(remain / cd) : 0f;
                s.CdOverlay.fillAmount = ratio;
                if (s.CdText != null)
                    s.CdText.text = remain > 0.05f ? remain.ToString("0.0") : "";
            }
        }

        static void SetChipActive(Text label, bool show)
        {
            if (label == null) return;
            var go = label.transform.parent != null
                ? label.transform.parent.gameObject
                : label.gameObject;
            if (go.activeSelf != show)
                go.SetActive(show);
        }

        static Entity GetLocalPlayer()
        {
            long id = BattleSystem.Instance != null ? BattleSystem.Instance.LocalPlayerId : 0;
            if (id == 0) return null;
            return EntityManager.Instance?.GetEntity(id);
        }
    }
}
