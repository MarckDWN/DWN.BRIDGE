using AIBridge.Services.WebChat.Sync;
using Microsoft.Playwright;
using System.Diagnostics;

namespace AIBridge.Services.WebChat.Engines.Gemini;

/// <summary>
/// Tutta la "conoscenza del sito" di Gemini.google.com.
/// Implementa IChatWebEngine: URL, selettori, lettura messaggi,
/// block detection, navigazione, allegati, titolo chat.
///
/// GeminiBrowserService non contiene più literal Gemini (verificato dal test E3).
/// </summary>
public sealed class GeminiWebEngine : IChatWebEngine
{
    private const string GeminiHost     = "gemini.google.com";
    private const string GeminiAppBase  = "https://gemini.google.com/app";
    private const string UpsellCss      = "[id*='upsell'],[class*='upsell'],[id*='popover'],.experimental-mode-upsell{display:none!important;}";

    private readonly ISelectorsService? _selectorsService;
    private readonly ILoginFlow _loginFlow;
    private readonly string? _overrideDirectory;
    private readonly EngineSelectors _defaultSelectors = CreateDefaultSelectors();

    public GeminiWebEngine(
        ISelectorsService? selectorsService = null,
        ILoginFlow? loginFlow = null,
        string? overrideDirectory = null)
    {
        _selectorsService  = selectorsService;
        _loginFlow         = loginFlow ?? new GeminiLoginFlow();
        _overrideDirectory = overrideDirectory;
    }

    // ── Descrittore / configurazione ─────────────────────────────────────────

    public EngineDescriptor Descriptor { get; } = new EngineDescriptor(
        Id:                "gemini",
        DisplayName:       "Google Gemini",
        ProfileFolderName: "GeminiProfile",
        BaseUrl:           GeminiAppBase
    );

    public EngineTimings Timings { get; } = new EngineTimings();

    public EngineSelectors Selectors =>
        SelectorLayerResolver.Resolve(
            Descriptor.Id,
            _defaultSelectors,
            _selectorsService?.Current,
            _overrideDirectory);

    private static EngineSelectors CreateDefaultSelectors()
    {
        var selectors = EngineSelectors.FromDomSelectors(new AIBridge.Shared.Models.DomSelectors());
        selectors.AttachButton =
            "button[aria-label='Carica immagine'], button[aria-label='Upload image'], " +
            "button[aria-label='Carica file'], button[aria-label='Upload file'], " +
            "button[aria-label='Carica immagine o file'], button[aria-label='Upload image or file']";
        selectors.UploadMenuTextRegex = "(?i)computer|file|carica|upload";
        selectors.SignedOutIndicators =
            "[data-test-id=\"sidenav-error-action-link\"], " +
            "[data-test-id=\"sidenav-error-message\"], " +
            "[data-test-id=\"signed-out-highly-regulated-zero-state-disclaimer\"]";
        selectors.SignInLink   = "a[href*='ServiceLogin']";
        selectors.SignInButton = "[data-test-id='sign-in-button']";
        selectors.CaptchaFrame = "iframe[src*='recaptcha']";
        selectors.CodeBlock    = "pre code";
        selectors.HidePopupsCss = UpsellCss;
        selectors.ChatLinkSelectorTemplates = new[]
        {
            "a[href*='/app/{id}']",
            "[data-conversation-id='{id}']"
        };
        return selectors;
    }

    // ── URL / identità chat ───────────────────────────────────────────────────

    public string BuildNewChatUrl(string language)
        => $"{GeminiAppBase}?hl={language}";

    public string BuildChatUrl(string chatId, string language, string? authUser)
    {
        var url = $"{GeminiAppBase}/{chatId}?hl={language}";
        if (!string.IsNullOrEmpty(authUser))
            url += $"&authuser={authUser}";
        return url;
    }

    public bool IsOnEngineHost(string url)
        => url.Contains(GeminiHost);

    public string? TryExtractChatId(string url)
    {
        try
        {
            var uri = new Uri(url);
            if (!uri.Host.Contains(GeminiHost)) return null;
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // /app/<chatId>
            if (segments.Length >= 2 && segments[0] == "app")
                return segments[1];
        }
        catch { }
        return null;
    }

