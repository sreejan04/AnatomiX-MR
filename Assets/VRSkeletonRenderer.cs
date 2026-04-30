using UnityEngine;

public class VRSkeletonRenderer : MonoBehaviour
{
    [Header("References")]
    public PosePreview posePreview;
    public Transform skeletonParent; // drag the SkeletonParent empty GO here

    [Header("Visual")]
    public Color boneColor = Color.white;
    [Range(0.002f, 0.02f)]
    public float lineWidth = 0.008f;

    static readonly int[,] k_Bones =
    {
        // Face
        {0,1},{1,2},{2,3},{3,7},{0,4},{4,5},{5,6},{6,8},
        // Torso
        {11,12},{11,23},{12,24},{23,24},
        // Left arm
        {11,13},{13,15},{15,17},{15,19},{15,21},{17,19},
        // Right arm
        {12,14},{14,16},{16,18},{16,20},{16,22},{18,20},
        // Left leg
        {23,25},{25,27},{27,29},{29,31},{27,31},
        // Right leg
        {24,26},{26,28},{28,30},{30,32},{28,32}
    };

    LineRenderer[] m_Lines;

    void Awake()
    {
        if (skeletonParent == null)
            skeletonParent = new GameObject("SkeletonBones").transform;

        var mat = new Material(Shader.Find("Unlit/Color")) { color = boneColor };
        int count = k_Bones.GetLength(0);
        m_Lines = new LineRenderer[count];

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject($"Bone_{i:00}");
            go.transform.SetParent(skeletonParent, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.material = mat;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            m_Lines[i] = lr;
        }
    }

    void Update()
    {
        if (posePreview == null) return;

        for (int i = 0; i < k_Bones.GetLength(0); i++)
        {
            int a = k_Bones[i, 0];
            int b = k_Bones[i, 1];

            bool visible = posePreview.IsKeypointVisible(a)
                        && posePreview.IsKeypointVisible(b);

            m_Lines[i].enabled = visible;
            if (!visible) continue;

            m_Lines[i].SetPosition(0, posePreview.GetKeypointPosition(a));
            m_Lines[i].SetPosition(1, posePreview.GetKeypointPosition(b));
        }
    }
}