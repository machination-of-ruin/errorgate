using System.Numerics;
using Content.Client.Movement.Components;
using Content.Shared.Camera;
using Content.Shared.Input;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Map;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Content.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;


namespace Content.Client.Movement.Systems;

public sealed partial class EyeCursorOffsetSystem : EntitySystem
{
    [Dependency] private readonly IEyeManager _eyeManager = default!;
    [Dependency] private readonly IInputManager _inputManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContentEyeSystem _contentEye = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!; // ERRORGATE

    // This value is here to make sure the user doesn't have to move their mouse
    // all the way out to the edge of the screen to get the full offset.
    static private float _edgeOffset = 0.9f;

    private static bool _toggled; // WD EDIT

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EyeCursorOffsetComponent, GetEyeOffsetEvent>(OnGetEyeOffsetEvent);

        // WD EDIT START
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.LookUp, new EyeOffsetInputCmdHandler())
            .Register<EyeCursorOffsetSystem>();
        // WD EDIT END
    }

    private void OnGetEyeOffsetEvent(EntityUid uid, EyeCursorOffsetComponent component, ref GetEyeOffsetEvent args)
    {
        var offset = OffsetAfterMouse(uid, component);
        if (offset == null)
            return;

        args.Offset += offset.Value;
    }

    public Vector2? OffsetAfterMouse(EntityUid uid, EyeCursorOffsetComponent? component)
    {
        // WD EDIT START
        if (!_toggled)
            return null;
        // WD EDIT END

        var localPlayer = _player.LocalPlayer?.ControlledEntity;
        var mousePos = _inputManager.MouseScreenPosition;
        var screenSize = _clyde.MainWindow.Size;

        // ERRORGATE: the neutral point is the middle of the game view, where the player is drawn, not the middle of the
        // window. With the chat panel next to the view the two are about 2 tiles apart, which made the view jump that far
        // as soon as look far was held and made "straight above the player" a cursor position that aimed to the side.
        var center = new Vector2(screenSize.X / 2f, screenSize.Y / 2f);
        var half = MathF.Min(screenSize.X / 2f, screenSize.Y / 2f);
        if (_ui.ActiveScreen?.GetWidget<MainViewport>() is { } viewport && viewport.PixelSize.X > 0 && viewport.PixelSize.Y > 0)
        {
            var size = (Vector2) viewport.PixelSize;
            center = (Vector2) viewport.GlobalPixelPosition + size / 2f;
            half = MathF.Min(size.X, size.Y) / 2f;
        }

        var minValue = half * _edgeOffset;

        var mouseNormalizedPos = new Vector2(-(mousePos.X - center.X) / minValue, (mousePos.Y - center.Y) / minValue); // X needs to be inverted here for some reason, otherwise it ends up flipped.

        if (localPlayer == null)
            return null;

        var playerPos = _transform.GetWorldPosition(localPlayer.Value);

        if (component == null)
        {
            component = EnsureComp<EyeCursorOffsetComponent>(uid);
        }

        // Doesn't move the offset if the mouse has left the game window!
        if (mousePos.Window != WindowId.Invalid)
        {
            // The offset must account for the in-world rotation.
            var eyeRotation = _eyeManager.CurrentEye.Rotation;
            var mouseActualRelativePos = Vector2.Transform(mouseNormalizedPos, System.Numerics.Quaternion.CreateFromAxisAngle(-System.Numerics.Vector3.UnitZ, (float)(eyeRotation.Opposite().Theta))); // I don't know, it just works.

            // Caps the offset into a circle around the player.
            mouseActualRelativePos *= component.MaxOffset;
            if (mouseActualRelativePos.Length() > component.MaxOffset)
            {
                mouseActualRelativePos = mouseActualRelativePos.Normalized() * component.MaxOffset;
            }

            component.TargetPosition = mouseActualRelativePos;

            //Makes the view not jump immediately when moving the cursor fast.
            if (component.CurrentPosition != component.TargetPosition)
            {
                Vector2 vectorOffset = component.TargetPosition - component.CurrentPosition;
                if (vectorOffset.Length() > component.OffsetSpeed)
                {
                    vectorOffset = vectorOffset.Normalized() * component.OffsetSpeed;
                }
                component.CurrentPosition += vectorOffset;
            }
        }
        return component.CurrentPosition;
    }

    // WD EDIT START
    private sealed class EyeOffsetInputCmdHandler : InputCmdHandler
    {
        public override bool HandleCmdMessage(
            IEntityManager entManager,
            ICommonSession? session,
            IFullInputCmdMessage message
        )
        {
            if (session?.AttachedEntity == null)
                return false;

            _toggled = message.State == BoundKeyState.Down;
            return false;
        }
    }
    // WD EDIT END
}
