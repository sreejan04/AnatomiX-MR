using UnityEngine;
using UnityEngine.XR;
using TMPro;
using System.Collections.Generic;

public class VoiceListeningUI : MonoBehaviour
{
    [Header("UI Reference")]
    public TextMeshProUGUI listeningText;
    public GameObject listeningPanel;

    [Header("Settings")]
    [Tooltip("Which controller button to use")]
    public bool useRightController = true;

    bool m_IsListening = false;
    bool m_ButtonWasPressed = false;

    // XR Input devices
    InputDevice m_Controller;

    void Start()
    {
        // Hide text at start
        if (listeningPanel != null)
            listeningPanel.SetActive(false);
        if (listeningText != null)
            listeningText.gameObject.SetActive(false);
    }

    void Update()
    {
        // Get controller
        GetController();

        // Read trigger button
        bool triggerPressed = false;
        m_Controller.TryGetFeatureValue(
            CommonUsages.triggerButton,
            out triggerPressed
        );

        // Toggle on button press (not hold)
        if (triggerPressed && !m_ButtonWasPressed)
        {
            ToggleListening();
        }

        m_ButtonWasPressed = triggerPressed;
        // Always face camera
        if (listeningText != null && listeningText.gameObject.activeSelf)
        {
            listeningText.transform.LookAt(Camera.main.transform);
            listeningText.transform.Rotate(0, 180, 0);
        }
    }

    void GetController()
    {
        if (m_Controller.isValid) return;

        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            useRightController
                ? InputDeviceCharacteristics.Right
                : InputDeviceCharacteristics.Left,
            devices
        );

        if (devices.Count > 0)
            m_Controller = devices[0];
    }

    void ToggleListening()
    {
        m_IsListening = !m_IsListening;

        if (listeningText != null)
            listeningText.gameObject.SetActive(m_IsListening);

        if (listeningPanel != null)
            listeningPanel.SetActive(m_IsListening);

        Debug.Log($"[Voice] Listening = {m_IsListening}");
    }
}