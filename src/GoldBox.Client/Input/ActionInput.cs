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
            if (_prevKeys.IsKeyUp(k) && !IsModifier(k))
            {
                LastDevice = InputDevice.Keyboard;
                _newInput ??= (InputDevice.Keyboard, Modified(keys, k.ToString()));
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

    private static bool IsModifier(Keys k) =>
        k is Keys.LeftAlt or Keys.RightAlt or Keys.LeftControl or Keys.RightControl;

    private static bool AltDown(KeyboardState keys) => keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt);
    private static bool CtrlDown(KeyboardState keys) => keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl);

    /// <summary>A key's binding name with the modifiers held: "Alt+M", "Ctrl+S", "Ctrl+Alt+X", or the bare name.</summary>
    private static string Modified(KeyboardState keys, string name) =>
        (CtrlDown(keys) ? "Ctrl+" : "") + (AltDown(keys) ? "Alt+" : "") + name;

    /// <summary>
    /// A key binding is down: "Alt+M" / "Ctrl+S" need exactly those modifiers held (the original's Alt-M, Ctrl-S), and a
    /// bare key does not count while Alt or Ctrl is held, so Alt-M is not also M.
    /// </summary>
    internal static bool KeyDown(KeyboardState keys, string binding)
    {
        bool alt = false, ctrl = false;
        var name = binding;
        while (true)
        {
            if (name.StartsWith("Alt+", StringComparison.Ordinal)) { alt = true; name = name[4..]; }
            else if (name.StartsWith("Ctrl+", StringComparison.Ordinal)) { ctrl = true; name = name[5..]; }
            else break;
        }
        return Enum.TryParse<Keys>(name, out var k) && keys.IsKeyDown(k) && AltDown(keys) == alt && CtrlDown(keys) == ctrl;
    }

    private bool IsDown(GameAction a, KeyboardState keys, GamePadState pad)
    {
        foreach (var name in Bindings.Keys(a))
            if (KeyDown(keys, name))
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

    /// <summary>A key or button press waiting in the buffer: its actions, its name, and the bytes the BIOS holds for it
    /// (<see cref="DosKeys.Of"/>; null for a pad button, a held key's repeat or a key the PC has no BIOS key for).</summary>
    private sealed record Waiting(HashSet<GameAction> Pressed, (InputDevice Device, string Name)? New, (byte Ascii, byte Scan)? Dos);

    /// <summary>The BIOS type-ahead buffer holds 15 keys; a key pressed when it is full is lost (DOSBox-X s24).</summary>
    public const int BufferSize = 15;

    private readonly List<Waiting> _waiting = [];
    private bool _injected;

    /// <summary>A key or pad button pressed while the program was not reading keys, waiting for the next read.</summary>
    public bool HasBuffered => _waiting.Count > 0;

    /// <summary>The number of presses waiting (at most <see cref="BufferSize"/>).</summary>
    public int Buffered => _waiting.Count;

    /// <summary>
    /// Called once a frame by a screen, after its own Controls check: whether the game logic reads keys now. While it
    /// does not read (a pause, the text typing out), this frame's key or pad press waits in the buffer (up to
    /// <see cref="BufferSize"/>, later ones lost) and the frame acts on nothing; mouse clicks are not keys and are
    /// dropped. A frame that reads with presses waiting (its own new press, if any, joins them last) gets what the
    /// program's GetKey (START 0x89E9) makes of them (<see cref="Resolve"/>), and the buffer is empty after it. With
    /// <see cref="SkipPauses"/> nothing is kept and the frame's input stays (the screen ends the pause on Confirm).
    /// <paramref name="scanRead"/> false: the reader calls GetKey once and does not read an extended key's scan code
    /// (a key wait 0x7C85, line input 0x7ABB: <see cref="ResolveSingle"/>).
    /// </summary>
    public void ReadingKeys(bool reading, bool scanRead = true)
    {
        if (reading)
        {
            if (_waiting.Count == 0)
                return;
            if (_injected)
            {
                Discard();
                return;
            }
            if (_pressed.Count > 0 || _newInput is not null)
                Keep();
            Waiting? read;
            if (!scanRead && _waiting.Count > 0 && _waiting[0].Dos is { Ascii: 0 } first)
                read = ResolveSingle(first.Scan);
            else
            {
                read = Resolve(_waiting, Bindings);
                Discard();
            }
            _pressed.Clear();
            _newInput = null;
            if (read is null)
                return;
            _pressed.UnionWith(read.Pressed);
            _newInput = read.New;
            if (read.New is { } n)
                LastDevice = n.Device;
            return;
        }
        if (SkipPauses)
            return;
        if (!_injected && (_pressed.Count > 0 || _newInput is not null))
            Keep();
        _pressed.Clear();
        _clicks.Clear();
        _newInput = null;
    }

    private void Keep()
    {
        if (_waiting.Count >= BufferSize)
            return;
        var dos = _newInput is (InputDevice.Keyboard, var name) ? DosKeys.Of(name) : null;
        _waiting.Add(new Waiting([.. _pressed], _newInput, dos));
    }

    /// <summary>
    /// What one read takes from the waiting presses (all of them go). GetKey reads a key; unless it is 0 (an extended
    /// key) it reads every key waiting and keeps the last byte, and a reader that got 0 calls GetKey again for the scan
    /// code, which drains the rest the same way (menu 0x2CE4F, the AI's check 0xBB97/0xBBA5). So the program gets the
    /// last byte of the buffer, as a character when the first key was not extended and as an extended code when it
    /// was (DOSBox-X s24, party menu: X then Right reads 'M', Modify; Right then C reads an extended 'c', nothing).
    /// A single press, or a last one with no BIOS bytes (a pad button), is read as itself; a byte no key gives reads
    /// as nothing.
    /// </summary>
    private static Waiting? Resolve(List<Waiting> waiting, InputBindings bindings)
    {
        var last = waiting[^1];
        if (waiting.Count == 1 || last.Dos is not { } l)
            return last;
        byte code = l.Ascii != 0 ? l.Ascii : l.Scan;
        bool extended = waiting[0].Dos is { Ascii: 0 };
        if (extended == (l.Ascii == 0))
            return last;
        string? name = extended ? DosKeys.OfScan(code) : DosKeys.OfCharacter(code);
        if (name is null)
            return null;
        var pressed = new HashSet<GameAction>();
        foreach (var a in Enum.GetValues<GameAction>())
            if (bindings.Keys(a).Contains(name))
                pressed.Add(a);
        return new Waiting(pressed, (InputDevice.Keyboard, name), DosKeys.Of(name));
    }

    /// <summary>
    /// A reader that calls GetKey once (the key wait 0x7C85 at 0x7CC1, line input at START 0x7ABB, the number input
    /// 0x24F84, …) with an extended key first: it gets that key's 0 and nothing more is read, so the first press is
    /// read as itself and its scan code stays waiting (Crt's ReadKey keeps it) ahead of the rest, where the next read
    /// takes it as a character (DOSBox-X s24: Right at the character-name prompt typed M).
    /// </summary>
    private Waiting ResolveSingle(byte scan)
    {
        var first = _waiting[0];
        _waiting.RemoveAt(0);
        string? name = DosKeys.OfCharacter(scan);
        var pressed = new HashSet<GameAction>();
        if (name is not null)
            foreach (var a in Enum.GetValues<GameAction>())
                if (Bindings.Keys(a).Contains(name))
                    pressed.Add(a);
        _waiting.Insert(0, new Waiting(pressed, name is null ? null : (InputDevice.Keyboard, name), (scan, 0)));
        return first;
    }

    /// <summary>
    /// Empties the key buffer: the program's reads that take a waiting key and act on none (START 0x8AB2, called by
    /// the fight start 0xE402 and the setup loop 0xDFC6).
    /// </summary>
    public void Discard() => _waiting.Clear();

    /// <summary>
    /// The AI's key check (GAME.OVR 0xBB81) on the logic thread while the screen waits: the waiting presses are read
    /// (the buffer empties; <see cref="Resolve"/>) and the code compared, extended or not: 0x20 (space,
    /// <see cref="GameAction.FightManual"/>, the pad's right-stick press), 0x32 (Alt-M, <see cref="GameAction.FightQuickMagic"/>;
    /// the '2' key gives it too) and 0x2D (Alt-X, <see cref="GameAction.FightAltX"/>; also '-'); 0 for any other key
    /// (taken, no effect) or none. A click during a pause is dropped, so the mouse has no way to it.
    /// </summary>
    public byte TakeAiKey()
    {
        if (_waiting.Count == 0)
            return 0;
        var read = Resolve(_waiting, Bindings);
        Discard();
        if (read is null)
            return 0;
        if (read.Pressed.Contains(GameAction.FightManual)) return 0x20;
        if (read.Pressed.Contains(GameAction.FightQuickMagic)) return 0x32;
        if (read.Pressed.Contains(GameAction.FightAltX)) return 0x2D;
        byte code = read.Dos is { } d ? d.Ascii != 0 ? d.Ascii : d.Scan : (byte)0;
        return code is 0x20 or 0x32 or 0x2D ? code : (byte)0;
    }

    /// <summary>A pause may end early this frame (QoL <see cref="SkipPauses"/> on and Confirm pressed or a click made).</summary>
    public bool SkipsPause => SkipPauses && (Pressed(GameAction.Confirm) || _clicks.Count > 0);
}
