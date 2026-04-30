using UnityEngine;

public class VRBodyMeshRenderer : MonoBehaviour
{
    [System.Serializable]
    public class BodySegment
    {
        public string name;
        public GameObject meshPrefab;
        public int keypointA;
        public int keypointB;
        public float widthScale = 0.08f;
        public Vector3 meshForwardAxis = Vector3.up;
        [Tooltip("Extra rotation offset in degrees to fix FBX orientation")]
        public Vector3 rotationOffset = Vector3.zero;
    }

    [Header("References")]
    public PosePreview posePreview;
    public Transform segmentParent;

    [Header("Debug")]
    public bool forceShowAll = true;

    [Header("Body Segments")]
    public BodySegment[] segments;

    GameObject[] m_Instances;
    float[] m_MeshLengths;

    void Awake()
    {
        if (segmentParent == null)
            segmentParent = new GameObject("SegmentParent").transform;

        m_Instances = new GameObject[segments.Length];
        m_MeshLengths = new float[segments.Length];

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];

            if (seg.meshPrefab == null)
            {
                Debug.LogWarning($"[BodyMesh] '{seg.name}' has no mesh!");
                continue;
            }

            m_Instances[i] = Instantiate(
                seg.meshPrefab,
                Vector3.zero,
                Quaternion.identity,
                segmentParent
            );
            m_Instances[i].name = $"Segment_{seg.name}";

            SetLayerRecursively(m_Instances[i], 0);

            Renderer rend = m_Instances[i].GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Vector3 size = rend.bounds.size;
                Vector3 axis = seg.meshForwardAxis == Vector3.zero
                    ? Vector3.up : seg.meshForwardAxis.normalized;

                if (Mathf.Abs(axis.y) > 0.5f)
                    m_MeshLengths[i] = size.y;
                else if (Mathf.Abs(axis.z) > 0.5f)
                    m_MeshLengths[i] = size.z;
                else
                    m_MeshLengths[i] = size.x;

                if (m_MeshLengths[i] < 0.0001f)
                    m_MeshLengths[i] = 1f;
            }
            else
            {
                m_MeshLengths[i] = 1f;
            }

            m_Instances[i].SetActive(false);
        }
    }

    void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform child in go.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    void Update()
    {
        if (posePreview == null || m_Instances == null) return;

        for (int i = 0; i < segments.Length; i++)
        {
            var seg = segments[i];
            var inst = m_Instances[i];

            if (inst == null) continue;

            Vector3 posA = posePreview.GetKeypointPosition(seg.keypointA);
            Vector3 posB = posePreview.GetKeypointPosition(seg.keypointB);

            if (posA == Vector3.zero && posB == Vector3.zero)
            {
                inst.SetActive(false);
                continue;
            }

            if (!forceShowAll)
            {
                if (!posePreview.IsKeypointVisible(seg.keypointA) ||
                    !posePreview.IsKeypointVisible(seg.keypointB))
                {
                    inst.SetActive(false);
                    continue;
                }
            }

            float length = Vector3.Distance(posA, posB);
            if (length < 0.001f)
            {
                inst.SetActive(false);
                continue;
            }

            inst.SetActive(true);
            inst.transform.position = (posA + posB) * 0.5f;

            Vector3 direction = (posB - posA).normalized;
            Vector3 axis = seg.meshForwardAxis == Vector3.zero
                ? Vector3.up : seg.meshForwardAxis.normalized;

            // Base rotation from bone direction
            Quaternion baseRotation = Quaternion.FromToRotation(axis, direction);

            // Apply extra rotation offset to fix FBX orientation
            Quaternion offsetRotation = Quaternion.Euler(seg.rotationOffset);

            inst.transform.rotation = baseRotation * offsetRotation;

            float ratio = length / m_MeshLengths[i];

            if (Mathf.Abs(axis.y) > 0.5f)
                inst.transform.localScale =
                    new Vector3(seg.widthScale, ratio, seg.widthScale);
            else if (Mathf.Abs(axis.z) > 0.5f)
                inst.transform.localScale =
                    new Vector3(seg.widthScale, seg.widthScale, ratio);
            else
                inst.transform.localScale =
                    new Vector3(ratio, seg.widthScale, seg.widthScale);
        }
    }

    void OnDestroy()
    {
        if (m_Instances == null) return;
        foreach (var inst in m_Instances)
            if (inst != null) Destroy(inst);
    }
}