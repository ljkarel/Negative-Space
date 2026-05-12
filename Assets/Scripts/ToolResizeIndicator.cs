using System.Collections;
using System.Collections.Generic;
using IVLab.MinVR3;
using UnityEngine;

public class ToolResizeIndicator : MonoBehaviour
{
    [SerializeField] private Vector2 _indicatorScaleMinMax = new Vector2(0.3f, 4f);
    [SerializeField] private Transform _brushCursor;
    [SerializeField] BrushResizerUI _brushResizerUI;
    [SerializeField] MainPaintingAndReframingUI _paintingUI;
    [SerializeField] VREventCallbackAny _handProximityClose;
    [SerializeField] VREventCallbackAny _handProximityFar;

    private GameObject _indicatorMesh;

    private void OnEnable()
    {
        _handProximityClose.StartListening();
        _handProximityFar.StartListening();
    }

    private void OnDisable()
    {
        _handProximityClose.StopListening();
        _handProximityFar.StopListening();
    }
    
    void Awake()
    {
        _indicatorMesh = transform.GetChild(0).gameObject;
        _handProximityClose.AddRuntimeListener(ShowIndicator);
        _handProximityFar.AddRuntimeListener(HideIndicator);
    }

    void ShowIndicator()
    {
        if (_paintingUI.IsWandMode) return;
        float newScale = _brushCursor.localScale.x;
        newScale = Mathf.Clamp(newScale, _indicatorScaleMinMax.x, _indicatorScaleMinMax.y);
        transform.localScale = Vector3.one * newScale;
        _indicatorMesh.SetActive(true);
    }

    void HideIndicator()
    {
        _indicatorMesh.SetActive(false);
    }
}