    // ── Modello dei messaggi ──────────────────────────────────────────────────

    public string MessageElementsSelector
        => Selectors.MessageElements;

    public async Task<MessageRole> ClassifyAsync(ILocator element)
    {
        try
        {
            var tag = await element.EvaluateAsync<string>("el => el.tagName.toLowerCase()");
            return tag == "query-content" ? MessageRole.User : MessageRole.Assistant;
        }
        catch
        {
            return MessageRole.Unknown;
        }
    }

    public async Task<string> ReadMessageAsync(ILocator locator, MessageRole role, IPage page)
    {
        if (role == MessageRole.User)
        {
            return ((await locator.InnerTextAsync()) ?? "").Trim();
        }

        // ── Assistant: HTML → Markdown con ReverseMarkdown ──
        string html = "";
        try
        {
            html = await locator.InnerHTMLAsync() ?? "";

            // Normalizza URL protocol-relative (es. src="//www.gstatic.com/...") in https://
            // per evitare che ReverseMarkdown generi ![](//...) interpretato da Windows come UNC path.
            html = System.Text.RegularExpressions.Regex.Replace(
                html, @"(\bsrc=[""'])//", "$1https://", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            // Protegge eventuali tag XML/.csproj o generic C# scritti dall'LLM dentro @@@FILE:...@@@
            // senza blocco ```xml / ```csharp prima che ReverseMarkdown (UnknownTagsOption.Bypass) li cancelli.
            html = FileBlockHtmlProtector.ProtectCustomTagsInFileBlocks(html);

            // Sostituisce blob: URL delle immagini con data: base64 URI (con try/catch JS e onerror)
            if (html.Contains("blob:"))
            {
                var blobUrls = new System.Text.RegularExpressions.Regex(@"blob:https?://[a-zA-Z0-9\.\-_]+/[a-zA-Z0-9\-_]+")
                    .Matches(html);
                foreach (System.Text.RegularExpressions.Match m in blobUrls)
                {
                    try
                    {
                        var blobUrl = m.Value;
                        var jsCode = @"async (url) => {
                            try {
                                const response = await fetch(url);
                                const blob = await response.blob();
                                return new Promise((resolve) => {
                                    const reader = new FileReader();
                                    reader.onloadend = () => resolve(reader.result);
                                    reader.onerror = () => resolve('');
                                    reader.readAsDataURL(blob);
                                });
                            } catch (e) {
                                return '';
                            }
                        }";
                        var b64 = await page.EvaluateAsync<string>(jsCode, blobUrl);
                        if (!string.IsNullOrEmpty(b64))
                            html = html.Replace(blobUrl, b64);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[BlobConverter] Error converting blob URL: {ex.Message}");
                    }
                }
            }
        }
        catch { }

        string text;
        try
        {
            var config = new ReverseMarkdown.Config
            {
                GithubFlavored    = true,
                RemoveComments    = true,
                SmartHrefHandling = true,
                UnknownTags       = ReverseMarkdown.Config.UnknownTagsOption.Bypass
            };
            var converter = new ReverseMarkdown.Converter(config);
            text = converter.Convert(html);
            text = System.Net.WebUtility.HtmlDecode(text);
        }
        catch
        {
            text = await locator.InnerTextAsync() ?? "";
        }

        // Aggiungi blocchi ```tool``` per i code block che contengono JSON con "action"
        try
        {
            var codeBlocks = await locator.Locator("pre code").AllAsync();
            foreach (var codeBlock in codeBlocks)
            {
                var codeText  = await codeBlock.InnerTextAsync();
                var className = await codeBlock.GetAttributeAsync("class") ?? "";

                bool isTool = className.Contains("language-tool") ||
                              className.Contains("lang-tool") ||
                              className.Equals("tool") ||
                              (codeText.Contains("\"action\"") &&
                               (codeText.Trim().StartsWith("{") || codeText.Trim().StartsWith("[")));

                if (isTool && !text.Contains("```tool"))
                    text += $"\n\n```tool\n{codeText.Trim()}\n```";
            }
        }
        catch { }

        return text.Trim();
    }

