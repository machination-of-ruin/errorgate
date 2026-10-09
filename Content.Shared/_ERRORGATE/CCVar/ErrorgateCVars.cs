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
}
