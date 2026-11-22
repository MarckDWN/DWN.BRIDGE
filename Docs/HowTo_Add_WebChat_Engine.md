# Come aggiungere o mantenere un Engine WebChat in DWN.Bridge

Questa guida descrive l'architettura modulare introdotta in Fase 1 (`Services/WebChat/`) per:
1. Fixare rapidamente selettori GUI rotti **senza ricompilare** tramite override JSON locale.
2. Diagnosticare i selettori con `SelfTestAsync`.
3. Aggiungere un nuovo provider WebChat (es. Claude, ChatGPT, DeepSeek) in isolamento.

---

## 1. Fix rapido di un selettore rotto (Override Locale JSON)

La risoluzione dei selettori segue tre layer in ordine crescente di priorità:

```
1. Default nel codice (Engine)  <  2. OTA Server (ISelectorsService)  <  3. Override locale JSON
```

Il file di override locale si trova in:
```
%LocalAppData%\AIBridge\selectors\{engineId}.json
```
Per Gemini, il percorso è:
```
%LocalAppData%\AIBridge\selectors\gemini.json
```

### Regole dell'override
- **Solo le chiavi presenti** nel JSON sovrascrivono i layer sottostanti; tutte le altre mantengono il valore OTA o di default.
- I nomi delle chiavi sono **case-insensitive** e corrispondono alle proprietà di `EngineSelectors`:
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
  - `ChatLinkSelectorTemplates` (array JSON di stringhe)
- **Hot-reload automatico:** alla successiva lettura di `engine.Selectors` (ad esempio al prossimo messaggio, cambio chat o `SelfTestAsync`), il file modificato viene ricaricato automaticamente senza riavviare l'app.
- **Sicurezza:** se il file JSON è malformato o contiene errori di sintassi, viene ignorato con un log `[Selectors] Override locale ignorato ...` e l'app continua a usare i selettori OTA/default.

### Esempio pratico (`gemini.json`)
```json
{
  "StopButton": "button[aria-label*='Interrompi'], button[data-testid='stop-btn']",
  "TextArea": "div[role='textbox'][contenteditable='true']"
}
```

---

## 2. Come interpretare `IChatWebEngine.SelfTestAsync(IPage)`

`SelfTestAsync(page)` esegue un check rapido sul DOM corrente e restituisce un `EngineSelfTestReport`:

- `AllCriticalFound`: `true` se **tutti** i selettori critici (`TextArea`, `SendButton`) sono presenti nel DOM.
- `Checks`: elenco di `SelectorCheckResult(Name, Selector, Found, MatchCount, Note)`:
  - `Note == null` → selettore **critico** (`TextArea`, `SendButton`). Se `Found == false`, l'invio dei prompt è compromesso.
  - `Note == "optional"` → selettore **contestuale** (`StopButton` è visibile solo durante la generazione, `MessageElements` può essere 0 su una chat nuova vuota, `UploadButton` dipende dal layout).

Esempio di output nei log di debug:
```
[SelfTest:gemini] CriticalOK=True | TextArea=OK(1), SendButton=OK(1), StopButton=MISSING, MessageElements=OK(4), UploadButton=OK(1)
```

---

## 3. Checklist per creare un nuovo Engine

1. **Cartella dedicata:** crea `Services/WebChat/Engines/<Provider>/<Provider>WebEngine.cs` (tutti i literal CSS/URL del sito devono vivere **solo** qui dentro, verificato dal test E3).
2. **Implementa `IChatWebEngine`:**
   - `Descriptor`: scegli un `Id` stabile (es. `"claude"`), `DisplayName`, `ProfileFolderName` (es. `"ClaudeProfile"` — non cambiarlo mai dopo il rilascio per preservare i cookie degli utenti) e `BaseUrl`.
   - `Selectors`: risolvi sempre tramite `SelectorLayerResolver.Resolve(Descriptor.Id, _defaultSelectors, _selectorsService?.Current)`.
   - `BuildNewChatUrl`, `BuildChatUrl`, `IsOnEngineHost`, `TryExtractChatId`.
   - `ClassifyAsync` e `ReadMessageAsync` (usa `FileBlockHtmlProtector.ProtectCustomTagsInFileBlocks(html)` prima di `ReverseMarkdown` per preservare tag XML/`.csproj` non racchiusi in `<pre><code>`, e normalizza eventuali URL `//...` in `https://...`).
   - `BuildStopButtonSelector()`, `DetectBlockAsync(page)`, `IsSpaCrash(response)`.
   - `PreparePageAsync(page)`, `AttachFilesAsync(...)`, `NavigateToChatAsync(...)`, `NavigateToNewChatAsync(...)`, `GetChatTitleAsync(...)`.
   - `SelfTestAsync(page)`.
3. **Registrazione in `EngineRegistry`:**
   In `Services/WebChat/EngineRegistry.cs`:
   ```csharp
   registry.Register("claude", (selectors, ui) => new ClaudeWebEngine(selectors, new ClaudeLoginFlow(ui)));
   ```
4. **Fixture HTML + Suite di conformità:**
   - Aggiungi `AIBridge.WebChat.Tests/Fixtures/<provider>_streaming.html` con i selettori minimi e gli helper JS (`window.testApi`).
   - Crea `<Provider>WebEngineContractTests : ChatWebEngineContractTests<<Provider>WebEngine>` in `AIBridge.WebChat.Tests/`: tutti i test di contratto (`Descriptor`, URL round-trip, `ClassifyAndRead`, `StopButtonSelector`, `DetectBlock`, `SelfTestAsync`) verranno eseguiti automaticamente.

---

## 4. Template minimo di Engine

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

    // Implementa i restanti membri di IChatWebEngine...
}
```

