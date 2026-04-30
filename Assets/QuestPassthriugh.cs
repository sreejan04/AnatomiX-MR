using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

public class QuestPassthrough : MonoBehaviour
{
    IEnumerator Start()
    {
        // Wait for XR to fully initialize
        yield return new WaitUntil(() =>
            XRGeneralSettings.Instance != null &&
            XRGeneralSettings.Instance.Manager.activeLoader != null);

        yield return new WaitForSeconds(0.5f);

        // Set camera to transparent so passthrough shows through
        Camera.main.clearFlags = CameraClearFlags.SolidColor;
        Camera.main.backgroundColor = new Color(0f, 0f, 0f, 0f);
        Camera.main.depth = -1;

        // Enable passthrough via Meta XR API
        EnablePassthrough();
    }

    void EnablePassthrough()
    {
        // Try Meta's OVR passthrough if available
        var passthroughLayer = FindObjectOfType<OVRPassthroughLayer>();
        if (passthroughLayer != null)
        {
            passthroughLayer.enabled = true;
            Debug.Log("[Passthrough] OVRPassthroughLayer enabled");
            return;
        }

        // Fallback: XR Display subsystem alpha
        var displays = new System.Collections.Generic.List<XRDisplaySubsystem>();
        SubsystemManager.GetSubsystems(displays);
        foreach (var d in displays)
        {
            d.SetPreferredMirrorBlitMode(XRMirrorViewBlitMode.LeftEye);
            Debug.Log("[Passthrough] XR display subsystem found: " + d);
        }
    }
}