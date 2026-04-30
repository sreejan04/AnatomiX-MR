using System;
using Unity.Mathematics;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Android;

public class VRPoseDetectionXR : MonoBehaviour
{

    [Header("Models")]
    public ModelAsset poseDetector;
    public ModelAsset poseLandmarker;
    public TextAsset anchorsCSV;

    [Header("Scene References")]
    public PosePreview posePreview;
    public Transform headsetTransform;

    [Header("Tuning")]
    public float scoreThreshold = 0.75f;
    public float detectionDistance = 2.0f;
    public int frameSkipInterval = 3;
    [Tooltip("Flip if skeleton appears upside down")]
    public bool flipY = false;
    public float verticalOffset = -0.4f;

    const int k_NumAnchors = 2254;
    const int k_NumKeypoints = 33;
    const int detectorInputSize = 224;
    const int landmarkerInputSize = 256;

    float[,] m_Anchors;
    Worker m_PoseDetectorWorker;
    Worker m_PoseLandmarkerWorker;
    Tensor<float> m_DetectorInput;
    Tensor<float> m_LandmarkerInput;
    WebCamTexture m_Webcam;
    Awaitable m_DetectAwaitable;
    float m_TextureWidth;
    float m_TextureHeight;
    int m_FrameCount;
    bool m_Initialized = false;

    async void Start()
    {
        Debug.Log("[PoseVR] ===== START =====");

        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            Permission.RequestUserPermission(Permission.Camera);
            await Awaitable.WaitForSecondsAsync(2f);
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        Debug.Log($"[PoseVR] Found {devices.Length} cameras");

        if (devices.Length == 0)
        {
            Debug.LogError("[PoseVR] No cameras found!");
            return;
        }

        for (int d = 0; d < devices.Length; d++)
            Debug.Log($"[PoseVR] Cam {d}: {devices[d].name} front={devices[d].isFrontFacing}");

        int camIdx = 0;
        for (int d = 0; d < devices.Length; d++)
            if (!devices[d].isFrontFacing) { camIdx = d; break; }

        m_Webcam = new WebCamTexture(devices[camIdx].name, 640, 480, 30);
        m_Webcam.Play();
        Debug.Log($"[PoseVR] Camera started: {devices[camIdx].name}");

        if (headsetTransform == null)
            headsetTransform = Camera.main.transform;

        try
        {
            m_Anchors = BlazeUtils.LoadAnchors(anchorsCSV.text, k_NumAnchors);
            Debug.Log("[PoseVR] Anchors loaded");

            var poseDetectorModel = ModelLoader.Load(poseDetector);
            var graph = new FunctionalGraph();
            var inp = graph.AddInput(poseDetectorModel, 0);
            var outs = Functional.Forward(poseDetectorModel, inp);
            var filtered = BlazeUtils.ArgMaxFiltering(outs[0], outs[1]);
            poseDetectorModel = graph.Compile(filtered.Item1, filtered.Item2, filtered.Item3);
            m_PoseDetectorWorker = new Worker(poseDetectorModel, BackendType.GPUCompute);
            Debug.Log("[PoseVR] Detector loaded");

            var poseLandmarkerModel = ModelLoader.Load(poseLandmarker);
            m_PoseLandmarkerWorker = new Worker(poseLandmarkerModel, BackendType.GPUCompute);
            Debug.Log("[PoseVR] Landmarker loaded");

            m_DetectorInput = new Tensor<float>(new TensorShape(1, detectorInputSize, detectorInputSize, 3));
            m_LandmarkerInput = new Tensor<float>(new TensorShape(1, landmarkerInputSize, landmarkerInputSize, 3));

            m_Initialized = true;
            Debug.Log("[PoseVR] Init complete — starting loop");
        }
        catch (Exception e)
        {
            Debug.LogError($"[PoseVR] Init FAILED: {e.Message}\n{e.StackTrace}");
            return;
        }

        while (true)
        {
            if (m_Webcam.width < 100)
            {
                await Awaitable.NextFrameAsync();
                continue;
            }

            m_FrameCount++;
            if (m_FrameCount % frameSkipInterval != 0)
            {
                await Awaitable.NextFrameAsync();
                continue;
            }

            try
            {
                m_DetectAwaitable = Detect(m_Webcam);
                await m_DetectAwaitable;
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                Debug.LogError($"[PoseVR] Detect error: {e.Message}");
                await Awaitable.NextFrameAsync();
            }
        }
    }

