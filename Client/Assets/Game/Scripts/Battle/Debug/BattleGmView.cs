using UnityEngine;
using UnityEngine.UI;

namespace Client.Battle
{
    /// <summary>战斗 GM Prefab 引用。布局只改 Prefab，运行时只填内容。</summary>
    public sealed class BattleGmView : MonoBehaviour
    {
        public GameObject WindowRoot;
        public Button ToggleButton;
        public Button CloseButton;
        public Button DumpButton;
        public Text StatusText;

        [Header("页签")]
        public Button TabState;
        public Button TabDelay;
        public Button TabSync;
        public Button TabStory;
        public GameObject StatePage;
        public GameObject DelayPage;
        public GameObject SyncPage;
        public GameObject StoryPage;

        [Header("状态")]
        public Text StateBody;

        [Header("延迟")]
        public Button DelaySimButton;
        public Button RttPlus;
        public Button RttMinus;
        public Button Time025;
        public Button Time1;
        public Text PacketBody;

        [Header("同步")]
        public Button MarkersButton;
        public Button LocalPredButton;
        public Button RemoteInterpButton;
        public Button AimRangeButton;

        [Header("剧情")]
        public Button StorySuppress;
        public Button StoryComeback;
        public Button StoryTrade;
        public Button StoryWhiff;
        public Button StoryKite;
        public Button StoryTwoVOne;
        public Button StoryStop;
    }
}
