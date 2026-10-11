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
