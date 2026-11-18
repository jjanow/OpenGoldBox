using GoldBox.Client.Input;
using GoldBox.Client.Screens;
using GoldBox.Core.Explore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace GoldBox.Client;

/// <summary>
/// The MonoGame game: one 320×200 indexed frame drawn by the current screen, shown scaled with sharp pixels and
/// letterboxed to the window. Input goes through <see cref="ActionInput"/> only.
/// </summary>
public sealed class GoldBoxGame : Game, IScreenHost
{
    public const int FrameWidth = 320, FrameHeight = 200;

    private readonly GraphicsDeviceManager _graphics;
    private readonly Func<IScreenHost, IScreen?> _firstScreen;
    private readonly int _scale;
    private readonly Stack<IScreen> _screens = new();
    private readonly IndexedFrame _frame = new(FrameWidth, FrameHeight);
    private SpriteBatch? _batch;
    private FrameTexture? _texture;
    private Rectangle _dest;

    public ActionInput Input { get; }

    /// <summary>Shown in the window title when there is nothing to play (no game folder): the reason.</summary>
    public string? Problem { get; init; }

    /// <summary>The colours of the frame's indices (EGA 0–15 plus the art's own palettes); null = EGA only.</summary>
    public ScreenPalette? Palette { get; init; }

    /// <summary>QoL option: show the frame at 4:3 (each pixel 1.2 times taller), off = square pixels.</summary>
    public bool Stretch43 { get; init; }

    public GoldBoxGame(Func<IScreenHost, IScreen?> firstScreen, InputBindings bindings, int scale)
    {
        _firstScreen = firstScreen;
        _scale = Math.Clamp(scale, 1, 8);
        Input = new ActionInput(bindings);
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = FrameWidth * _scale,
            PreferredBackBufferHeight = FrameHeight * _scale,
            SynchronizeWithVerticalRetrace = true,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "Gold Box: Pool of Radiance";
    }

    protected override void Initialize()
    {
        if (Stretch43)
        {
            _graphics.PreferredBackBufferHeight = 240 * _scale;
            _graphics.ApplyChanges();
        }
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _batch = new SpriteBatch(GraphicsDevice);
        _texture = new FrameTexture(GraphicsDevice, FrameWidth, FrameHeight);
        if (_firstScreen(this) is { } s)
            _screens.Push(s);
        if (Problem is not null)
            Window.Title = "Gold Box: " + Problem;
    }

    public void Push(IScreen screen)
    {
        _screens.Push(screen);
        Input.Swallow();
    }

    public void Pop()
    {
        if (_screens.Count > 1)
            _screens.Pop();
        Input.Swallow();
    }

    public void Quit() => Exit();

    public void SaveBindings() => ClientSettings.SaveBindings(Input.Bindings);

    protected override void Update(GameTime gameTime)
    {
        var pad = FirstConnectedPad(GamePad.MaximumGamePadCount, i => GamePad.GetState(i));
        Input.Update(Keyboard.GetState(), pad, Mouse.GetState(), gameTime.TotalGameTime, ToFrame);
        if (_screens.Count == 0)
        {
            // Nothing to play: any Cancel or click closes the window.
            if (Input.Pressed(GameAction.Cancel) || Input.Clicks.Count > 0)
                Exit();
            base.Update(gameTime);
            return;
        }
        var screen = _screens.Peek();
        if (Input.MousePosition is { } mp && Input.LastDevice == InputDevice.Mouse)
            screen.Hover(mp.X, mp.Y);
        foreach (var click in Input.Clicks)
            if (screen.MapClick(click) is { } a)
                Input.Inject(a);
        screen.Update(Input);
        base.Update(gameTime);
    }

    /// <summary>
    /// The pad to read: the first connected one of the <paramref name="slots"/> slots (SDL hands a pad the first free
    /// slot, so after a reconnect it may sit in slot 2 or later), or slot 0's state when none is connected.
    /// </summary>
    public static GamePadState FirstConnectedPad(int slots, Func<int, GamePadState> state)
    {
        for (int i = 0; i < slots; i++)
            if (state(i) is { IsConnected: true } s)
                return s;
        return slots > 0 ? state(0) : default;
    }

    /// <summary>Window pixel → frame pixel, or null outside the frame.</summary>
    private Point? ToFrame(Point p)
    {
        if (_dest.Width == 0 || !_dest.Contains(p))
            return null;
        return new Point((p.X - _dest.X) * FrameWidth / _dest.Width, (p.Y - _dest.Y) * FrameHeight / _dest.Height);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_screens.Count > 0)
            _screens.Peek().Draw(_frame, Input);
        else
            _frame.Clear(0);
        _texture!.Upload(_frame, Palette);

        var vp = GraphicsDevice.Viewport;
        int w, h;
        if (Stretch43)
        {
            // 320 × 240 per scale step: whole multiples of the width, height 1.2 × (rows repeat unevenly, as on a CRT).
            int scale = Math.Max(1, Math.Min(vp.Width / FrameWidth, vp.Height / 240));
            (w, h) = (FrameWidth * scale, 240 * scale);
        }
        else
        {
            int scale = Math.Max(1, Math.Min(vp.Width / FrameWidth, vp.Height / FrameHeight));
            (w, h) = (FrameWidth * scale, FrameHeight * scale);
        }
        _dest = new Rectangle((vp.Width - w) / 2, (vp.Height - h) / 2, w, h);

        GraphicsDevice.Clear(Color.Black);
        _batch!.Begin(samplerState: SamplerState.PointClamp);
        _batch.Draw(_texture.Texture, _dest, Color.White);
        _batch.End();
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _texture?.Dispose();
        _batch?.Dispose();
    }
}
