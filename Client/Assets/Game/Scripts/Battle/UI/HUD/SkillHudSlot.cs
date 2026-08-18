using UnityEngine;
using UnityEngine.UI;

namespace Client.Battle
{
    /// <summary>技能格在 Prefab 上的引用。开战时由 BattlePanel 按热键表绑定。</summary>
    public sealed class SkillHudSlot : MonoBehaviour
    {
        public KeyCode Hotkey;
        public Button Button;
        public Text LabelText;
        public Text CdText;
        public Image CdOverlay;
        public Image IconBg;
        public SkillHudAimButton AimButton;
    }
}
