using UnityEngine;
using UnityEngine.Profiling;

public class FPSDisplay : MonoBehaviour
{
    private float deltaTime;
    private float fps;
    private float frameTimeMs;

    private GUIStyle style;
    private Rect rect;

    void Awake()
    {
        Application.targetFrameRate = 120;
        QualitySettings.vSyncCount = 0;

        rect = new Rect(10, 10, 400, 200);

        style = new GUIStyle();
        style.fontSize = 32;
        style.normal.textColor = Color.white;
    }

    void Update()
    {
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;

        fps = 1.0f / deltaTime;
        frameTimeMs = deltaTime * 1000.0f;
    }

    void OnGUI()
    {
        // Background box
        GUI.Box(new Rect(5, 5, 450, 180), "");

        // Color based on FPS
        if (fps >= 60)
            style.normal.textColor = Color.green;
        else if (fps >= 30)
            style.normal.textColor = Color.yellow;
        else
            style.normal.textColor = Color.red;

        // Memory Info
        float totalMemory = Profiler.GetTotalAllocatedMemoryLong() / (1024f * 1024f);
        float reservedMemory = Profiler.GetTotalReservedMemoryLong() / (1024f * 1024f);
        float monoMemory = Profiler.GetMonoUsedSizeLong() / (1024f * 1024f);

        string debugText =
            $"FPS: {Mathf.RoundToInt(fps)}\n" +
            $"Frame Time: {frameTimeMs:F1} ms\n" +
            $"Allocated: {totalMemory:F0} MB\n" +
            $"Reserved: {reservedMemory:F0} MB\n" +
            $"Mono: {monoMemory:F0} MB";

        GUI.Label(rect, debugText, style);
    }
}