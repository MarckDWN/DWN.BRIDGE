using System.Reflection;
using System.IO;

namespace AIBridge.Services.Extensions;

/// <summary>
/// Sistema di caricamento per estensioni generiche di AIBridge.
/// Scansiona la cartella 'extensions' per trovare DLL contenenti tipi marcati
/// con [AIBridgeExtension] che implementano ISessionAdapter (o future interfacce).
/// 
/// Questo permette alla community e alle aziende di estendere l'applicazione
/// senza dover modificare o ricompilare il codice core pubblico.
/// </summary>
public static class ExtensionHost
{
    private static readonly string ExtensionsPath = Path.Combine(AppContext.BaseDirectory, "extensions");

    /// <summary>
    /// Tenta di caricare un ISessionAdapter da un'estensione.
    /// Se esistono più estensioni, carica la prima trovata.
    /// Restituisce null se nessuna estensione valida è presente.
    /// </summary>
    public static ISessionAdapter? TryLoadSessionAdapter()
    {
        if (!Directory.Exists(ExtensionsPath))
            return null;

        foreach (var dll in Directory.GetFiles(ExtensionsPath, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                
                // Cerca tipi pubblici con l'attributo AIBridgeExtension che implementano ISessionAdapter
                var adapterType = assembly.GetExportedTypes().FirstOrDefault(t =>
                    t.GetCustomAttribute<AIBridgeExtensionAttribute>() != null &&
                    typeof(ISessionAdapter).IsAssignableFrom(t) &&
                    !t.IsAbstract && !t.IsInterface);

                if (adapterType != null)
                {
                    return (ISessionAdapter?)Activator.CreateInstance(adapterType);
                }
            }
            catch
            {
                // Ignora silenziosamente DLL non compatibili (es. dipendenze native o runtime differenti)
            }
        }

        return null;
    }
}
