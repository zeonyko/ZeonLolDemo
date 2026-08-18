using UnityEngine;
using UnityEngine.UI;
using Launch;

namespace Login
{
    /// <summary>登录页。Prefab 与脚本同名；挂到 <see cref="AppUiRoot"/> 主层，不自带 Canvas。</summary>
    public sealed class LoginPanel : MonoBehaviour
    {
        [SerializeField] private Button enterButton;
        [SerializeField] private Text statusText;

        private bool _entering;

        private void Awake()
        {
            AppUiRoot.AttachMain(transform);
            HideSceneMarker("SceneMarker_LoginScene");
            TintCamera(new Color32(11, 18, 24, 255));
            SetStatus(string.Empty);

            if (enterButton != null)
            {
                enterButton.onClick.RemoveListener(OnEnterClicked);
                enterButton.onClick.AddListener(OnEnterClicked);
            }
        }

        private void OnDestroy()
        {
            if (enterButton != null)
                enterButton.onClick.RemoveListener(OnEnterClicked);
        }

        private void OnEnterClicked()
        {
            var runner = AppSceneRunner.EnsureHost();
            if (_entering || runner.IsBusy)
                return;

            _entering = true;
            if (enterButton != null)
                enterButton.interactable = false;
            SetStatus("正在进入游戏…");
            runner.LoadScene(
                LoginDestinations.Current,
                LoginDestinations.BattleResourceTags,
                onFail: OnEnterFailed);
        }

        private void OnEnterFailed(string error)
        {
            _entering = false;
            if (enterButton != null)
                enterButton.interactable = true;
            SetStatus("进入失败: " + error);
        }

        private void SetStatus(string text)
        {
            if (statusText != null)
                statusText.text = text;
        }

        private static void HideSceneMarker(string name)
        {
            var marker = GameObject.Find(name);
            if (marker != null)
                marker.SetActive(false);
        }

        private static void TintCamera(Color color)
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = color;
        }
    }
}
