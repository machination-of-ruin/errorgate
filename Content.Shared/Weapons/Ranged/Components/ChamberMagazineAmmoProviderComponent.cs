using Robust.Shared.Audio;

namespace Content.Shared.Weapons.Ranged.Components;

/// <summary>
/// Chamber + mags in one package. If you need just magazine then use <see cref="MagazineAmmoProviderComponent"/>
/// </summary>
[RegisterComponent, AutoGenerateComponentState]
public sealed partial class ChamberMagazineAmmoProviderComponent : MagazineAmmoProviderComponent
{
    /// <summary>
    /// If the gun has a bolt and whether that bolt is closed. Firing is impossible
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("boltClosed"), AutoNetworkedField]
    public bool? BoltClosed = true;

    /// <summary>
    /// Does the gun automatically open and close the bolt upon shooting.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("autoCycle"), AutoNetworkedField]
    public bool AutoCycle = true;

    /// <summary>
    /// If true, the gun will lock
    /// the bold open if the mag is empty.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("boltCatch"), AutoNetworkedField]
    public bool BoltCatch = true;

    [ViewVariables(VVAccess.ReadWrite), DataField("soundBoltClosed"), AutoNetworkedField]
    public SoundSpecifier? BoltClosedSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Bolt/rifle_bolt_closed.ogg");

    [ViewVariables(VVAccess.ReadWrite), DataField("soundBoltOpened"), AutoNetworkedField]
    public SoundSpecifier? BoltOpenedSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Bolt/rifle_bolt_open.ogg");

    [ViewVariables(VVAccess.ReadWrite), DataField("soundRack"), AutoNetworkedField]
    public SoundSpecifier? RackSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Cock/ltrifle_cock.ogg");
}
