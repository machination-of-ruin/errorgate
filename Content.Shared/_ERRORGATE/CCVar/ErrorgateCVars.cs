using Robust.Shared.Configuration;

namespace Content.Shared._ERRORGATE.CCVar;

[CVarDefs]
public sealed class ErrorgateCVars
{
    /// <summary>
    ///     Immersive interactions: worn items cannot be used unless they allow it, and items inside containers cannot be
    ///     interacted with directly. Replicated because the checks are predicted by the client.
    /// </summary>
    public static readonly CVarDef<bool> ImmersiveInteractions =
        CVarDef.Create("errorgate.immersive_interactions", true, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    ///     Seconds a dead player has to wait in the death void before they can respawn. 0 means immediately.
    ///     Admins using <c>forcerespawn</c> ignore it.
    /// </summary>
    public static readonly CVarDef<float> RespawnCooldown =
        CVarDef.Create("errorgate.respawn_cooldown", 0f, CVar.SERVER | CVar.ARCHIVE);

    /// <summary>
    ///     Players beyond normal hearing range hear a muffled, echoing copy of gunshots on the same grid.
    /// </summary>
    public static readonly CVarDef<bool> DistantGunfireEnabled =
        CVarDef.Create("errorgate.distant_gunfire_enabled", true, CVar.SERVER | CVar.ARCHIVE);

    /// <summary>
    ///     Multiplier for how far distant gunfire carries beyond the normal hearing range (twice the PVS range).
    ///     1 uses the per-caliber ranges as authored, the default 0.7 trims them.
    /// </summary>
    public static readonly CVarDef<float> DistantGunfireRangeScale =
        CVarDef.Create("errorgate.distant_gunfire_range_scale", 0.7f, CVar.SERVER | CVar.ARCHIVE);
}
