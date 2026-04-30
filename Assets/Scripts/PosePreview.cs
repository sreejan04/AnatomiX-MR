using UnityEngine;

public class PosePreview : MonoBehaviour
{
    public BoundingBox boundingBox;
    public BoundingCircle boundingCircle;
    public Keypoint[] keypoints;

    private readonly Vector3[] m_WorldPositions = new Vector3[33];
    private readonly Vector3[] m_SmoothedPositions = new Vector3[33];
    private readonly bool[] m_Visibility = new bool[33];

    private const float k_SmoothSpeed = 12f;

    public void SetActive(bool active)
    {
        gameObject.SetActive(active);
    }

    public void SetBoundingBox(bool active, Vector3 position, Vector2 size)
    {
        boundingBox.Set(active, position, size);
    }

    public void SetBoundingCircle(bool active, Vector3 position, float radius)
    {
        boundingCircle.Set(active, position, radius);
    }

    public void SetKeypoint(int index, bool active, Vector3 position)
    {
        m_Visibility[index] = active;

        if (active)
        {
            m_SmoothedPositions[index] = Vector3.Lerp(
                m_SmoothedPositions[index],
                position,
                Time.deltaTime * k_SmoothSpeed
            );
            m_WorldPositions[index] = m_SmoothedPositions[index];
        }

        keypoints[index].Set(active, m_WorldPositions[index]);
    }

    public bool IsKeypointVisible(int index) => m_Visibility[index];
    public Vector3 GetKeypointPosition(int index) => m_WorldPositions[index];
}