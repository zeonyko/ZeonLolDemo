using Shared;
using UnityEngine;

namespace Client.Battle
{
    /// <summary>场景里摆的碰撞盒。导出给逻辑用，不参与服务器物理。</summary>
    [ExecuteAlways]
    public sealed class MapCollisionVolume : MonoBehaviour
    {
        #region Inspector 参数
        [Tooltip("要不要写进碰撞配置")]
        public bool IncludeInExport = true;

        [Tooltip("逻辑尺寸（本地前后左右）。会乘物体缩放")]
        public Vector2 LocalSizeXZ = new Vector2(2f, 2f);
        #endregion

        #region 导出
        public string ExportName => string.IsNullOrEmpty(name) ? "Blocker" : name;

        /// <summary>导出为 JSON 用的 Blocker 数据（世界 AABB）。</summary>
        public MapCollisionBlockerData ToBlockerData()
        {
            ComputeWorldAabb(out float cx, out float cz, out float sx, out float sz);
            return new MapCollisionBlockerData
            {
                Name = ExportName,
                CenterX = cx,
                CenterZ = cz,
                SizeX = sx,
                SizeZ = sz
            };
        }

        /// <summary>把本地 XZ 尺寸的四个角点变换到世界空间，取轴对齐包围盒（支持旋转墙的保守包围）。</summary>
        public void ComputeWorldAabb(out float centerX, out float centerZ, out float sizeX, out float sizeZ)
        {
            float hx = Mathf.Max(0.01f, LocalSizeXZ.x * 0.5f);
            float hz = Mathf.Max(0.01f, LocalSizeXZ.y * 0.5f);
            // 本地角点 → 世界，取 XZ AABB（支持旋转墙的保守包围）
            Vector3[] local =
            {
                new Vector3(-hx, 0f, -hz),
                new Vector3(hx, 0f, -hz),
                new Vector3(hx, 0f, hz),
                new Vector3(-hx, 0f, hz),
            };

            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < local.Length; i++)
            {
                Vector3 w = transform.TransformPoint(local[i]);
                if (w.x < minX) minX = w.x;
                if (w.x > maxX) maxX = w.x;
                if (w.z < minZ) minZ = w.z;
                if (w.z > maxZ) maxZ = w.z;
            }

            centerX = (minX + maxX) * 0.5f;
            centerZ = (minZ + maxZ) * 0.5f;
            sizeX = Mathf.Max(0.01f, maxX - minX);
            sizeZ = Mathf.Max(0.01f, maxZ - minZ);
        }

        #endregion

        #region 编辑器可视化
        void OnDrawGizmos()
        {
            if (!IncludeInExport) return;
            ComputeWorldAabb(out float cx, out float cz, out float sx, out float sz);
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.35f);
            Gizmos.DrawCube(new Vector3(cx, 1.2f, cz), new Vector3(sx, 2.4f, sz));
            Gizmos.color = new Color(1f, 0.45f, 0.15f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(cx, 1.2f, cz), new Vector3(sx, 2.4f, sz));
        }
        #endregion
    }
}
