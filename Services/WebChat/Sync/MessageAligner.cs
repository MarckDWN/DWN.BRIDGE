namespace AIBridge.Services.WebChat.Sync;

/// <summary>
/// Snapshot di un elemento messaggio già letto dal DOM (testo estratto, tag normalizzato).
/// Passato a <see cref="MessageAligner"/> per evitare dipendenze su Playwright.
/// </summary>
public sealed record DomMessageSnapshot(string Text, string Tag);

/// <summary>
/// Allineamento testuale tra gli elementi DOM visibili e la lista dei messaggi già elaborati.
/// Estratto dalla parte pura di <c>GeminiBrowserService.GetFirstNewElementIndexAsync</c>
/// (righe 601–717). Nessuna dipendenza su Playwright, stato esterno o I/O.
///
/// La parte con EvaluateAsync sull'attributo <c>data-aibridge-processed</c> (righe 572–599)
/// rimane nel chiamante perché richiede un round-trip CDP.
/// </summary>
public static class MessageAligner
{
    /// <summary>
    /// Restituisce l'indice del primo elemento in <paramref name="domSnapshots"/> che non è
    /// ancora presente in <paramref name="processed"/>.
    /// </summary>
    /// <param name="domSnapshots">Elementi DOM nell'ordine in cui compaiono nella pagina.</param>
    /// <param name="processed">Testi dei messaggi già elaborati (nell'ordine di arrivo).</param>
    /// <param name="waitingForResponse">True se è in corso un SendPromptAsync programmatico.</param>
    /// <param name="isChatBusy">Valore corrente di IsChatBusy del service.</param>
    public static int FindFirstNewIndex(
        IReadOnlyList<DomMessageSnapshot> domSnapshots,
        IReadOnlyList<string> processed,
        bool waitingForResponse,
        bool isChatBusy)
    {
        if (domSnapshots.Count == 0) return 0;

        // Pivot: ultimo elemento visibile (se vuoto = in generazione, usa il penultimo)
        int pivotDom = domSnapshots.Count - 1;
        var pivot = domSnapshots[pivotDom];
        if (string.IsNullOrEmpty(pivot.Text) && domSnapshots.Count > 1)
        {
            pivotDom--;
            pivot = domSnapshots[pivotDom];
        }

        // Fase 1: cerca il pivot nella lista dei processati (a ritroso, con verifica contesto)
        int matchPIdx = FindMatch(domSnapshots, processed, pivotDom);
        if (matchPIdx != -1)
            return pivotDom + 1;

        // Fase 2: prova elementi precedenti nel DOM come pivot alternativo
        for (int i = domSnapshots.Count - 2; i >= 0; i--)
        {
            if (string.IsNullOrEmpty(domSnapshots[i].Text)) continue;

            matchPIdx = FindMatch(domSnapshots, processed, i);
            if (matchPIdx != -1)
                return i + 1;
        }

        // Failsafe: se non siamo in attesa di generazione attiva, tutto il DOM visibile
        // è probabilmente storia già elaborata rientrata nel viewport per scrolling.
        if (!waitingForResponse && !isChatBusy && processed.Count > 0)
            return domSnapshots.Count; // 0 elementi nuovi

        // Nessuna corrispondenza: tratta tutto il DOM come nuovo (es. reset/nuova chat)
        return 0;
    }

    // ── Helper privati ────────────────────────────────────────────────────────

    private static int FindMatch(
        IReadOnlyList<DomMessageSnapshot> dom,
        IReadOnlyList<string> processed,
        int domPivot)
    {
        var pivotText = dom[domPivot].Text;
        for (int j = processed.Count - 1; j >= 0; j--)
        {
            if (Normalize(processed[j]) != Normalize(pivotText)) continue;

            // Verifica fino a 2 elementi precedenti per evitare falsi positivi su duplicati
            if (Verify(dom, processed, domPivot - 1, j - 1))
                return j;
        }
        return -1;
    }

    private static bool Verify(
        IReadOnlyList<DomMessageSnapshot> dom,
        IReadOnlyList<string> processed,
        int domIdx, int pIdx)
    {
        int checks = 0;
        while (domIdx >= 0 && pIdx >= 0 && checks < 2)
        {
            if (Normalize(dom[domIdx].Text) != Normalize(processed[pIdx]))
                return false;
            domIdx--; pIdx--; checks++;
        }
        return true;
    }

    /// <summary>Normalizza il testo per il confronto: CRLF→LF, CR→LF, Trim. Usato nei test e in GeminiWebEngine.</summary>
    public static string Normalize(string? s)
    {
        if (s == null) return string.Empty;
        return s.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
    }
}
