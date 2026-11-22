# How to Add or Maintain a WebChat Engine in DWN.Bridge

This guide describes the modular architecture introduced in Phase 1 (`Services/WebChat/`) to:
1. Quickly fix broken GUI selectors **without recompiling** via local JSON overrides.
2. Diagnose selectors using `SelfTestAsync`.
3. Add a new WebChat provider (e.g., Claude, ChatGPT, DeepSeek) in complete isolation.

---

## 1. Quick Fix for a Broken Selector (Local JSON Override)

Selector resolution follows three tiers in ascending order of priority:

```
1. In-Code Defaults (Engine)  <  2. OTA Server Config (ISelectorsService)  <  3. Local JSON Override
```

The local override file is located at:
```
%LocalAppData%\AIBridge\selectors\{engineId}.json
```
For Gemini, the path is:
```
%LocalAppData%\AIBridge\selectors\gemini.json
```

### Override Rules
- **Only specified keys** in the JSON will override the underlying layers; all omitted keys retain their OTA or hardcoded default values.
- Key names are **case-insensitive** and map to the properties of `EngineSelectors`:
  - `TextArea`
  - `SendButton`
  - `StopButton`
  - `MessageElements`
  - `UploadButton`
  - `MenuItems`
  - `AttachButton`
  - `UploadMenuTextRegex`
  - `SignedOutIndicators`
  - `SignInLink`
  - `SignInButton`
  - `CaptchaFrame`
  - `CodeBlock`
  - `HidePopupsCss`
  - `ChatLinkSelectorTemplates` (JSON array of strings)
- **Automatic Hot-Reload:** Upon the next access of `engine.Selectors` (e.g. on the next prompt dispatch, conversation switch, or `SelfTestAsync`), the updated file is reloaded automatically without restarting the application.
- **Safety:** If the JSON file is malformed or has syntax errors, it is safely ignored with a log entry `[Selectors] Local override ignored ...`, and the engine continues using OTA/default selectors.

### Practical Example (`gemini.json`)
```json
{
  "StopButton": "button[aria-label*='Stop'], button[data-testid='stop-btn']",
  "TextArea": "div[role='textbox'][contenteditable='true']"
}
```

---

## 2. Interpreting `IChatWebEngine.SelfTestAsync(IPage)`

`SelfTestAsync(page)` runs a fast diagnostic check on the live DOM and returns an `EngineSelfTestReport`:

- `AllCriticalFound`: `true` if **all** critical selectors (`TextArea`, `SendButton`) are present in the DOM.
- `Checks`: list of `SelectorCheckResult(Name, Selector, Found, MatchCount, Note)`:
  - `Note == null` → **Critical** selector (`TextArea`, `SendButton`). If `Found == false`, prompt dispatching is blocked.
  - `Note == "optional"` → **Contextual** selector (`StopButton` is only visible during active token generation, `MessageElements` may be 0 on a brand new empty chat, `UploadButton` depends on viewport layout).

Example debug log output:
```
[SelfTest:gemini] CriticalOK=True | TextArea=OK(1), SendButton=OK(1), StopButton=MISSING, MessageElements=OK(4), UploadButton=OK(1)
```

---

## 3. Checklist for Creating a New Engine

1. **Dedicated Folder:** Create `Services/WebChat/Engines/<Provider>/<Provider>WebEngine.cs` (all CSS/URL literals for that site must live **exclusively** inside this folder, verified by test E3).
2. **Implement `IChatWebEngine`:**
   - `Descriptor`: Choose a stable `Id` (e.g., `"claude"`), `DisplayName`, `ProfileFolderName` (e.g., `"ClaudeProfile"` — never change it after release to preserve user session cookies), and `BaseUrl`.
   - `Selectors`: Always resolve via `SelectorLayerResolver.Resolve(Descriptor.Id, _defaultSelectors, _selectorsService?.Current)`.
   - `BuildNewChatUrl`, `BuildChatUrl`, `IsOnEngineHost`, `TryExtractChatId`.
   - `ClassifyAsync` and `ReadMessageAsync` (use `FileBlockHtmlProtector.ProtectCustomTagsInFileBlocks(html)` before `ReverseMarkdown` to preserve XML/`.csproj` tags not enclosed in `<pre><code>`, and normalize relative URLs `//...` to `https://...`).
   - `BuildStopButtonSelector()`, `DetectBlockAsync(page)`, `IsSpaCrash(response)`.
   - `PreparePageAsync(page)`, `AttachFilesAsync(...)`, `NavigateToChatAsync(...)`, `NavigateToNewChatAsync(...)`, `GetChatTitleAsync(...)`.
   - `SelfTestAsync(page)`.
3. **Registration in `EngineRegistry`:**
   In `Services/WebChat/EngineRegistry.cs`:
   ```csharp
   registry.Register("claude", (selectors, ui) => new ClaudeWebEngine(selectors, new ClaudeLoginFlow(ui)));
   ```
4. **HTML Fixture + Compliance Suite:**
   - Add `AIBridge.WebChat.Tests/Fixtures/<provider>_streaming.html` with minimal selectors and test JS helpers (`window.testApi`).
   - Create `<Provider>WebEngineContractTests : ChatWebEngineContractTests<<Provider>WebEngine>` in `AIBridge.WebChat.Tests/`: all contract tests (`Descriptor`, URL round-trip, `ClassifyAndRead`, `StopButtonSelector`, `DetectBlock`, `SelfTestAsync`) will run automatically.

---

## 4. Minimal Engine Template

```csharp
using AIBridge.Services.WebChat;
using Microsoft.Playwright;

namespace AIBridge.Services.WebChat.Engines.MyProvider;

public sealed class MyProviderWebEngine : IChatWebEngine
{
    private readonly ISelectorsService? _selectorsService;
    private readonly EngineSelectors _defaults = CreateDefaults();

    public MyProviderWebEngine(ISelectorsService? selectorsService = null)
        => _selectorsService = selectorsService;

    public EngineDescriptor Descriptor { get; } = new(
        Id: "myprovider",
        DisplayName: "My Provider",
        ProfileFolderName: "MyProviderProfile",
        BaseUrl: "https://chat.example.com/"
    );

    public EngineTimings Timings { get; } = new();

    public EngineSelectors Selectors =>
        SelectorLayerResolver.Resolve(Descriptor.Id, _defaults, _selectorsService?.Current);

    private static EngineSelectors CreateDefaults() => new()
    {
        TextArea = "textarea#prompt",
        SendButton = "button[type='submit']",
        StopButton = "button[aria-label='Stop']",
        MessageElements = ".message-user, .message-assistant"
    };

    // Implement remaining members of IChatWebEngine...
}
```
