static const float GamutRelativeTolerance = 1e-4;

float3 Rec709ToDciP3(float3 value)
{
    return float3(
        0.822461962 * value.r + 0.177538037 * value.g,
        0.0331941992 * value.r + 0.966805815 * value.g,
        0.0170826315 * value.r + 0.0723974406 * value.g + 0.910519957 * value.b);
}

float3 Rec709ToRec2020(float3 value)
{
    return float3(
        0.627403914 * value.r + 0.329283028 * value.g + 0.0433130674 * value.b,
        0.0690972879 * value.r + 0.919540405 * value.g + 0.0113623151 * value.b,
        0.0163914393 * value.r + 0.0880133062 * value.g + 0.895595252 * value.b);
}

float3 Rec2020ToDciP3(float3 value)
{
    return float3(
         1.34357821 * value.r - 0.282179683 * value.g - 0.0613985806 * value.b,
        -0.0652974545 * value.r + 1.07578790 * value.g - 0.0104904631 * value.b,
         0.00282178726 * value.r - 0.0195984952 * value.g + 1.01677668 * value.b);
}

float3 DecodeLinearRgbForGamut(float4 source, uint inputMode)
{
    if (inputMode == 0)
        return source.rgb;
    if (inputMode == 2)
    {
        return float3(
            PqEotf(source.r),
            PqEotf(source.g),
            PqEotf(source.b)) / 10000.0;
    }
    return SrgbToLinear(saturate(source.rgb));
}

bool IsInsideGamut(float3 value)
{
    float magnitude = max(max(abs(value.r), abs(value.g)), abs(value.b));
    float tolerance = max(1e-7, magnitude * GamutRelativeTolerance);
    return all(value >= -tolerance);
}

uint ClassifyGamut(float3 linearRgb, uint inputMode)
{
    float3 rec709 = inputMode == 2 ? Rec2020ToRec709(linearRgb) : linearRgb;
    float3 dciP3 = inputMode == 2 ? Rec2020ToDciP3(linearRgb) : Rec709ToDciP3(linearRgb);
    float3 rec2020 = inputMode == 2 ? linearRgb : Rec709ToRec2020(linearRgb);

    return IsInsideGamut(rec709) ? 0 :
           IsInsideGamut(dciP3) ? 1 :
           IsInsideGamut(rec2020) ? 2 : 3;
}

float3 MapGamutCategoryToFalseColor(uint category)
{
    if (category == 0)
        return float3(0.278, 0.839, 1.0);
    if (category == 1)
        return float3(1.0, 0.835, 0.31);
    if (category == 2)
        return float3(0.898, 0.412, 1.0);
    return float3(1.0, 0.255, 0.20);
}
