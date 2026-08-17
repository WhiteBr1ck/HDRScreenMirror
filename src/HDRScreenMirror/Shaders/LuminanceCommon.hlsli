float3 SrgbToLinear(float3 value)
{
    float3 low = value / 12.92;
    float3 high = pow(max((value + 0.055) / 1.055, 0.0), 2.4);
    return lerp(low, high, step(0.04045, value));
}

float PqEotf(float value)
{
    const float m1 = 2610.0 / 16384.0;
    const float m2 = 2523.0 / 32.0;
    const float c1 = 3424.0 / 4096.0;
    const float c2 = 2413.0 / 128.0;
    const float c3 = 2392.0 / 128.0;
    float p = pow(saturate(value), 1.0 / m2);
    float numerator = max(p - c1, 0.0);
    float denominator = max(c2 - c3 * p, 1e-6);
    return 10000.0 * pow(numerator / denominator, 1.0 / m1);
}

float3 Rec2020ToRec709(float3 value)
{
    return float3(
        1.660491 * value.r - 0.587641 * value.g - 0.072850 * value.b,
       -0.124550 * value.r + 1.132900 * value.g - 0.008349 * value.b,
       -0.018151 * value.r - 0.100579 * value.g + 1.118730 * value.b);
}

float3 DecodeInputToNits(float4 source, uint inputMode, float paperWhiteNits)
{
    if (inputMode == 0)
        return source.rgb * 80.0;

    if (inputMode == 2)
    {
        return float3(
            PqEotf(source.r),
            PqEotf(source.g),
            PqEotf(source.b));
    }

    return SrgbToLinear(saturate(source.rgb)) * paperWhiteNits;
}

float CalculateLuminanceNits(float3 rgbNits, uint inputMode)
{
    const float3 rec709Weights = float3(0.2126, 0.7152, 0.0722);
    const float3 rec2020Weights = float3(0.2627, 0.6780, 0.0593);
    return max(0.0, dot(rgbNits, inputMode == 2 ? rec2020Weights : rec709Weights));
}

float3 MapNitsToFalseColor(float nits)
{
    nits = max(nits, 0.0);
    if (nits <= 100.0)
    {
        float gray = nits / 100.0 * 0.25;
        return gray.xxx;
    }
    if (nits <= 203.0)
    {
        float t = (nits - 100.0) / 103.0;
        return float3(0.0, 1.0, 1.0 - t);
    }
    if (nits <= 400.0)
    {
        float t = (nits - 203.0) / 197.0;
        return float3(t, 1.0, 0.0);
    }
    if (nits <= 1000.0)
    {
        float t = (nits - 400.0) / 600.0;
        return float3(1.0, 1.0 - t, 0.0);
    }
    if (nits <= 2000.0)
    {
        float t = (nits - 1000.0) / 1000.0;
        return float3(1.0, 0.0, t);
    }
    if (nits < 4000.0)
    {
        float t = (nits - 2000.0) / 2000.0;
        return float3(1.0 - t, 0.0, 1.0);
    }
    return float3(1.0, 1.0, 1.0);
}
