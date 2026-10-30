using UnityEngine;

// Shared approved v5 presentation clock. No gameplay or score dependencies.
public static class BoardVictoryTiming
{
    public const float LightStart = 1.13f;
    public const float RippleStretch = 1.15f;
    public const float LifeStart = LightStart + (.51f + .022f + .19f) * RippleStretch + .025f;
    public const float Duration = LifeStart + .82f;
    public static float LightTime(float time) => (time - LightStart) / RippleStretch;
    public static float Arrival(float diagonal) => Mathf.Clamp01(diagonal) * .51f;
    private static float Spring(float t, float frequency, float damping)
    {
        if (t <= 0f) return 0f;
        if (t >= .72f) return 1f;
        float q = Mathf.Sqrt(1f - damping * damping), wd = frequency * q;
        return 1f - Mathf.Exp(-damping * frequency * t) *
            (Mathf.Cos(wd * t) + damping / q * Mathf.Sin(wd * t));
    }
    public static bool Hidden(float time) => time >= .06f && time < .14f;
    public static bool Wink(float time) => time >= .46f && time < .79f;
    public static float AvatarScale(float time)
    {
        if (time < .06f) return 1f;
        if (time < .14f) return 0f;
        if (time < .73f) return .86f + .43f * Spring(time - .14f, 29f, .66f);
        if (time >= 1.13f) return 1f;
        return 1f + .29f * (1f - Spring(time - .73f, 24f, .73f));
    }
    public static float Tilt(float time)
    {
        if (time < .32f || time >= 1.13f) return 0f;
        if (time < .76f) return 9.5f * Spring(time - .32f, 26f, .66f);
        return 9.5f * (1f - Spring(time - .76f, 28f, .68f));
    }
    public static float CellScale(float diagonal, float lightTime)
    {
        float age = lightTime - Arrival(diagonal) - .022f;
        if (age < 0f || age > .19f) return 1f;
        float wave = Mathf.Sin(Mathf.PI * age / .19f);
        return 1f + .045f * wave * wave;   // v3: a touch livelier than the original .035
    }
}
