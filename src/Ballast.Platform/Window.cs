using System.Runtime.InteropServices;
using SDL;
using Vortice.Vulkan;
using static SDL.SDL3;

namespace Ballast.Platform;

/// <summary>Keys the engine cares about, independent of SDL.</summary>
public enum Key
{
    W, A, S, D, E, R, F, B, X, C, Q, T, V, L, Space, LeftShift, Escape, Tab, F1, F2, F3, F5, Backspace,
    D1, D2, D3, D4, D5, D6, D7, D8, D9,
    MouseLeft, MouseRight,
    Up, Down, Left, Right, Enter,
    // The rest of the keyboard and mouse, so any control can be bound to them (T80).
    G, H, I, J, K, M, N, O, P, U, Y, Z, D0, F4, F6, F7, F8, F9, F10, F11, F12,
    LeftCtrl, LeftAlt, RightShift, RightCtrl, RightAlt, CapsLock, MouseMiddle, Mouse4, Mouse5,
}

/// <summary>
/// Keyboard and mouse state for one frame. Look input accumulates between reads so none is lost
/// when the render rate and the 30 Hz tick disagree.
/// </summary>
public sealed class InputState
{
    readonly HashSet<Key> _down = new();
    readonly HashSet<Key> _pressed = new();

    public bool Down(Key k) => _down.Contains(k);
    /// <summary>Went down since the last <see cref="EndFrame"/>.</summary>
    public bool Pressed(Key k) => _pressed.Contains(k);
    /// <summary>A key that went down since the last <see cref="EndFrame"/>, if any (the lowest, if several): for binding one.</summary>
    public Key? AnyPressed => _pressed.Count == 0 ? null : _pressed.Min();
    public float MouseDX { get; internal set; }
    public float MouseDY { get; internal set; }
    /// <summary>Characters typed since the last <see cref="EndFrame"/>, while the window takes text (<see cref="Window.TextInput"/>).</summary>
    public string Text => _text.ToString();
    readonly System.Text.StringBuilder _text = new();

    internal void Type(string text) => _text.Append(text);

    internal void Set(Key k, bool down)
    {
        if (down && _down.Add(k))
            _pressed.Add(k);
        else if (!down)
            _down.Remove(k);
    }

    public void EndFrame()
    {
        _pressed.Clear();
        _text.Clear();
        MouseDX = MouseDY = 0;
    }
}

/// <summary>An SDL3 window with a Vulkan surface, relative mouse look and keyboard input.</summary>
public sealed unsafe class Window : IDisposable
{
    readonly SDL_Window* _window;

