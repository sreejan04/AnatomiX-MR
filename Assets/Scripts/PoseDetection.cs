using System;
using Unity.Mathematics;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.UI;

public class PoseDetectionWebcam : MonoBehaviour
{
    public PosePreview posePreview;
    public RawImage webcamDisplay;

    public ModelAsset poseDetector;
    public ModelAsset poseLandmarker;
    public TextAsset anchorsCSV;

    public int cameraIndex = 0;
    public float scoreThreshold = 0.75f;

    const int k_NumAnchors = 2254;
    float[,] m_Anchors;

    const int k_NumKeypoints = 33;
    const int detectorInputSize = 224;
    const int landmarkerInputSize = 256;

    Worker m_PoseDetectorWorker;
    Worker m_PoseLandmarkerWorker;

    Tensor<float> m_DetectorInput;
    Tensor<float> m_LandmarkerInput;

    WebCamTexture webcam;
    Awaitable m_DetectAwaitable;

    float m_TextureWidth;
    float m_TextureHeight;

    async void Start()
    {
        // 📷 START WEBCAM
        webcam = new WebCamTexture(WebCamTexture.devices[cameraIndex].name, 640, 480);
        webcam.Play();
        webcamDisplay.texture = webcam;

        // 🔥 LOAD ANCHORS
        m_Anchors = BlazeUtils.LoadAnchors(anchorsCSV.text, k_NumAnchors);

        // 🔥 DETECTOR MODEL
        var poseDetectorModel = ModelLoader.Load(poseDetector);
        var graph = new FunctionalGraph();
        var input = graph.AddInput(poseDetectorModel, 0);
        var outputs = Functional.Forward(poseDetectorModel, input);

        var boxes = outputs[0];
        var scores = outputs[1];

        var filtered = BlazeUtils.ArgMaxFiltering(boxes, scores);
        poseDetectorModel = graph.Compile(filtered.Item1, filtered.Item2, filtered.Item3);

        m_PoseDetectorWorker = new Worker(poseDetectorModel, BackendType.GPUCompute);

        // 🔥 LANDMARK MODEL
        var poseLandmarkerModel = ModelLoader.Load(poseLandmarker);
        m_PoseLandmarkerWorker = new Worker(poseLandmarkerModel, BackendType.GPUCompute);

        m_DetectorInput = new Tensor<float>(new TensorShape(1, detectorInputSize, detectorInputSize, 3));
        m_LandmarkerInput = new Tensor<float>(new TensorShape(1, landmarkerInputSize, landmarkerInputSize, 3));

        // 🔁 MAIN LOOP
        while (true)
        {
            if (webcam.width < 100) continue;

            try
            {
                m_DetectAwaitable = Detect(webcam);
                await m_DetectAwaitable;
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    Vector3 ImageToWorld(Vector2 position)
    {
        return (position - 0.5f * new Vector2(m_TextureWidth, m_TextureHeight)) / m_TextureHeight;
    }

    async Awaitable Detect(Texture texture)
    {
        m_TextureWidth = texture.width;
        m_TextureHeight = texture.height;

        var size = Mathf.Max(texture.width, texture.height);

        float scale = size / (float)detectorInputSize;

        var M = BlazeUtils.mul(
            BlazeUtils.TranslationMatrix(0.5f * (new Vector2(texture.width, texture.height) + new Vector2(-size, size))),
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

        var landmarksT = (m_PoseLandmarkerWorker.PeekOutput("Identity") as Tensor<float>).ReadbackAndCloneAsync();
        using var landmarks = await landmarksT;

        for (int k = 0; k < k_NumKeypoints; k++)
        {
            float x = landmarks[5 * k + 0];
            float y = landmarks[5 * k + 1];
            float z = landmarks[5 * k + 2];
            float visibility = landmarks[5 * k + 3];
            float presence = landmarks[5 * k + 4];

            var posImg = BlazeUtils.mul(M2, new float2(x, y));

            Vector3 posWorld = ImageToWorld(posImg) + new Vector3(0, 0, z / m_TextureHeight);

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
    }
}