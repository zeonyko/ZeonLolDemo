using UnityEngine;

namespace Client.Battle
{
    /// <summary>对账调试标记：脚底圈出本地权威位(S) / 远端英雄原始包位(R)。</summary>
    public class SyncCompareComponent : Component
    {
        public static SyncCompareComponent Instance { get; private set; }

        const float RingRadius = 0.32f;
        const float RingY = 0.03f;
        const float RingWidth = 0.045f;
        const int RingSegments = 28;

        public Vector3 LastAuthPos { get; private set; }
        public bool HasAuthPos { get; private set; }
        public float LastAuthLead { get; private set; }
        public float LastReconcileError { get; private set; }
        public int PendingPredictions { get; private set; }
        public uint LastAckSeq { get; private set; }
        public bool LastReconcileApplied { get; private set; }
        public bool LastReconcileHardSnap { get; private set; }

        sealed class FootMarker
        {
            public GameObject Root;
            public TextMesh Label;
        }

        readonly FootMarker _auth = new FootMarker();
        readonly FootMarker _rawRemote = new FootMarker();

        public override void OnAwake()
        {
            Instance = this;
        }

        public override void OnUpdate(float dt)
        {
            UpdateMarkers();
        }

        public override void OnLateUpdate(float dt)
        {
            FaceCamera(_auth.Label);
            FaceCamera(_rawRemote.Label);
        }

        public override void OnDestroy()
        {
            DestroyMarker(_auth);
            DestroyMarker(_rawRemote);
            if (Instance == this) Instance = null;
        }

        public void ReportReconcile(
            Vector3 authPos,
            float authLead,
            float postReplayError,
            int pendingAfterAck,
            uint ackSeq,
            bool applied,
            bool hardSnap)
        {
            LastAuthPos = authPos;
            HasAuthPos = true;
            LastAuthLead = authLead;
            LastReconcileError = postReplayError;
            PendingPredictions = pendingAfterAck;
            LastAckSeq = ackSeq;
            LastReconcileApplied = applied;
            LastReconcileHardSnap = hardSnap;
        }

        public void ReportRemoteRawSnapshot(Vector3 rawPos)
        {
            EnsureMarker(_rawRemote, "Marker_RemoteRaw", new Color(1f, 0.35f, 1f, 0.9f), "R");
            if (_rawRemote.Root != null)
            {
                PlaceAtFeet(_rawRemote.Root, rawPos);
                _rawRemote.Root.SetActive(SyncDebugSettings.ShowMarkers);
            }
        }

        void UpdateMarkers()
        {
            bool show = SyncDebugSettings.ShowMarkers;

            if (HasAuthPos)
            {
                EnsureMarker(_auth, "Marker_ServerAuth", new Color(1f, 0.25f, 0.2f, 0.9f), "S");
                if (_auth.Root != null)
                {
                    PlaceAtFeet(_auth.Root, LastAuthPos);
                    _auth.Root.SetActive(show);
                }
            }
            else if (_auth.Root != null)
            {
                _auth.Root.SetActive(false);
            }

            if (_rawRemote.Root != null && !show)
                _rawRemote.Root.SetActive(false);
        }

        static void EnsureMarker(FootMarker marker, string name, Color color, string labelText)
        {
            if (marker.Root != null) return;
            marker.Root = CreateFootMarker(name, color, out marker.Label, labelText);
        }

        static void DestroyMarker(FootMarker marker)
        {
            if (marker.Root != null) Object.Destroy(marker.Root);
            marker.Root = null;
            marker.Label = null;
        }

        static void PlaceAtFeet(GameObject marker, Vector3 feetPos)
        {
            marker.transform.position = feetPos;
        }

        static GameObject CreateFootMarker(string name, Color color, out TextMesh label, string labelText)
        {
            var go = new GameObject(name);
            var line = go.AddComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = false;
            line.widthMultiplier = RingWidth;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = RingSegments;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = (i / (float)RingSegments) * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * RingRadius, RingY, Mathf.Sin(a) * RingRadius));
            }

            BattleUnlitTint.Apply(line, color);
            label = AttachLabel(go, color, labelText);
            go.SetActive(false);
            return go;
        }

        static TextMesh AttachLabel(GameObject parent, Color color, string labelText)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            var label = labelGo.AddComponent<TextMesh>();
            label.text = labelText;
            label.color = color;
            label.fontSize = 48;
            label.characterSize = 0.05f;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            return label;
        }

        static void FaceCamera(TextMesh mesh)
        {
            if (mesh == null || Camera.main == null) return;
            mesh.transform.rotation = Camera.main.transform.rotation;
        }
    }
}
