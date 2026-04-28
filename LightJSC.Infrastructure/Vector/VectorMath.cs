using System.Numerics;

namespace LightJSC.Infrastructure.Vector;

public static class VectorMath
{
    public static void NormalizeInPlace(float[] vector)
    {
        if (vector.Length == 0)
        {
            return;
        }

        var sum = 0f;
        for (var i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }

        if (sum <= 0f)
        {
            return;
        }

        var inv = 1f / MathF.Sqrt(sum);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] *= inv;
        }
    }

    public static float DotProduct(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length != right.Length)
        {
            return 0f;
        }

        var vectorWidth = Vector<float>.Count;
        var sum = Vector<float>.Zero;
        var i = 0;
        for (; i <= left.Length - vectorWidth; i += vectorWidth)
        {
            var v1 = new Vector<float>(left.Slice(i, vectorWidth));
            var v2 = new Vector<float>(right.Slice(i, vectorWidth));
            sum += v1 * v2;
        }

        var total = 0f;
        for (var j = 0; j < vectorWidth; j++)
        {
            total += sum[j];
        }

        for (; i < left.Length; i++)
        {
            total += left[i] * right[i];
        }

        return total;
    }
}

