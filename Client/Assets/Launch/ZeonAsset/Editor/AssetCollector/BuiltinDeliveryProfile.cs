using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.ZeonAsset.Editor
{
    /// <summary>
    /// 首包交付配置：决定哪些 Tag 的 Bundle 进入 StreamingAssets。
    /// 与收集 Tag（Group.Tags）分离——收集 Tag 描述业务分组；本 Profile 描述安装包交付。
    /// </summary>
    [CreateAssetMenu(fileName = "BuiltinDeliveryProfile", menuName = "Resource/Builtin Delivery Profile")]
    public class BuiltinDeliveryProfile : ScriptableObject
    {
        [Tooltip("命中任一 Tag 的 Bundle 进入首包（再按依赖闭包扩展）。")]
        public List<string> BuiltinTags = new List<string> { "Builtin" };

        [Tooltip("无论是否命中 BuiltinTags，始终并入首包（推荐含 Shaders）。")]
        public List<string> AlwaysIncludeTags = new List<string> { "Shaders" };

        [Tooltip("命中则强制排除（优先级高于 Builtin / Always）。代码热更 DLL 通常排除出安装包。")]
        public List<string> ExcludeTags = new List<string> { "HotUpdate", "AOT" };

        [Tooltip("选中 Bundle 的 DependBundles 递归并入首包。")]
        public bool IncludeDependencyClosure = true;

        [Tooltip("无 Tag 的 Bundle 是否进入首包（一般 false，避免整包塞进安装包）。")]
        public bool IncludeUntaggedBundles;

        /// <summary>优先从模拟 CDN 取全量 Manifest/Bundle；否则用 Build 输出目录。</summary>
        public bool PreferCdnAsSource = true;

        public static BuiltinDeliveryProfile CreateDefault()
        {
            var p = CreateInstance<BuiltinDeliveryProfile>();
            p.BuiltinTags = new List<string> { "Builtin" };
            p.AlwaysIncludeTags = new List<string> { "Shaders" };
            p.ExcludeTags = new List<string> { "HotUpdate", "AOT" };
            p.IncludeDependencyClosure = true;
            p.IncludeUntaggedBundles = false;
            p.PreferCdnAsSource = true;
            return p;
        }
    }
}
