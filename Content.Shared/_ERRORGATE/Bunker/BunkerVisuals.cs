using Robust.Shared.Serialization;

namespace Content.Shared._ERRORGATE.Bunker;

[Serializable, NetSerializable]
public enum BunkerHatchVisuals : byte
{
    Open,
}

[Serializable, NetSerializable]
public enum BunkerHatchVisualLayers : byte
{
    Base,
}

/// <summary>The hatch inside the vestibule: shown welded shut while it is locked.</summary>
[Serializable, NetSerializable]
public enum BunkerExitVisuals : byte
{
    Locked,
}

[Serializable, NetSerializable]
public enum BunkerExitVisualLayers : byte
{
    Base,
}
