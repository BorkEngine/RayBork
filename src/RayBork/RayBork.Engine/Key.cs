namespace RayBork;

/// <summary>
/// Physical keyboard scancodes whose numeric values match the SDL3 scancode set.
/// </summary>
/// <remarks>
/// Values are SDL3 scancode numbers, so a platform scancode can be cast directly to <see cref="Key"/>.
/// Names describe key positions, not layout characters: <see cref="W"/> is the key in the W position on a
/// US layout regardless of the active layout. There is one name per value.
/// </remarks>
public enum Key {
    /// <summary>The Unknown key.</summary>
    Unknown = 0,
    /// <summary>The A key.</summary>
    A = 4,
    /// <summary>The B key.</summary>
    B = 5,
    /// <summary>The C key.</summary>
    C = 6,
    /// <summary>The D key.</summary>
    D = 7,
    /// <summary>The E key.</summary>
    E = 8,
    /// <summary>The F key.</summary>
    F = 9,
    /// <summary>The G key.</summary>
    G = 10,
    /// <summary>The H key.</summary>
    H = 11,
    /// <summary>The I key.</summary>
    I = 12,
    /// <summary>The J key.</summary>
    J = 13,
    /// <summary>The K key.</summary>
    K = 14,
    /// <summary>The L key.</summary>
    L = 15,
    /// <summary>The M key.</summary>
    M = 16,
    /// <summary>The N key.</summary>
    N = 17,
    /// <summary>The O key.</summary>
    O = 18,
    /// <summary>The P key.</summary>
    P = 19,
    /// <summary>The Q key.</summary>
    Q = 20,
    /// <summary>The R key.</summary>
    R = 21,
    /// <summary>The S key.</summary>
    S = 22,
    /// <summary>The T key.</summary>
    T = 23,
    /// <summary>The U key.</summary>
    U = 24,
    /// <summary>The V key.</summary>
    V = 25,
    /// <summary>The W key.</summary>
    W = 26,
    /// <summary>The X key.</summary>
    X = 27,
    /// <summary>The Y key.</summary>
    Y = 28,
    /// <summary>The Z key.</summary>
    Z = 29,
    /// <summary>The D1 number-row key.</summary>
    D1 = 30,
    /// <summary>The D2 number-row key.</summary>
    D2 = 31,
    /// <summary>The D3 number-row key.</summary>
    D3 = 32,
    /// <summary>The D4 number-row key.</summary>
    D4 = 33,
    /// <summary>The D5 number-row key.</summary>
    D5 = 34,
    /// <summary>The D6 number-row key.</summary>
    D6 = 35,
    /// <summary>The D7 number-row key.</summary>
    D7 = 36,
    /// <summary>The D8 number-row key.</summary>
    D8 = 37,
    /// <summary>The D9 number-row key.</summary>
    D9 = 38,
    /// <summary>The D0 number-row key.</summary>
    D0 = 39,
    /// <summary>The Enter key.</summary>
    Enter = 40,
    /// <summary>The Escape key.</summary>
    Escape = 41,
    /// <summary>The Backspace key.</summary>
    Backspace = 42,
    /// <summary>The Tab key.</summary>
    Tab = 43,
    /// <summary>The Space key.</summary>
    Space = 44,
    /// <summary>The Minus key.</summary>
    Minus = 45,
    /// <summary>The Equal key.</summary>
    Equal = 46,
    /// <summary>The LeftBracket key.</summary>
    LeftBracket = 47,
    /// <summary>The RightBracket key.</summary>
    RightBracket = 48,
    /// <summary>The Backslash key.</summary>
    Backslash = 49,
    /// <summary>The NonUsHash key.</summary>
    NonUsHash = 50,
    /// <summary>The Semicolon key.</summary>
    Semicolon = 51,
    /// <summary>The Apostrophe key.</summary>
    Apostrophe = 52,
    /// <summary>The Grave key.</summary>
    Grave = 53,
    /// <summary>The Comma key.</summary>
    Comma = 54,
    /// <summary>The Period key.</summary>
    Period = 55,
    /// <summary>The Slash key.</summary>
    Slash = 56,
    /// <summary>The CapsLock key.</summary>
    CapsLock = 57,
    /// <summary>The F1 function key.</summary>
    F1 = 58,
    /// <summary>The F2 function key.</summary>
    F2 = 59,
    /// <summary>The F3 function key.</summary>
    F3 = 60,
    /// <summary>The F4 function key.</summary>
    F4 = 61,
    /// <summary>The F5 function key.</summary>
    F5 = 62,
    /// <summary>The F6 function key.</summary>
    F6 = 63,
    /// <summary>The F7 function key.</summary>
    F7 = 64,
    /// <summary>The F8 function key.</summary>
    F8 = 65,
    /// <summary>The F9 function key.</summary>
    F9 = 66,
    /// <summary>The F10 function key.</summary>
    F10 = 67,
    /// <summary>The F11 function key.</summary>
    F11 = 68,
    /// <summary>The F12 function key.</summary>
    F12 = 69,
    /// <summary>The PrintScreen key.</summary>
    PrintScreen = 70,
    /// <summary>The ScrollLock key.</summary>
    ScrollLock = 71,
    /// <summary>The Pause key.</summary>
    Pause = 72,
    /// <summary>The Insert key.</summary>
    Insert = 73,
    /// <summary>The Home key.</summary>
    Home = 74,
    /// <summary>The PageUp key.</summary>
    PageUp = 75,
    /// <summary>The Delete key.</summary>
    Delete = 76,
    /// <summary>The End key.</summary>
    End = 77,
    /// <summary>The PageDown key.</summary>
    PageDown = 78,
    /// <summary>The Right key.</summary>
    Right = 79,
    /// <summary>The Left key.</summary>
    Left = 80,
    /// <summary>The Down key.</summary>
    Down = 81,
    /// <summary>The Up key.</summary>
    Up = 82,
    /// <summary>The NumLock key.</summary>
    NumLock = 83,
    /// <summary>The KeypadDivide key.</summary>
    KeypadDivide = 84,
    /// <summary>The KeypadMultiply key.</summary>
    KeypadMultiply = 85,
    /// <summary>The KeypadSubtract key.</summary>
    KeypadSubtract = 86,
    /// <summary>The KeypadAdd key.</summary>
    KeypadAdd = 87,
    /// <summary>The KeypadEnter key.</summary>
    KeypadEnter = 88,
    /// <summary>The numeric keypad 1 key.</summary>
    Keypad1 = 89,
    /// <summary>The numeric keypad 2 key.</summary>
    Keypad2 = 90,
    /// <summary>The numeric keypad 3 key.</summary>
    Keypad3 = 91,
    /// <summary>The numeric keypad 4 key.</summary>
    Keypad4 = 92,
    /// <summary>The numeric keypad 5 key.</summary>
    Keypad5 = 93,
    /// <summary>The numeric keypad 6 key.</summary>
    Keypad6 = 94,
    /// <summary>The numeric keypad 7 key.</summary>
    Keypad7 = 95,
    /// <summary>The numeric keypad 8 key.</summary>
    Keypad8 = 96,
    /// <summary>The numeric keypad 9 key.</summary>
    Keypad9 = 97,
    /// <summary>The numeric keypad 0 key.</summary>
    Keypad0 = 98,
    /// <summary>The KeypadDecimal key.</summary>
    KeypadDecimal = 99,
    /// <summary>The NonUsBackslash key.</summary>
    NonUsBackslash = 100,
    /// <summary>The Application key.</summary>
    Application = 101,
    /// <summary>The Power key.</summary>
    Power = 102,
    /// <summary>The KeypadEqual key.</summary>
    KeypadEqual = 103,
    /// <summary>The F13 function key.</summary>
    F13 = 104,
    /// <summary>The F14 function key.</summary>
    F14 = 105,
    /// <summary>The F15 function key.</summary>
    F15 = 106,
    /// <summary>The F16 function key.</summary>
    F16 = 107,
    /// <summary>The F17 function key.</summary>
    F17 = 108,
    /// <summary>The F18 function key.</summary>
    F18 = 109,
    /// <summary>The F19 function key.</summary>
    F19 = 110,
    /// <summary>The F20 function key.</summary>
    F20 = 111,
    /// <summary>The F21 function key.</summary>
    F21 = 112,
    /// <summary>The F22 function key.</summary>
    F22 = 113,
    /// <summary>The F23 function key.</summary>
    F23 = 114,
    /// <summary>The F24 function key.</summary>
    F24 = 115,
    /// <summary>The Execute key.</summary>
    Execute = 116,
    /// <summary>The Help key.</summary>
    Help = 117,
    /// <summary>The Menu key.</summary>
    Menu = 118,
    /// <summary>The Select key.</summary>
    Select = 119,
    /// <summary>The Stop key.</summary>
    Stop = 120,
    /// <summary>The Again key.</summary>
    Again = 121,
    /// <summary>The Undo key.</summary>
    Undo = 122,
    /// <summary>The Cut key.</summary>
    Cut = 123,
    /// <summary>The Copy key.</summary>
    Copy = 124,
    /// <summary>The Paste key.</summary>
    Paste = 125,
    /// <summary>The Find key.</summary>
    Find = 126,
    /// <summary>The Mute key.</summary>
    Mute = 127,
    /// <summary>The VolumeUp key.</summary>
    VolumeUp = 128,
    /// <summary>The VolumeDown key.</summary>
    VolumeDown = 129,
    /// <summary>The KeypadComma key.</summary>
    KeypadComma = 133,
    /// <summary>The KeypadEqualsAs400 key.</summary>
    KeypadEqualsAs400 = 134,
    /// <summary>The International1 key.</summary>
    International1 = 135,
    /// <summary>The International2 key.</summary>
    International2 = 136,
    /// <summary>The International3 key.</summary>
    International3 = 137,
    /// <summary>The International4 key.</summary>
    International4 = 138,
    /// <summary>The International5 key.</summary>
    International5 = 139,
    /// <summary>The International6 key.</summary>
    International6 = 140,
    /// <summary>The International7 key.</summary>
    International7 = 141,
    /// <summary>The International8 key.</summary>
    International8 = 142,
    /// <summary>The International9 key.</summary>
    International9 = 143,
    /// <summary>The Lang1 key.</summary>
    Lang1 = 144,
    /// <summary>The Lang2 key.</summary>
    Lang2 = 145,
    /// <summary>The Lang3 key.</summary>
    Lang3 = 146,
    /// <summary>The Lang4 key.</summary>
    Lang4 = 147,
    /// <summary>The Lang5 key.</summary>
    Lang5 = 148,
    /// <summary>The Lang6 key.</summary>
    Lang6 = 149,
    /// <summary>The Lang7 key.</summary>
    Lang7 = 150,
    /// <summary>The Lang8 key.</summary>
    Lang8 = 151,
    /// <summary>The Lang9 key.</summary>
    Lang9 = 152,
    /// <summary>The AltErase key.</summary>
    AltErase = 153,
    /// <summary>The SysReq key.</summary>
    SysReq = 154,
    /// <summary>The Cancel key.</summary>
    Cancel = 155,
    /// <summary>The Clear key.</summary>
    Clear = 156,
    /// <summary>The Prior key.</summary>
    Prior = 157,
    /// <summary>The Return2 key.</summary>
    Return2 = 158,
    /// <summary>The Separator key.</summary>
    Separator = 159,
    /// <summary>The Out key.</summary>
    Out = 160,
    /// <summary>The Oper key.</summary>
    Oper = 161,
    /// <summary>The ClearAgain key.</summary>
    ClearAgain = 162,
    /// <summary>The CrSel key.</summary>
    CrSel = 163,
    /// <summary>The ExSel key.</summary>
    ExSel = 164,
    /// <summary>The Front key.</summary>
    Front = 165,
    /// <summary>The Keypad00 key.</summary>
    Keypad00 = 176,
    /// <summary>The Keypad000 key.</summary>
    Keypad000 = 177,
    /// <summary>The ThousandsSeparator key.</summary>
    ThousandsSeparator = 178,
    /// <summary>The DecimalSeparator key.</summary>
    DecimalSeparator = 179,
    /// <summary>The CurrencyUnit key.</summary>
    CurrencyUnit = 180,
    /// <summary>The CurrencySubunit key.</summary>
    CurrencySubunit = 181,
    /// <summary>The KeypadLeftParen key.</summary>
    KeypadLeftParen = 182,
    /// <summary>The KeypadRightParen key.</summary>
    KeypadRightParen = 183,
    /// <summary>The KeypadLeftBrace key.</summary>
    KeypadLeftBrace = 184,
    /// <summary>The KeypadRightBrace key.</summary>
    KeypadRightBrace = 185,
    /// <summary>The KeypadTab key.</summary>
    KeypadTab = 186,
    /// <summary>The KeypadBackspace key.</summary>
    KeypadBackspace = 187,
    /// <summary>The KeypadA key.</summary>
    KeypadA = 188,
    /// <summary>The KeypadB key.</summary>
    KeypadB = 189,
    /// <summary>The KeypadC key.</summary>
    KeypadC = 190,
    /// <summary>The KeypadD key.</summary>
    KeypadD = 191,
    /// <summary>The KeypadE key.</summary>
    KeypadE = 192,
    /// <summary>The KeypadF key.</summary>
    KeypadF = 193,
    /// <summary>The KeypadXor key.</summary>
    KeypadXor = 194,
    /// <summary>The KeypadPower key.</summary>
    KeypadPower = 195,
    /// <summary>The KeypadPercent key.</summary>
    KeypadPercent = 196,
    /// <summary>The KeypadLess key.</summary>
    KeypadLess = 197,
    /// <summary>The KeypadGreater key.</summary>
    KeypadGreater = 198,
    /// <summary>The KeypadAmpersand key.</summary>
    KeypadAmpersand = 199,
    /// <summary>The KeypadDoubleAmpersand key.</summary>
    KeypadDoubleAmpersand = 200,
    /// <summary>The KeypadVerticalBar key.</summary>
    KeypadVerticalBar = 201,
    /// <summary>The KeypadDoubleVerticalBar key.</summary>
    KeypadDoubleVerticalBar = 202,
    /// <summary>The KeypadColon key.</summary>
    KeypadColon = 203,
    /// <summary>The KeypadHash key.</summary>
    KeypadHash = 204,
    /// <summary>The KeypadSpace key.</summary>
    KeypadSpace = 205,
    /// <summary>The KeypadAt key.</summary>
    KeypadAt = 206,
    /// <summary>The KeypadExclamation key.</summary>
    KeypadExclamation = 207,
    /// <summary>The KeypadMemoryStore key.</summary>
    KeypadMemoryStore = 208,
    /// <summary>The KeypadMemoryRecall key.</summary>
    KeypadMemoryRecall = 209,
    /// <summary>The KeypadMemoryClear key.</summary>
    KeypadMemoryClear = 210,
    /// <summary>The KeypadMemoryAdd key.</summary>
    KeypadMemoryAdd = 211,
    /// <summary>The KeypadMemorySubtract key.</summary>
    KeypadMemorySubtract = 212,
    /// <summary>The KeypadMemoryMultiply key.</summary>
    KeypadMemoryMultiply = 213,
    /// <summary>The KeypadMemoryDivide key.</summary>
    KeypadMemoryDivide = 214,
    /// <summary>The KeypadPlusMinus key.</summary>
    KeypadPlusMinus = 215,
    /// <summary>The KeypadClear key.</summary>
    KeypadClear = 216,
    /// <summary>The KeypadClearEntry key.</summary>
    KeypadClearEntry = 217,
    /// <summary>The KeypadBinary key.</summary>
    KeypadBinary = 218,
    /// <summary>The KeypadOctal key.</summary>
    KeypadOctal = 219,
    /// <summary>The KeypadDecimalSeparator key.</summary>
    KeypadDecimalSeparator = 220,
    /// <summary>The KeypadHexadecimal key.</summary>
    KeypadHexadecimal = 221,
    /// <summary>The LeftControl key.</summary>
    LeftControl = 224,
    /// <summary>The LeftShift key.</summary>
    LeftShift = 225,
    /// <summary>The LeftAlt key.</summary>
    LeftAlt = 226,
    /// <summary>The LeftSuper key.</summary>
    LeftSuper = 227,
    /// <summary>The RightControl key.</summary>
    RightControl = 228,
    /// <summary>The RightShift key.</summary>
    RightShift = 229,
    /// <summary>The RightAlt key.</summary>
    RightAlt = 230,
    /// <summary>The RightSuper key.</summary>
    RightSuper = 231,
    /// <summary>The Mode key.</summary>
    Mode = 257,
    /// <summary>The Sleep key.</summary>
    Sleep = 258,
    /// <summary>The Wake key.</summary>
    Wake = 259,
    /// <summary>The ChannelIncrement key.</summary>
    ChannelIncrement = 260,
    /// <summary>The ChannelDecrement key.</summary>
    ChannelDecrement = 261,
    /// <summary>The MediaPlay key.</summary>
    MediaPlay = 262,
    /// <summary>The MediaPause key.</summary>
    MediaPause = 263,
    /// <summary>The MediaRecord key.</summary>
    MediaRecord = 264,
    /// <summary>The MediaFastForward key.</summary>
    MediaFastForward = 265,
    /// <summary>The MediaRewind key.</summary>
    MediaRewind = 266,
    /// <summary>The MediaNextTrack key.</summary>
    MediaNextTrack = 267,
    /// <summary>The MediaPreviousTrack key.</summary>
    MediaPreviousTrack = 268,
    /// <summary>The MediaStop key.</summary>
    MediaStop = 269,
    /// <summary>The MediaEject key.</summary>
    MediaEject = 270,
    /// <summary>The MediaPlayPause key.</summary>
    MediaPlayPause = 271,
    /// <summary>The MediaSelect key.</summary>
    MediaSelect = 272,
    /// <summary>The AccessibilityNew key.</summary>
    AccessibilityNew = 273,
    /// <summary>The AccessibilityOpen key.</summary>
    AccessibilityOpen = 274,
    /// <summary>The AccessibilityClose key.</summary>
    AccessibilityClose = 275,
    /// <summary>The AccessibilityExit key.</summary>
    AccessibilityExit = 276,
    /// <summary>The AccessibilitySave key.</summary>
    AccessibilitySave = 277,
    /// <summary>The AccessibilityPrint key.</summary>
    AccessibilityPrint = 278,
    /// <summary>The AccessibilityProperties key.</summary>
    AccessibilityProperties = 279,
    /// <summary>The AccessibilitySearch key.</summary>
    AccessibilitySearch = 280,
    /// <summary>The AccessibilityHome key.</summary>
    AccessibilityHome = 281,
    /// <summary>The AccessibilityBack key.</summary>
    AccessibilityBack = 282,
    /// <summary>The AccessibilityForward key.</summary>
    AccessibilityForward = 283,
    /// <summary>The AccessibilityStop key.</summary>
    AccessibilityStop = 284,
    /// <summary>The AccessibilityRefresh key.</summary>
    AccessibilityRefresh = 285,
    /// <summary>The AccessibilityBookmarks key.</summary>
    AccessibilityBookmarks = 286,
    /// <summary>The SoftLeft key.</summary>
    SoftLeft = 287,
    /// <summary>The SoftRight key.</summary>
    SoftRight = 288,
    /// <summary>The Call key.</summary>
    Call = 289,
    /// <summary>The EndCall key.</summary>
    EndCall = 290,
}
