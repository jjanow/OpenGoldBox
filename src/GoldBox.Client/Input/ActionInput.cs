using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client.Input;

/// <summary>A mouse click in screen pixels of the 320×200 frame.</summary>
public readonly record struct ScreenClick(int X, int Y, bool Right);

/// <summary>
/// The one action layer: reads the keyboard, the pad and the mouse each frame and turns them into
/// <see cref="GameAction"/>s through <see cref="InputBindings"/>. Mouse clicks become actions through the screen that
/// was clicked (<see cref="Inject"/>). Remembers the device used last, for the prompts.
/// </summary>
public sealed class ActionInput(InputBindings bindings)
{
    /// <summary>Held movement repeats like the DOS keyboard's typematic: first repeat after this, then every <see cref="RepeatInterval"/>.</summary>
    public static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400), RepeatInterval = TimeSpan.FromMilliseconds(180);

    private static readonly GameAction[] Repeating =
        [GameAction.Forward, GameAction.TurnLeft, GameAction.TurnRight, GameAction.TurnAround, GameAction.MenuUp, GameAction.MenuDown, GameAction.MenuLeft, GameAction.MenuRight,
         GameAction.StepUpLeft, GameAction.StepUpRight, GameAction.StepDownLeft, GameAction.StepDownRight];

    private static readonly Buttons[] AllButtons = Enum.GetValues<Buttons>();

    private KeyboardState _prevKeys;
    private GamePadState _prevPad;
    private MouseState _prevMouse;
    private readonly HashSet<GameAction> _pressed = [];
    private readonly Dictionary<GameAction, TimeSpan> _heldSince = [];
    private readonly Dictionary<GameAction, TimeSpan> _nextRepeat = [];
    private readonly List<ScreenClick> _clicks = [];
    private (InputDevice Device, string Name)? _newInput;

    public InputBindings Bindings { get; set; } = bindings;

    /// <summary>The device of the last key, button, stick push or click (prompts follow it). Starts on the keyboard.</summary>
    public InputDevice LastDevice { get; private set; } = InputDevice.Keyboard;

    /// <summary>Mouse position in frame pixels (null when outside the frame).</summary>
    public Point? MousePosition { get; private set; }

    /// <summary>This frame's clicks, in frame pixels.</summary>
    public IReadOnlyList<ScreenClick> Clicks => _clicks;

    /// <summary>The first new key or pad button this frame, by binding name (for rebinding).</summary>
    public (InputDevice Device, string Name)? NewInput => _newInput;

    public bool Pressed(GameAction a) => _pressed.Contains(a);

    /// <summary>The action's key or button is down this frame (pressed now or earlier and still held).</summary>
    public bool Held(GameAction a) => _heldSince.ContainsKey(a);

    /// <summary>An action caused by the mouse (a click a screen turned into an action).</summary>
    public void Inject(GameAction a)
    {
        _pressed.Add(a);
        _injected = true;
        LastDevice = InputDevice.Mouse;
    }

    /// <param name="toFrame">Window pixel → frame pixel (null outside the frame).</param>
    public void Update(KeyboardState keys, GamePadState pad, MouseState mouse, TimeSpan now, Func<Point, Point?> toFrame)
    {
        _pressed.Clear();
        _clicks.Clear();
        _newInput = null;
        _injected = false;

        foreach (var k in keys.GetPressedKeys())
            if (_prevKeys.IsKeyUp(k))
            {
                LastDevice = InputDevice.Keyboard;
                _newInput ??= (InputDevice.Keyboard, k.ToString());
            }
        if (pad.IsConnected)
            foreach (var b in AllButtons)
                if (pad.IsButtonDown(b) && !_prevPad.IsButtonDown(b))
                {
                    LastDevice = InputDevice.Gamepad;
                    _newInput ??= (InputDevice.Gamepad, b.ToString());
                }

        MousePosition = toFrame(mouse.Position);
        if (mouse.Position != _prevMouse.Position && MousePosition is not null)
            LastDevice = InputDevice.Mouse;
        if (MousePosition is { } mp)
        {
            if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
                _clicks.Add(new ScreenClick(mp.X, mp.Y, false));
            if (mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released)
                _clicks.Add(new ScreenClick(mp.X, mp.Y, true));
        }

        foreach (var a in Enum.GetValues<GameAction>())
        {
            bool down = IsDown(a, keys, pad), was = IsDown(a, _prevKeys, _prevPad);
            if (down && !was)
            {
                _pressed.Add(a);
                _heldSince[a] = now;
                _nextRepeat[a] = now + RepeatDelay;
            }
            else if (down && Repeating.Contains(a) && _nextRepeat.TryGetValue(a, out var next) && now >= next)
            {
                _pressed.Add(a);
                _nextRepeat[a] = now + RepeatInterval;
            }
            else if (!down)
            {
                _heldSince.Remove(a);
                _nextRepeat.Remove(a);
            }
        }

        _prevKeys = keys;
        _prevPad = pad;
        _prevMouse = mouse;
    }

    private bool IsDown(GameAction a, KeyboardState keys, GamePadState pad)
    {
        foreach (var name in Bindings.Keys(a))
            if (Enum.TryParse<Keys>(name, out var k) && keys.IsKeyDown(k))
                return true;
        if (pad.IsConnected)
            foreach (var name in Bindings.Buttons(a))
                if (Enum.TryParse<Buttons>(name, out var b) && pad.IsButtonDown(b))
                    return true;
        return false;
    }

    /// <summary>Forget held keys (after a screen change, so a held key does not act on the new screen at once).</summary>
    public void Swallow()
    {
        _pressed.Clear();
        _clicks.Clear();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The key buffer (docs/re/view3d.md "Keys while the program is busy")
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// QoL (<see cref="ClientSettings.SkipPauses"/>), off = original: Confirm or a click ends a pause early and keys
    /// pressed during a pause are dropped. Off, a pause cannot be cut short and its keys wait for the next key read.
    /// </summary>
    public bool SkipPauses { get; set; }

    private HashSet<GameAction>? _buffered;
    private (InputDevice Device, string Name)? _bufferedNew;
    private bool _injected;

    /// <summary>A key or pad button pressed while the program was not reading keys, waiting for the next read.</summary>
    public bool HasBuffered => _buffered is not null;

    /// <summary>
    /// Called once a frame by a screen, after its own Controls check: whether the game logic reads keys now. The
    /// program reads keys through GetKey (START 0x89E9), which takes every key waiting in the BIOS buffer and keeps
    /// the last one; while it does not read (a pause, the text typing out), keys wait in the buffer. So: not reading,
    /// this frame's key or button press is kept as the waiting key (the newest replaces an older one) and the frame acts
    /// on nothing; reading, a frame with no new press gets the waiting key as its press. Mouse clicks are not keys:
    /// during a pause they are dropped. With <see cref="SkipPauses"/> nothing is kept and the frame's input stays (the
    /// screen ends the pause on Confirm).
    /// </summary>
    public void ReadingKeys(bool reading)
    {
        if (reading)
        {
            if (_buffered is null)
                return;
            if (_pressed.Count == 0 && _newInput is null)
            {
                _pressed.UnionWith(_buffered);
                _newInput = _bufferedNew;
                if (_bufferedNew is { } bn)
                    LastDevice = bn.Device;
            }
            Discard();
            return;
        }
        if (SkipPauses)
            return;
        if (!_injected && (_pressed.Count > 0 || _newInput is not null))
        {
            _buffered = [.. _pressed];
            _bufferedNew = _newInput;
        }
        _pressed.Clear();
        _clicks.Clear();
        _newInput = null;
    }

    /// <summary>
    /// Empties the key buffer: the program's reads that take a waiting key and act on none (START 0x8AB2, called by
    /// the fight start 0xE402 and the setup loop 0xDFC6).
    /// </summary>
    public void Discard()
    {
        _buffered = null;
        _bufferedNew = null;
    }

    /// <summary>
    /// The AI's key check (GAME.OVR 0xBB81) on the logic thread while the screen waits: a waiting key is read (the
    /// buffer empties) and its code returned: <see cref="GameAction.FightManual"/> (space, the pad's right-stick press)
    /// 0x20; 0 for any other key (taken, no effect) or none. [?] The check's Alt-M (scan 0x32) and Alt-X (0x2D) are not
    /// mapped (the Client has no Alt keys); a click during a pause is dropped, so the mouse has no way to it.
    /// </summary>
    public byte TakeAiKey()
    {
        bool manual = _buffered?.Contains(GameAction.FightManual) == true;
        Discard();
        return manual ? (byte)0x20 : (byte)0;
    }

    /// <summary>A pause may end early this frame (QoL <see cref="SkipPauses"/> on and Confirm pressed or a click made).</summary>
    public bool SkipsPause => SkipPauses && (Pressed(GameAction.Confirm) || _clicks.Count > 0);
}
