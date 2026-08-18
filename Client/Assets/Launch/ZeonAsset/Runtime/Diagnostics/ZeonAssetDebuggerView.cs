using UnityEngine;
using UnityEngine.UI;

namespace Game.ZeonAsset
{
    /// <summary>
    /// 资源 GM 面板 Prefab 引用。布局只改 Prefab，运行时只填内容。
    /// </summary>
    public sealed class ZeonAssetDebuggerView : MonoBehaviour
    {
        public GameObject WindowRoot;
        public GameObject HudRoot;
        public Text HudText;
        public Text FooterText;
        public Button CloseButton;

        [Header("主页签")]
        public Button TabLog;
        public Button TabAssets;
        public Button TabPerf;
        public GameObject LogPage;
        public GameObject AssetsPage;
        public GameObject PerfPage;

        [Header("日志")]
        public Button RepairButton;
        public Button FollowButton;
        public Button ScrollEndButton;
        public Button ClearLogButton;
        public Button UploadButton;
        public Text LogMetaText;
        public Text LogBodyText;
        public ScrollRect LogScroll;

        [Header("资源调试")]
        public Text HeaderStatus;
        public Button SubInspect;
        public Button SubResident;
        public Button SubBoot;
        public GameObject InspectRoot;
        public GameObject ResidentRoot;
        public GameObject BootRoot;
        public InputField PathInput;
        public InputField FilterInput;
        public Button KindAsset;
        public Button KindRaw;
        public Button KindScene;
        public Button QueryButton;
        public Button LoadButton;
        public Button UnloadButton;
        public Text InspectBody;
        public Button DumpLeakButton;
        public Button UnloadUnusedButton;
        public Button CopyBootButton;
        public Text ResidentBody;
        public Text BootBody;

        [Header("性能")]
        public Button HudToggle;
        public Button Fps30;
        public Button Fps60;
        public Button RestorePower;
        public Button DumpPerf;
        public Text PerfBody;

        public static void SetLabel(Button button, string text)
        {
            if (button == null)
                return;
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
                label.text = text;
        }

        public static void Paint(Button button, bool on)
        {
            if (button == null)
                return;
            var image = button.GetComponent<Image>();
            if (image != null)
                image.color = on
                    ? new Color(0.28f, 0.24f, 0.14f, 1f)
                    : new Color(0.18f, 0.19f, 0.23f, 1f);
            var label = button.GetComponentInChildren<Text>();
            if (label != null)
                label.color = on
                    ? new Color(0.91f, 0.76f, 0.38f, 1f)
                    : new Color(0.62f, 0.65f, 0.70f, 1f);
        }

        public static void SetActive(GameObject go, bool active)
        {
            if (go != null && go.activeSelf != active)
                go.SetActive(active);
        }

        /// <summary>
        /// 字号加大后嵌套 ContentSizeFitter 经常算不出高度，滑条会拉不动。
        /// 按正文 preferredHeight 写死 Content 高度。
        /// </summary>
        public static void FitScrollText(Text body)
        {
            if (body == null)
                return;
            var scroll = body.GetComponentInParent<ScrollRect>();
            if (scroll == null)
                return;
            FitScroll(scroll, body);
        }

        public static void FitAllScrolls(GameObject root)
        {
            if (root == null || !root.activeInHierarchy)
                return;
            var scrolls = root.GetComponentsInChildren<ScrollRect>(false);
            for (int i = 0; i < scrolls.Length; i++)
                FitScroll(scrolls[i], null);
        }

        public static void FitScroll(ScrollRect scroll, Text body)
        {
            if (scroll == null || scroll.content == null)
                return;
            if (scroll.viewport != null && scroll.viewport.rect.width < 1f)
                return;

            if (body == null)
            {
                var found = scroll.content.GetComponentsInChildren<Text>(false);
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != null && found[i].gameObject.name == "Body")
                    {
                        body = found[i];
                        break;
                    }
                }
            }

            DisableFitter(scroll.content);
            Canvas.ForceUpdateCanvases();

            float h = 8f;
            if (body != null)
            {
                DisableFitter(body.rectTransform);
                LayoutRebuilder.ForceRebuildLayoutImmediate(body.rectTransform);
                h = Mathf.Max(h, body.preferredHeight + 12f);
                body.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
            h = Mathf.Max(h, LayoutUtility.GetPreferredHeight(scroll.content));

            var content = scroll.content;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
            var le = content.GetComponent<LayoutElement>();
            if (le != null && le.flexibleHeight < 0.5f)
            {
                le.minHeight = h;
                le.preferredHeight = h;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        public static void FitPreferredHeight(Text text, float minHeight)
        {
            if (text == null)
                return;
            var parent = text.rectTransform.parent as RectTransform;
            if (parent != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
            float h = Mathf.Max(minHeight, text.preferredHeight + 8f);
            var le = text.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.minHeight = h;
                le.preferredHeight = h;
            }

            text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
        }

        static void DisableFitter(Component target)
        {
            if (target == null)
                return;
            var fitter = target.GetComponent<ContentSizeFitter>();
            if (fitter != null)
                fitter.enabled = false;
        }
    }
}
