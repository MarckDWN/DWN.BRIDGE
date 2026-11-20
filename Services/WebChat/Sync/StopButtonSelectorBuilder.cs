namespace AIBridge.Services.WebChat.Sync;

/// <summary>
/// Costruisce il selettore composto del pulsante Stop aggiungendo il pseudo-filtro Playwright
/// <c>:near(textAreaSelector, 300)</c> a ogni parte del selettore grezzo.
/// Estratto da <c>GeminiBrowserService.GetStopButtonSelector()</c> (righe 60-71).
/// Funzione pura: nessuna dipendenza su Playwright o stato esterno.
/// </summary>
public static class StopButtonSelectorBuilder
{
    private const int NearDistancePx = 300;

    /// <summary>
    /// <paramref name="rawStopSelector"/> è il valore di <c>EngineSelectors.StopButton</c>
    /// (può contenere più selettori separati da virgola).<br/>
    /// <paramref name="textAreaSelector"/> è il selettore della textarea; se vuoto usa il fallback
    /// Gemini <c>div[role='textbox']</c>.
    /// </summary>
    public static string Build(string? rawStopSelector, string? textAreaSelector)
    {
        if (string.IsNullOrWhiteSpace(textAreaSelector))
            textAreaSelector = "div[role='textbox']";

        if (string.IsNullOrWhiteSpace(rawStopSelector))
            return $"button[aria-label*='Stop']:near({textAreaSelector}, {NearDistancePx})";

        var parts = rawStopSelector.Split(',');
        for (int i = 0; i < parts.Length; i++)
            parts[i] = $"{parts[i].Trim()}:near({textAreaSelector}, {NearDistancePx})";

        return string.Join(", ", parts);
    }
}
