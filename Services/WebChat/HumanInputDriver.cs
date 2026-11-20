using System.Diagnostics;
using Microsoft.Playwright;

namespace AIBridge.Services.WebChat;

/// <summary>
/// Driver di input "umano" (Unit 6): movimenti mouse con offset casuale sul bounding box,
/// pause pre-focus/pre-invio, inserimento testo (FillAsync sotto soglia, JS injection sopra
/// <see cref="EngineTimings.LongMessageThresholdChars"/> con fallback), trigger spazio+backspace
/// per svegliare gli handler Angular/React, e invio tramite pulsante Send o Ctrl+Enter.
/// </summary>
public sealed class HumanInputDriver : IHumanInput
{
    private static readonly Random _rng = new();

    private readonly Func<IPage?> _pageProvider;
    private readonly IChatWebEngine _engine;
    private readonly bool _fastMode;

    public HumanInputDriver(BrowserHost browserHost, IChatWebEngine engine, bool fastMode = false)
        : this(() => browserHost.Page, engine, fastMode)
    {
    }

    public HumanInputDriver(Func<IPage?> pageProvider, IChatWebEngine engine, bool fastMode = false)
    {
        _pageProvider = pageProvider ?? throw new ArgumentNullException(nameof(pageProvider));
        _engine       = engine       ?? throw new ArgumentNullException(nameof(engine));
        _fastMode     = fastMode;
    }

    /// <summary>
    /// Delay randomico tra <paramref name="minMs"/> e <paramref name="maxMs"/> ms.
    /// In modalità test (<c>fastMode = true</c>) ritorna immediatamente.
    /// </summary>
    public Task DelayAsync(int minMs, int maxMs)
    {
        if (_fastMode) return Task.CompletedTask;
        return Task.Delay(_rng.Next(minMs, maxMs));
    }

    /// <summary>
    /// Muove il mouse verso il centro dell'elemento con un piccolo offset casuale,
    /// poi esegue il click con coordinate relative e <c>Force = true</c>.
    /// </summary>
    public async Task ClickAsync(ILocator locator)
    {
        var page = _pageProvider();
        try
        {
            var box = await locator.BoundingBoxAsync();
            if (box != null && page != null)
            {
                float offsetX = (float)(_rng.NextDouble() * box.Width * 0.6 + box.Width * 0.2);
                float offsetY = (float)(_rng.NextDouble() * box.Height * 0.6 + box.Height * 0.2);
                await page.Mouse.MoveAsync(box.X + offsetX, box.Y + offsetY);
                await DelayAsync(40, 100);

                await locator.ClickAsync(new LocatorClickOptions
                {
                    Position = new Position { X = offsetX, Y = offsetY },
                    Force = true
                });
            }
            else
            {
                await locator.ClickAsync();
            }
        }
        catch
        {
            await locator.ClickAsync();
        }
    }

    /// <summary>
    /// Digita una stringa carattere per carattere con delay casuali tra 40–120ms.
    /// </summary>
    public async Task TypeAsync(string text)
    {
        var page = _pageProvider();
        if (page == null) return;

        foreach (char c in text)
        {
            await page.Keyboard.TypeAsync(c.ToString());
            await DelayAsync(40, 120);
            if (_rng.NextDouble() < 0.05)
                await DelayAsync(200, 500);
        }
    }

