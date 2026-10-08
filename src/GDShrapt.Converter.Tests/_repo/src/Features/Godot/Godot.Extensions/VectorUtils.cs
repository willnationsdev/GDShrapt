namespace Godot.Extensions.Math;

public partial class VectorUtils : RefCounted
{
    public static Vector2I Round2I(Vector2 value)
    {
#if GODOT_REAL_T_IS_DOUBLE
        return new(Math.Round(value.X), Math.Round(value.Y));
#else 
        return new(Mathf.RoundToInt(value.X), Mathf.RoundToInt(value.Y));
#endif
    }

    public static Vector3I Round3I(Vector3 value)
    {
#if GODOT_REAL_T_IS_DOUBLE
        return new(Math.Round(value.X), Math.Round(value.Y), Math.Round(value.Z));
#else 
        return new(Mathf.RoundToInt(value.X), Mathf.RoundToInt(value.Y), Mathf.RoundToInt(value.Z));
#endif
    }
}