    public Window(string title, int width, int height)
    {
        if (!SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO | SDL_InitFlags.SDL_INIT_EVENTS))
            throw new InvalidOperationException("SDL_Init failed: " + SDL_GetError());
        _window = SDL_CreateWindow(title, width, height, SDL_WindowFlags.SDL_WINDOW_VULKAN | SDL_WindowFlags.SDL_WINDOW_RESIZABLE);
        if (_window == null)
            throw new InvalidOperationException("SDL_CreateWindow failed: " + SDL_GetError());
        MouseCaptured = true;
    }

    public InputState Input { get; } = new();
    public bool CloseRequested { get; private set; }
    public bool Resized { get; set; }

    public bool MouseCaptured
    {
        get => SDL_GetWindowRelativeMouseMode(_window);
        set => SDL_SetWindowRelativeMouseMode(_window, value);
    }

    /// <summary>Takes typed text into <see cref="InputState.Text"/> (a menu's address field); off, keys are only keys.</summary>
    public bool TextInput
    {
        get => SDL_TextInputActive(_window);
        set
        {
            if (value)
                SDL_StartTextInput(_window);
            else
                SDL_StopTextInput(_window);
        }
    }

    /// <summary>The whole screen, borderless at the desktop's own mode, or a window (T83).</summary>
    public bool Fullscreen
    {
        get => (SDL_GetWindowFlags(_window) & SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0;
        set
        {
            if (value != Fullscreen)
            {
                SDL_SetWindowFullscreen(_window, value);
                Resized = true;
            }
        }
    }

    /// <summary>Sets the window's size, in points (windowed).</summary>
    public void SetSize(int width, int height)
    {
        SDL_SetWindowSize(_window, width, height);
        Resized = true;
    }

    public string Title
    {
        set => SDL_SetWindowTitle(_window, value);
    }

    public (int Width, int Height) PixelSize
    {
        get
        {
            int w, h;
            SDL_GetWindowSizeInPixels(_window, &w, &h);
            return (w, h);
        }
    }

    /// <summary>Instance extensions SDL needs to create a surface on this platform.</summary>
    public static IReadOnlyList<string> VulkanInstanceExtensions()
    {
        uint count;
        byte** names = SDL_Vulkan_GetInstanceExtensions(&count);
        var list = new List<string>();
        for (uint i = 0; i < count; i++)
            list.Add(Marshal.PtrToStringUTF8((nint)names[i])!);
        return list;
    }

    public VkSurfaceKHR CreateSurface(VkInstance instance)
    {
        VkSurfaceKHR_T* surface;
        if (!SDL_Vulkan_CreateSurface(_window, (VkInstance_T*)instance.Handle, null, &surface))
            throw new InvalidOperationException("SDL_Vulkan_CreateSurface failed: " + SDL_GetError());
        return new VkSurfaceKHR((ulong)surface);
    }

    public void PumpEvents()
    {
        SDL_Event e;
        while (SDL_PollEvent(&e))
        {
            switch ((SDL_EventType)e.type)
            {
                case SDL_EventType.SDL_EVENT_QUIT:
                case SDL_EventType.SDL_EVENT_WINDOW_CLOSE_REQUESTED:
                    CloseRequested = true;
                    break;
                case SDL_EventType.SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                    Resized = true;
                    break;
                case SDL_EventType.SDL_EVENT_KEY_DOWN:
                case SDL_EventType.SDL_EVENT_KEY_UP:
                    if (Map(e.key.scancode) is { } key)
                        Input.Set(key, e.key.down);
                    break;
                case SDL_EventType.SDL_EVENT_TEXT_INPUT:
                    if (e.text.text != null)
                        Input.Type(System.Text.Encoding.UTF8.GetString(System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpanFromNullTerminated(e.text.text)));
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_MOTION:
                    if (MouseCaptured)
                    {
                        Input.MouseDX += e.motion.xrel;
                        Input.MouseDY += e.motion.yrel;
                    }
                    break;
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN:
                case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
                    bool down = (SDL_EventType)e.type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN;
                    if (down && !MouseCaptured)
                    {
                        MouseCaptured = true;
                        break;
                    }
                    if (e.button.button == 1)
                        Input.Set(Key.MouseLeft, down);
                    else if (e.button.button == 3)
                        Input.Set(Key.MouseRight, down);
                    else if (e.button.button == 2)
                        Input.Set(Key.MouseMiddle, down);
                    else if (e.button.button == 4)
                        Input.Set(Key.Mouse4, down);
                    else if (e.button.button == 5)
                        Input.Set(Key.Mouse5, down);
                    break;
            }
        }
    }

    static Key? Map(SDL_Scancode code) => code switch
    {
        SDL_Scancode.SDL_SCANCODE_W => Key.W,
        SDL_Scancode.SDL_SCANCODE_A => Key.A,
        SDL_Scancode.SDL_SCANCODE_S => Key.S,
        SDL_Scancode.SDL_SCANCODE_D => Key.D,
        SDL_Scancode.SDL_SCANCODE_E => Key.E,
        SDL_Scancode.SDL_SCANCODE_R => Key.R,
        SDL_Scancode.SDL_SCANCODE_F => Key.F,
        SDL_Scancode.SDL_SCANCODE_B => Key.B,
        SDL_Scancode.SDL_SCANCODE_X => Key.X,
        SDL_Scancode.SDL_SCANCODE_C => Key.C,
        SDL_Scancode.SDL_SCANCODE_Q => Key.Q,
        SDL_Scancode.SDL_SCANCODE_T => Key.T,
        SDL_Scancode.SDL_SCANCODE_V => Key.V,
        SDL_Scancode.SDL_SCANCODE_L => Key.L,
        SDL_Scancode.SDL_SCANCODE_SPACE => Key.Space,
        SDL_Scancode.SDL_SCANCODE_LSHIFT => Key.LeftShift,
        SDL_Scancode.SDL_SCANCODE_ESCAPE => Key.Escape,
        SDL_Scancode.SDL_SCANCODE_TAB => Key.Tab,
        SDL_Scancode.SDL_SCANCODE_F1 => Key.F1,
        SDL_Scancode.SDL_SCANCODE_F2 => Key.F2,
        SDL_Scancode.SDL_SCANCODE_F3 => Key.F3,
        SDL_Scancode.SDL_SCANCODE_F5 => Key.F5,
        SDL_Scancode.SDL_SCANCODE_BACKSPACE => Key.Backspace,
        SDL_Scancode.SDL_SCANCODE_UP => Key.Up,
        SDL_Scancode.SDL_SCANCODE_DOWN => Key.Down,
        SDL_Scancode.SDL_SCANCODE_LEFT => Key.Left,
        SDL_Scancode.SDL_SCANCODE_RIGHT => Key.Right,
        SDL_Scancode.SDL_SCANCODE_RETURN or SDL_Scancode.SDL_SCANCODE_KP_ENTER => Key.Enter,
        >= SDL_Scancode.SDL_SCANCODE_1 and <= SDL_Scancode.SDL_SCANCODE_9 => Key.D1 + (code - SDL_Scancode.SDL_SCANCODE_1),
        SDL_Scancode.SDL_SCANCODE_0 => Key.D0,
        SDL_Scancode.SDL_SCANCODE_G => Key.G,
        SDL_Scancode.SDL_SCANCODE_H => Key.H,
        SDL_Scancode.SDL_SCANCODE_I => Key.I,
        SDL_Scancode.SDL_SCANCODE_J => Key.J,
        SDL_Scancode.SDL_SCANCODE_K => Key.K,
        SDL_Scancode.SDL_SCANCODE_M => Key.M,
        SDL_Scancode.SDL_SCANCODE_N => Key.N,
        SDL_Scancode.SDL_SCANCODE_O => Key.O,
        SDL_Scancode.SDL_SCANCODE_P => Key.P,
        SDL_Scancode.SDL_SCANCODE_U => Key.U,
        SDL_Scancode.SDL_SCANCODE_Y => Key.Y,
        SDL_Scancode.SDL_SCANCODE_Z => Key.Z,
        SDL_Scancode.SDL_SCANCODE_F4 => Key.F4,
        SDL_Scancode.SDL_SCANCODE_F6 => Key.F6,
        SDL_Scancode.SDL_SCANCODE_F7 => Key.F7,
        SDL_Scancode.SDL_SCANCODE_F8 => Key.F8,
        SDL_Scancode.SDL_SCANCODE_F9 => Key.F9,
        SDL_Scancode.SDL_SCANCODE_F10 => Key.F10,
        SDL_Scancode.SDL_SCANCODE_F11 => Key.F11,
        SDL_Scancode.SDL_SCANCODE_F12 => Key.F12,
        SDL_Scancode.SDL_SCANCODE_LCTRL => Key.LeftCtrl,
        SDL_Scancode.SDL_SCANCODE_LALT => Key.LeftAlt,
        SDL_Scancode.SDL_SCANCODE_RSHIFT => Key.RightShift,
        SDL_Scancode.SDL_SCANCODE_RCTRL => Key.RightCtrl,
        SDL_Scancode.SDL_SCANCODE_RALT => Key.RightAlt,
        SDL_Scancode.SDL_SCANCODE_CAPSLOCK => Key.CapsLock,
        _ => null,
    };

    public void Dispose()
    {
        SDL_DestroyWindow(_window);
        SDL_Quit();
    }
}