    /// <summary>
    /// Attende la textbox, la focalizza, pulisce il testo precedente, inserisce <paramref name="message"/>
    /// (FillAsync se &lt;= <see cref="EngineTimings.LongMessageThresholdChars"/>, altrimenti JS injection
    /// con fallback) ed esegue il trigger spazio+backspace per aggiornare lo stato di Angular/React.
    /// </summary>
    public async Task InsertPromptAsync(string message)
    {
        var page = _pageProvider();
        if (page == null) return;

        // 1. Attesa pre-focus (simula il tempo di avvicinamento al mouse)
        await DelayAsync(150, 400);

        var textAreaSelector = _engine.Selectors.TextArea;
        int waitTimeout = _engine.Timings.TextAreaWaitTimeoutMs > 0
            ? _engine.Timings.TextAreaWaitTimeoutMs
            : 15_000;

        await page.WaitForSelectorAsync(textAreaSelector, new PageWaitForSelectorOptions
        {
            Timeout = waitTimeout
        });

        // 2. Muovi il mouse verso la textbox prima di cliccarci
        var textArea = page.Locator(textAreaSelector).First;
        var box = await textArea.BoundingBoxAsync();
        if (box != null)
        {
            float ox = (float)(_rng.NextDouble() * box.Width * 0.6 + box.Width * 0.2);
            float oy = (float)(_rng.NextDouble() * box.Height * 0.6 + box.Height * 0.2);
            await page.Mouse.MoveAsync(box.X + ox, box.Y + oy);
            await DelayAsync(80, 200);
        }

        await page.FocusAsync(textAreaSelector);
        await DelayAsync(100, 250);

        // 3. Pulisci eventuale testo precedente
        await page.Keyboard.PressAsync("Control+A");
        await DelayAsync(60, 120);
        await page.Keyboard.PressAsync("Backspace");
        await DelayAsync(80, 180);

        // 4. Strategia di inserimento testo (FillAsync vs JS injection per testi lunghi)
        int threshold = _engine.Timings.LongMessageThresholdChars > 0
            ? _engine.Timings.LongMessageThresholdChars
            : 1_000;

        try
        {
            if (message.Length > threshold)
            {
                await InjectTextViaJsAsync(page, textAreaSelector, message);
            }
            else
            {
                await page.Locator(textAreaSelector).First.FillAsync(message);
            }
            await DelayAsync(300, 600);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SendPrompt] FillAsync/JS insertion failed, falling back to JS: {ex.Message}");
            await InjectTextViaJsAsync(page, textAreaSelector, message);
            await DelayAsync(300, 500);
        }

        // 5. Evento di trigger (spazio + backspace) per aggiornare lo stato di React/Angular
        await page.FocusAsync(textAreaSelector);
        await DelayAsync(100, 200);

        await page.Keyboard.PressAsync(" ");
        await DelayAsync(50, 100);
        await page.Keyboard.PressAsync("Backspace");
        await DelayAsync(100, 250);

        // 6. Pausa pre-invio (simula rilettura prima di inviare)
        await DelayAsync(400, 900);
    }

    /// <summary>
    /// Invia il prompt cliccando sul pulsante Send (se visibile e abilitato)
    /// oppure premendo <c>Control+Enter</c> sulla textbox.
    /// </summary>
    public async Task SubmitAsync()
    {
        var page = _pageProvider();
        if (page == null) return;

        var textAreaSelector = _engine.Selectors.TextArea;
        var sendBtnSelector = _engine.Selectors.SendButton;
        var sendBtn = page.Locator(sendBtnSelector).First;

        if (await sendBtn.CountAsync() > 0 && await sendBtn.IsVisibleAsync() && await sendBtn.IsEnabledAsync())
        {
            var btnBox = await sendBtn.BoundingBoxAsync();
            if (btnBox != null)
            {
                float ox = (float)(_rng.NextDouble() * btnBox.Width * 0.6 + btnBox.Width * 0.2);
                float oy = (float)(_rng.NextDouble() * btnBox.Height * 0.6 + btnBox.Height * 0.2);
                await page.Mouse.MoveAsync(btnBox.X + ox, btnBox.Y + oy);
                await DelayAsync(80, 200);
            }
            await ClickAsync(sendBtn);
        }
        else
        {
            var textArea = page.Locator(textAreaSelector).First;
            var box = await textArea.BoundingBoxAsync();
            if (box != null)
            {
                await page.Mouse.MoveAsync(
                    box.X + box.Width + _rng.Next(20, 60),
                    box.Y + _rng.Next(-10, 10)
                );
                await DelayAsync(80, 200);
            }
            await page.FocusAsync(textAreaSelector);
            await DelayAsync(50, 100);
            await page.Keyboard.PressAsync("Control+Enter");
        }
    }

    private static Task InjectTextViaJsAsync(IPage page, string selector, string text)
    {
        return page.EvaluateAsync(@"(args) => {
            const el = document.querySelector(args.selector);
            if (el) {
                el.focus();
                el.innerText = args.text;
                el.dispatchEvent(new Event('input', { bubbles: true }));
                el.dispatchEvent(new Event('change', { bubbles: true }));
            }
        }", new { selector, text });
    }
}

