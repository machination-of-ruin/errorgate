using Robust.Shared.Serialization;

namespace Content.Shared._ERRORGATE.Bunker;

[Serializable, NetSerializable]
public enum BunkerHatchVisuals : byte
{
    /// <summary>Red lights while the hatch is locked, blue while it can be used.</summary>
    Locked,
}

[Serializable, NetSerializable]
public enum BunkerHatchVisualLayers : byte
{
    Lights,
}

/// <summary>The hatch inside the vestibule: red lights while it is locked, blue while it can be used.</summary>
[Serializable, NetSerializable]
public enum BunkerExitVisuals : byte
{
    Locked,
}

[Serializable, NetSerializable]
public enum BunkerExitVisualLayers : byte
{
    Lights,
}