    // ── Stato pagina ──────────────────────────────────────────────────────────

    public string BuildStopButtonSelector()
        => StopButtonSelectorBuilder.Build(Selectors.StopButton, Selectors.TextArea);

    public async Task<BlockReason?> DetectBlockAsync(IPage page)
    {
        var url = page.Url;

        if (url.Contains("google.com/sorry"))             return BlockReason.Captcha;
        if (url.Contains("accounts.google.com/signin"))   return BlockReason.LoginLink;
        if (url.Contains("accounts.google.com"))          return BlockReason.SignedOut;

        try
        {
            // Sidenav data-test-id: stabile, indipendente dalla lingua
            var isSignedOut = await page.EvaluateAsync<bool>(
                "() => !!(document.querySelector('[data-test-id=\"sidenav-error-action-link\"]') || " +
                "         document.querySelector('[data-test-id=\"sidenav-error-message\"]') || " +
                "         document.querySelector('[data-test-id=\"signed-out-highly-regulated-zero-state-disclaimer\"]'))");
            if (isSignedOut) return BlockReason.SignedOut;
        }
        catch { }

        try
        {
            var signInLinks = page.Locator("a[href*='ServiceLogin']");
            if (await signInLinks.CountAsync() > 0 &&
                await signInLinks.First.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 100 }))
                return BlockReason.LoginLink;
        }
        catch { }

        try
        {
            var captcha = page.Locator("iframe[src*='recaptcha']");
            if (await captcha.CountAsync() > 0 && await captcha.IsVisibleAsync())
                return BlockReason.Captcha;
        }
        catch { }

        return null; // pagina utilizzabile
    }

    public bool IsSpaCrash(IResponse response)
        => response.Url.Contains("batchexecute") && response.Status == 0;

    // ── Azioni sito-specifiche ────────────────────────────────────────────────

    public async Task PreparePageAsync(IPage page)
    {
        try
        {
            await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = UpsellCss });
        }
        catch { }
    }

    public async Task AttachFilesAsync(IPage page, string[] filePaths, IHumanInput input)
    {
        var addBtn = page.Locator(
            "button[aria-label='Carica immagine'], button[aria-label='Upload image'], " +
            "button[aria-label='Carica file'], button[aria-label='Upload file'], " +
            "button[aria-label='Carica immagine o file'], button[aria-label='Upload image or file']"
        ).First;

        if (await addBtn.IsVisibleAsync())
        {
            await input.DelayAsync(300, 600);
            await input.ClickAsync(addBtn);
            await input.DelayAsync(600, 1000);
        }
        else
        {
            try
            {
                var leftBtn = page.Locator($"button:left-of({Selectors.TextArea})").First;
                if (await leftBtn.IsVisibleAsync())
                {
                    await input.DelayAsync(300, 600);
                    await input.ClickAsync(leftBtn);
                    await input.DelayAsync(600, 1000);
                }
            }
            catch { }
        }

        var fileChooser = await page.RunAndWaitForFileChooserAsync(async () =>
        {
            var menuItems = page.Locator(Selectors.MenuItems);
            if (await menuItems.CountAsync() > 0)
            {
                var computerUpload = menuItems.Filter(new()
                {
                    HasTextRegex = new System.Text.RegularExpressions.Regex("(?i)computer|file|carica|upload")
                }).First;

                if (await computerUpload.IsVisibleAsync())
                    await computerUpload.ClickAsync();
                else
                    await menuItems.First.ClickAsync();
            }
        });

        await fileChooser.SetFilesAsync(filePaths);
        await Task.Delay(5000);
    }

    public async Task<NavigationResult> NavigateToChatAsync(IPage page, string chatId, NavigationContext context)
    {
        var lang = context.Language;

        // 1. Cold-boot: se non siamo ancora su Gemini, naviga alla home prima
        if (!page.Url.Contains($"{GeminiHost}/app"))
        {
            try
            {
                await page.GotoAsync(BuildNewChatUrl(lang),
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 8000 });

                if (await DetectBlockAsync(page) != null)
                    return NavigationResult.LoginRequired;

                await page.WaitForSelectorAsync(Selectors.TextArea,
                    new PageWaitForSelectorOptions { Timeout = 5000 });
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GeminiEngine] Cold boot failed: {ex.Message}");
                if (await DetectBlockAsync(page) != null)
                    return NavigationResult.LoginRequired;
            }
        }

        // Se siamo già esattamente su questa chat e i messaggi sono presenti nel DOM, non ri-navigare
        if (!string.IsNullOrEmpty(chatId) && TryExtractChatId(page.Url) == chatId)
        {
            int existingCount = 0;
            try { existingCount = await page.Locator(Selectors.MessageElements).CountAsync(); } catch { }
            if (existingCount > 0)
                return NavigationResult.Ok;
        }

        // 2. Estrai authuser dalla sessione corrente
        string authUser = TryExtractAuthUser(page.Url);

        // Marca tutti gli elementi della chat precedente come 'stale' prima della transizione SPA,
        // così WaitForSelector/stabilizzazione non scambiano i vecchi messaggi per quelli della nuova chat.
        try
        {
            await page.EvaluateAsync(
                @"(selector) => {
                    document.querySelectorAll(selector).forEach(el => el.setAttribute('data-aibridge-stale', 'true'));
                }",
                Selectors.MessageElements);
        }
        catch { }

        // 3. Transizione SPA tramite link injection (più affidabile del Goto)
        bool spaOk = false;
        try
        {
            string js = $@"(() => {{
                const href = '/app/{chatId}' + ({(string.IsNullOrEmpty(authUser) ? "''" : $"'?authuser={authUser}'")});
                let link = document.querySelector(`a[href*='${{href}}']`);
                if (!link) {{
                    link = document.createElement('a');
                    link.href = href;
                    link.style.display = 'none';
                    document.body.appendChild(link);
                    link.click();
                    setTimeout(() => {{ try {{ link.remove(); }} catch(e) {{}} }}, 3000);
                }} else {{
                    link.click();
                }}
                return true;
            }})();";

            await page.EvaluateAsync(js);

            for (int i = 0; i < 15; i++)
            {
                if (page.Url.Contains(chatId)) { spaOk = true; break; }
                await Task.Delay(200);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[GeminiEngine] SPA transition failed: {ex.Message}");
        }

        // 4. Fallback: navigazione classica
        if (!spaOk)
        {
            await page.GotoAsync(BuildChatUrl(chatId, lang, authUser),
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 8000 });
        }

        // 5. Verifica blocco post-navigazione
        if (await DetectBlockAsync(page) != null)
            return NavigationResult.LoginRequired;

        // 6. Attendi DOM: messaggi NON-stale della nuova chat OPPURE blocco OPPURE redirect root
        var freshSelector = string.Join(", ",
            Selectors.MessageElements
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => $"{s}:not([data-aibridge-stale])"));

        var tMessages = page.WaitForSelectorAsync(freshSelector,
            new PageWaitForSelectorOptions { Timeout = 8000 });
        var tBlock = page.WaitForSelectorAsync(
            "[data-test-id='sign-in-button'], iframe[src*='recaptcha']",
            new PageWaitForSelectorOptions { Timeout = 8000 });
        var tRedirect = page.WaitForURLAsync(
            url => !string.IsNullOrEmpty(chatId) && !url.Contains(chatId) && url.Contains("/app"),
            new PageWaitForURLOptions { Timeout = 8000 });

        _ = tMessages.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        _ = tBlock.ContinueWith(t => t.Exception,    TaskContinuationOptions.OnlyOnFaulted);
        _ = tRedirect.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        try
        {
            var first = await Task.WhenAny(tMessages, tBlock, tRedirect);

            if (first == tBlock && tBlock.IsCompletedSuccessfully)
                return NavigationResult.LoginRequired;

            if (first == tMessages && tMessages.IsCompletedSuccessfully)
            {
                if (!string.IsNullOrEmpty(chatId) && !page.Url.Contains(chatId))
                {
                    if (await DetectBlockAsync(page) != null)
                        return NavigationResult.LoginRequired;

                    return NavigationResult.ChatNotFound;
                }

                return NavigationResult.Ok;
            }

            // Redirect rilevato oppure timeout su tMessages (nessun messaggio non-stale comparso per questa chat)
            var second = await Task.WhenAny(tMessages, tBlock, Task.Delay(1500));
            if (second == tBlock && tBlock.IsCompletedSuccessfully)
                return NavigationResult.LoginRequired;

            if (await DetectBlockAsync(page) != null)
                return NavigationResult.LoginRequired;

            int freshCount = 0;
            try { freshCount = await page.Locator(freshSelector).CountAsync(); } catch { }

            if ((!string.IsNullOrEmpty(chatId) && !page.Url.Contains(chatId)) || freshCount == 0)
            {
                return NavigationResult.ChatNotFound;
            }
        }
        catch { }

        return NavigationResult.Ok;
    }

    public async Task NavigateToNewChatAsync(IPage page, NavigationContext context)
    {
        await page.GotoAsync(BuildNewChatUrl(context.Language),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 8000 });
        await Task.Delay(800);
    }

    public async Task<string?> GetChatTitleAsync(IPage page, string chatId)
    {
        if (string.IsNullOrEmpty(chatId)) return null;
        try
        {
            var candidates = new[]
            {
                $"a[href*='{chatId}']",
                $"[data-conversation-id='{chatId}']",
                $"[href='/app/{chatId}']"
            };

            foreach (var selector in candidates)
            {
                var el = page.Locator(selector).First;
                if (await el.CountAsync() > 0)
                {
                    var text = (await el.InnerTextAsync())?.Trim();
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }

            var allLinks = await page.QuerySelectorAllAsync("a[href]");
            foreach (var link in allLinks)
            {
                var href = await link.GetAttributeAsync("href") ?? "";
                if (href.Contains(chatId))
                {
                    var text = await link.InnerTextAsync();
                    if (!string.IsNullOrEmpty(text?.Trim())) return text!.Trim();
                }
            }
        }
        catch { }
        return null;
    }

    // ── Login flow & Self-Test (Unit 8) ──────────────────────────────────────

    public ILoginFlow? LoginFlow => _loginFlow;

    public async Task<EngineSelfTestReport> SelfTestAsync(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var current = Selectors;
        var checks = new List<SelectorCheckResult>
        {
            await CheckSelectorAsync(page, "TextArea",        current.TextArea,        optional: false),
            await CheckSelectorAsync(page, "SendButton",      current.SendButton,      optional: false),
            await CheckSelectorAsync(page, "StopButton",      BuildStopButtonSelector(), optional: true),
            await CheckSelectorAsync(page, "MessageElements", current.MessageElements, optional: true),
            await CheckSelectorAsync(page, "UploadButton",    current.UploadButton,    optional: true),
        };

        var report = new EngineSelfTestReport(Descriptor.Id, DateTimeOffset.UtcNow, checks);
        Debug.WriteLine($"[SelfTest:{Descriptor.Id}] CriticalOK={report.AllCriticalFound} | " +
            string.Join(", ", checks.Select(c => $"{c.Name}={(c.Found ? $"OK({c.MatchCount})" : "MISSING")}")));
        return report;
    }

    private static async Task<SelectorCheckResult> CheckSelectorAsync(
        IPage page, string name, string selector, bool optional)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return new SelectorCheckResult(name, selector, false, 0, optional ? "optional" : "empty");
        }

        try
        {
            int count = await page.Locator(selector).CountAsync();
            return new SelectorCheckResult(name, selector, count > 0, count, optional ? "optional" : null);
        }
        catch (Exception ex)
        {
            return new SelectorCheckResult(name, selector, false, 0, $"error: {ex.Message}");
        }
    }

    // ── Helpers privati ───────────────────────────────────────────────────────

    private static string TryExtractAuthUser(string url)
    {
        try
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                new Uri(url).Query, @"[?&]authuser=([^&]+)");
            return m.Success ? m.Groups[1].Value : string.Empty;
        }
        catch { return string.Empty; }
    }
}