    Vector3 ImageToWorld(Vector2 imgPos, float depthOffset = 0f)
    {
        float nx = (imgPos.x / m_TextureWidth) - 0.5f;
        float ny = (imgPos.y / m_TextureHeight) - 0.5f;

        if (flipY) ny = -ny;

        float spread = detectionDistance * 1.1f;

        return headsetTransform.position
             + headsetTransform.forward * (detectionDistance + depthOffset)
             + headsetTransform.right * (nx * spread)
             + headsetTransform.up * (ny * spread+verticalOffset);
    }

    async Awaitable Detect(Texture texture)
    {
        m_TextureWidth = texture.width;
        m_TextureHeight = texture.height;

        float size = Mathf.Max(texture.width, texture.height);
        float scale = size / (float)detectorInputSize;

        var M = BlazeUtils.mul(
            BlazeUtils.TranslationMatrix(0.5f * (new Vector2(texture.width, texture.height)
                                                + new Vector2(-size, size))),
            BlazeUtils.ScaleMatrix(new Vector2(scale, -scale))
        );

        BlazeUtils.SampleImageAffine(texture, m_DetectorInput, M);
        m_PoseDetectorWorker.Schedule(m_DetectorInput);

        var idxT = (m_PoseDetectorWorker.PeekOutput(0) as Tensor<int>).ReadbackAndCloneAsync();
        var scoreT = (m_PoseDetectorWorker.PeekOutput(1) as Tensor<float>).ReadbackAndCloneAsync();
        var boxT = (m_PoseDetectorWorker.PeekOutput(2) as Tensor<float>).ReadbackAndCloneAsync();

        using var idx = await idxT;
        using var score = await scoreT;
        using var box = await boxT;

        if (score[0] < scoreThreshold)
        {
            posePreview.SetActive(false);
            return;
        }

        posePreview.SetActive(true);

        int i = idx[0];
        var anchor = detectorInputSize * new float2(m_Anchors[i, 0], m_Anchors[i, 1]);
        var kp1 = BlazeUtils.mul(M, anchor + new float2(box[0, 0, 4], box[0, 0, 5]));
        var kp2 = BlazeUtils.mul(M, anchor + new float2(box[0, 0, 6], box[0, 0, 7]));

        var delta = kp2 - kp1;
        float radius = 1.25f * math.length(delta);
        float theta = math.atan2(delta.y, delta.x);

        var origin = new float2(0.5f * landmarkerInputSize, 0.5f * landmarkerInputSize);
        float scale2 = radius / (0.5f * landmarkerInputSize);

        var M2 = BlazeUtils.mul(
            BlazeUtils.mul(
                BlazeUtils.mul(
                    BlazeUtils.TranslationMatrix(kp1),
                    BlazeUtils.ScaleMatrix(new float2(scale2, -scale2))
                ),
                BlazeUtils.RotationMatrix(0.5f * Mathf.PI - theta)
            ),
            BlazeUtils.TranslationMatrix(-origin)
        );

        BlazeUtils.SampleImageAffine(texture, m_LandmarkerInput, M2);
        m_PoseLandmarkerWorker.Schedule(m_LandmarkerInput);

        var landmarksT = (m_PoseLandmarkerWorker.PeekOutput("Identity") as Tensor<float>)
                         .ReadbackAndCloneAsync();
        using var landmarks = await landmarksT;

        for (int k = 0; k < k_NumKeypoints; k++)
        {
            float x = landmarks[5 * k + 0];
            float y = landmarks[5 * k + 1];
            float z = landmarks[5 * k + 2];
            float visibility = landmarks[5 * k + 3];
            float presence = landmarks[5 * k + 4];

            var posImg = BlazeUtils.mul(M2, new float2(x, y));
            float depthOff = z / m_TextureHeight * 0.4f;
            Vector3 posWorld = ImageToWorld(posImg, depthOff);

            posePreview.SetKeypoint(k, visibility > 0.5f && presence > 0.5f, posWorld);
        }
    }

    void OnDestroy()
    {
        m_DetectAwaitable?.Cancel();
        m_PoseDetectorWorker?.Dispose();
        m_PoseLandmarkerWorker?.Dispose();
        m_DetectorInput?.Dispose();
        m_LandmarkerInput?.Dispose();
        if (m_Webcam != null && m_Webcam.isPlaying) m_Webcam.Stop();
    }
}