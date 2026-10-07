using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Events;

/// <summary>
/// Raised whenever a muzzle flash client-side entity needs to be spawned.
/// </summary>
[Serializable, NetSerializable]
public sealed class MuzzleFlashEvent : EntityEventArgs
{
    public NetEntity Uid;
    public string Prototype;

    public Angle Angle;

    /// <summary>
    /// ERRORGATE: radius of the light the shot casts, was always 2 in the base game.
    /// </summary>
    public float MuzzleEffectRadius;

    public MuzzleFlashEvent(NetEntity uid, string prototype, Angle angle, float muzzleEffectRadius = 2f)
    {
        Uid = uid;
        Prototype = prototype;
        Angle = angle;
        MuzzleEffectRadius = muzzleEffectRadius;
    }
}
