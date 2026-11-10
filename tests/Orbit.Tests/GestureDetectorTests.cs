using Orbit.Core.Input;

namespace Orbit.Tests;

public class GestureDetectorTests
{
    private static GestureDetector Create(out List<string> fired)
    {
        var detector = new GestureDetector { SystemDragThreshold = 4 };
        var events = new List<string>();
        fired = events;

        detector.Configure(TriggerGesture.Defaults());
        detector.DragStarted += (_, x, y) => events.Add($"drag:{x},{y}");
        detector.DragEnded += (x, y) => events.Add($"end:{x},{y}");
        detector.HoldTriggered += (_, _, _) => events.Add("hold");
        detector.KeyChordTriggered += (_, x, y) => events.Add($"key:{x},{y}");
        return detector;
    }

    private const ModifierKeys TriggerMods = ModifierKeys.Ctrl | ModifierKeys.Alt;

    [Fact]
    public void Drag_PastThreshold_Fires()
    {
        var detector = Create(out var fired);

        detector.MouseDown(TriggerMouseButton.Left, 100, 100, TriggerMods);
        detector.MouseMove(103, 100, TriggerMods, TriggerMouseButton.Left);
        Assert.Empty(fired); // 3px 还没过阈值

        detector.MouseMove(110, 100, TriggerMods, TriggerMouseButton.Left);
        Assert.Equal(["drag:110,100"], fired);
    }

    [Fact]
    public void Drag_ThenRelease_Ends()
    {
        var detector = Create(out var fired);

        detector.MouseDown(TriggerMouseButton.Left, 100, 100, TriggerMods);
        detector.MouseMove(120, 100, TriggerMods, TriggerMouseButton.Left);
        detector.MouseUp(TriggerMouseButton.Left, 120, 100);

        Assert.Equal(["drag:120,100", "end:120,100"], fired);
    }

    [Fact]
    public void Drag_StartsOnlyOncePerPress()
    {
        var detector = Create(out var fired);

        detector.MouseDown(TriggerMouseButton.Left, 100, 100, TriggerMods);
        detector.MouseMove(130, 100, TriggerMods, TriggerMouseButton.Left);
        detector.MouseMove(150, 100, TriggerMods, TriggerMouseButton.Left);
        detector.MouseMove(170, 100, TriggerMods, TriggerMouseButton.Left);

        Assert.Single(fired);
    }

    [Fact]
    public void Drag_WrongModifiers_DoesNotFire()
    {
        var detector = Create(out var fired);

        detector.MouseDown(TriggerMouseButton.Left, 100, 100, ModifierKeys.Ctrl);
        detector.MouseMove(200, 100, ModifierKeys.Ctrl, TriggerMouseButton.Left);

        Assert.Empty(fired);
    }

    [Fact]
    public void Drag_WrongButton_DoesNotFire()
    {
        var detector = Create(out var fired);

        detector.MouseDown(TriggerMouseButton.Right, 100, 100, TriggerMods);
        detector.MouseMove(200, 100, TriggerMods, TriggerMouseButton.Right);

        Assert.Empty(fired);
    }

    [Fact]
    public void LateModifier_StartsDragWithoutPriorArming()
    {
        // 真实场景：先把文件拖起来，再按 Ctrl+Alt。
        var detector = Create(out var fired);

        detector.MouseDown(TriggerMouseButton.Left, 100, 100, ModifierKeys.None);
        detector.MouseMove(160, 100, TriggerMods, TriggerMouseButton.Left);

        Assert.Equal(["drag:160,100"], fired);
    }

    [Fact]
    public void LateModifier_OnlyOncePerPress()
    {
        var detector = Create(out var fired);

        detector.MouseDown(TriggerMouseButton.Left, 100, 100, ModifierKeys.None);
        detector.MouseMove(160, 100, TriggerMods, TriggerMouseButton.Left);
        detector.MouseMove(170, 100, TriggerMods, TriggerMouseButton.Left);

        Assert.Single(fired);
    }

    [Fact]
    public void Hold_WithoutMovement_Fires()
    {
        var detector = new GestureDetector { SystemDragThreshold = 4 };
        var events = new List<string>();
        long token = 0;

        detector.Configure([
            new TriggerGesture
            {
                Kind = TriggerKind.Hold,
                Modifiers = TriggerMods,
                Button = TriggerMouseButton.Right,
                HoldMilliseconds = 450,
            },
        ]);
        detector.HoldTimerRequested += (t, _) => token = t;
        detector.HoldTriggered += (_, _, _) => events.Add("hold");

        detector.MouseDown(TriggerMouseButton.Right, 50, 50, TriggerMods);
        Assert.NotEqual(0, token);

        detector.HoldElapsed(token);
        Assert.Equal(["hold"], events);
    }

    [Fact]
    public void Hold_IsCancelledByMovement()
    {
        var detector = new GestureDetector { SystemDragThreshold = 4 };
        var events = new List<string>();
        long token = 0;

        detector.Configure([
            new TriggerGesture { Kind = TriggerKind.Hold, Modifiers = TriggerMods, Button = TriggerMouseButton.Left },
        ]);
        detector.HoldTimerRequested += (t, _) => token = t;
        detector.HoldTriggered += (_, _, _) => events.Add("hold");

        detector.MouseDown(TriggerMouseButton.Left, 50, 50, TriggerMods);
        detector.MouseMove(80, 50, TriggerMods, TriggerMouseButton.Left);
        detector.HoldElapsed(token);

        Assert.Empty(events);
    }

    [Fact]
    public void KeyChord_ExactModifiers_Fires()
    {
        var detector = Create(out var fired);

        detector.MouseMove(12, 34, ModifierKeys.None);
        detector.KeyDown("Space", TriggerMods);

        Assert.Equal(["key:12,34"], fired);
    }

    [Fact]
    public void KeyChord_ExtraModifier_DoesNotFire()
    {
        var detector = Create(out var fired);

        detector.KeyDown("Space", ModifierKeys.Ctrl | ModifierKeys.Alt | ModifierKeys.Shift);

        Assert.Empty(fired);
    }

    [Fact]
    public void KeyChord_WrongKey_DoesNotFire()
    {
        var detector = Create(out var fired);

        detector.KeyDown("Enter", TriggerMods);

        Assert.Empty(fired);
    }

    [Fact]
    public void Paused_FiresNothing()
    {
        var detector = Create(out var fired);
        detector.Paused = true;

        detector.MouseDown(TriggerMouseButton.Left, 100, 100, TriggerMods);
        detector.MouseMove(200, 100, TriggerMods, TriggerMouseButton.Left);
        detector.KeyDown("Space", TriggerMods);

        Assert.Empty(fired);
    }

    [Fact]
    public void DisabledGesture_IsIgnored()
    {
        var detector = new GestureDetector();
        var events = new List<string>();
        detector.Configure([
            new TriggerGesture { Kind = TriggerKind.Drag, Modifiers = TriggerMods, Button = TriggerMouseButton.Left, Enabled = false },
        ]);
        detector.DragStarted += (_, _, _) => events.Add("drag");

        detector.MouseDown(TriggerMouseButton.Left, 0, 0, TriggerMods);
        detector.MouseMove(100, 0, TriggerMods, TriggerMouseButton.Left);

        Assert.Empty(events);
    }
}
