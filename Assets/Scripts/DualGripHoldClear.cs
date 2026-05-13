using UnityEngine;
using IVLab.MinVR3;

public class DualGripHoldClear : MonoBehaviour
{
    [SerializeField] private VREventCallbackAny m_LeftGripEvent;
    [SerializeField] private VREventCallbackAny m_RightGripEvent;
    [SerializeField] private MainPaintingAndReframingUI m_PaintingUI;
    [SerializeField] private float m_HoldDuration = 3f;
    [SerializeField] private Renderer m_VignetteRenderer;

    private const float k_GripThreshold = 0.5f;
    private static readonly int k_IntensityId = Shader.PropertyToID("_Intensity");

    public bool BothGripsHeld => m_LeftGrip > k_GripThreshold && m_RightGrip > k_GripThreshold;

    private float m_LeftGrip;
    private float m_RightGrip;
    private float m_HeldTime;
    private bool m_WaitingForRelease;
    private Material m_VignetteMat;

    private void Awake()
    {
        m_LeftGripEvent.AddRuntimeListener<float>(v  => m_LeftGrip  = v);
        m_RightGripEvent.AddRuntimeListener<float>(v => m_RightGrip = v);

        if (m_VignetteRenderer)
        {
            m_VignetteMat = m_VignetteRenderer.material; // creates instance
            SetVignetteIntensity(0f);
        }
    }

    private void OnEnable()  { m_LeftGripEvent.StartListening();  m_RightGripEvent.StartListening(); }
    private void OnDisable() { m_LeftGripEvent.StopListening();   m_RightGripEvent.StopListening(); }

    private void Update()
    {
        if (m_WaitingForRelease)
        {
            if (!BothGripsHeld) m_WaitingForRelease = false;
            return;
        }

        if (BothGripsHeld)
        {
            m_HeldTime += Time.deltaTime;
            SetVignetteIntensity(Mathf.Sqrt(m_HeldTime / m_HoldDuration));
            if (m_HeldTime >= m_HoldDuration)
            {
                m_PaintingUI.ClearArtworkUndoable();
                m_HeldTime = 0f;
                m_WaitingForRelease = true;
                SetVignetteIntensity(0f);
            }
        }
        else
        {
            m_HeldTime = 0f;
            SetVignetteIntensity(0f);
        }
    }

    private void SetVignetteIntensity(float t)
    {
        if (m_VignetteMat)
            m_VignetteMat.SetFloat(k_IntensityId, t);
    }
}
