using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AIBridge.Services.WebChat.Sync;

/// <summary>
/// Protegge i contenuti dei blocchi <c>@@@FILE: path@@@ ... @@@END_FILE@@@</c>
/// nell'HTML del DOM prima della conversione HTML → Markdown (es. ReverseMarkdown).
///
/// Problema risolto (Globale per tutte le WebChat):
/// Quando un LLM emette un file XML/XAML/.csproj o codice C# con generic (<c>List&lt;T&gt;</c>)
/// dentro <c>@@@FILE: ... @@@END_FILE@@@</c> dimenticando di racchiuderlo in un blocco
/// <c>```xml</c> / <c>```csharp</c>, il parser Markdown del browser inserisce i tag
/// <c>&lt;Project&gt;</c>, <c>&lt;PropertyGroup&gt;</c>, <c>&lt;OutputType&gt;</c> direttamente
/// come elementi DOM HTML sconosciuti (spesso convertiti in lowercase dal parser HTML5).
/// Il successivo passaggio in ReverseMarkdown con <c>UnknownTagsOption.Bypass</c> rimuoverebbe
/// tutti i tag lasciando solo il testo interno (es. <c>"WinExe\nnet8.0-windows\ntrue..."</c>).
/// </summary>
public static class FileBlockHtmlProtector
{
    // Tag HTML standard generati dal renderer Markdown della WebChat che NON sono tag XML del file utente
    private static readonly HashSet<string> StandardHtmlTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "span", "pre", "code", "br", "hr",
        "strong", "b", "em", "i", "u", "s", "del", "ins", "mark", "small", "sub", "sup",
        "ul", "ol", "li", "dl", "dt", "dd",
        "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "colgroup", "col",
        "blockquote", "q", "cite", "dfn", "abbr", "kbd", "samp", "var", "time",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "a", "img", "figure", "figcaption", "picture", "source",
        "section", "article", "header", "footer", "nav", "main", "aside", "details", "summary",
        "button", "svg", "path", "g", "circle", "rect", "line", "polyline", "polygon"
    };

    // Mappa case-insensitive → PascalCase canonico MSBuild / .NET (.csproj, .props, .targets, .config)
    private static readonly Dictionary<string, string> CanonicalMsBuildTagNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["project"] = "Project",
        ["propertygroup"] = "PropertyGroup",
        ["itemgroup"] = "ItemGroup",
        ["outputtype"] = "OutputType",
        ["targetframework"] = "TargetFramework",
        ["targetframeworks"] = "TargetFrameworks",
        ["usewindowsforms"] = "UseWindowsForms",
        ["usewpf"] = "UseWPF",
        ["nullable"] = "Nullable",
        ["implicitusings"] = "ImplicitUsings",
        ["rootnamespace"] = "RootNamespace",
        ["assemblyname"] = "AssemblyName",
        ["applicationicon"] = "ApplicationIcon",
        ["applicationmanifest"] = "ApplicationManifest",
        ["langversion"] = "LangVersion",
        ["allowunsafeblocks"] = "AllowUnsafeBlocks",
        ["optimize"] = "Optimize",
        ["treatwarningsaserrors"] = "TreatWarningsAsErrors",
        ["nowarn"] = "NoWarn",
        ["warninglevel"] = "WarningLevel",
        ["platforms"] = "Platforms",
        ["platformtarget"] = "PlatformTarget",
        ["prefer32bit"] = "Prefer32Bit",
        ["publishsinglefile"] = "PublishSingleFile",
        ["selfcontained"] = "SelfContained",
        ["runtimeidentifier"] = "RuntimeIdentifier",
        ["runtimeidentifiers"] = "RuntimeIdentifiers",
        ["publishready2run"] = "PublishReadyToRun",
        ["publishtrimmed"] = "PublishTrimmed",
        ["enablecompressioninsinglefile"] = "EnableCompressionInSingleFile",
        ["includenativelibrariesforselfextract"] = "IncludeNativeLibrariesForSelfExtract",
        ["generateassemblyinfo"] = "GenerateAssemblyInfo",
        ["version"] = "Version",
        ["authors"] = "Authors",
        ["company"] = "Company",
        ["product"] = "Product",
        ["description"] = "Description",
        ["copyright"] = "Copyright",
        ["packagereference"] = "PackageReference",
        ["projectreference"] = "ProjectReference",
        ["reference"] = "Reference",
        ["compile"] = "Compile",
        ["none"] = "None",
        ["content"] = "Content",
        ["resource"] = "Resource",
        ["embeddedresource"] = "EmbeddedResource",
        ["page"] = "Page",
        ["applicationdefinition"] = "ApplicationDefinition",
        ["folder"] = "Folder",
        ["using"] = "Using",
        ["internalsvisibleto"] = "InternalsVisibleTo",
        ["target"] = "Target",
        ["exec"] = "Exec",
        ["copy"] = "Copy",
        ["makedir"] = "MakeDir",
        ["delete"] = "Delete",
        ["message"] = "Message",
        ["import"] = "Import",
        ["choose"] = "Choose",
        ["when"] = "When",
        ["otherwise"] = "Otherwise",
        ["copytooutputdirectory"] = "CopyToOutputDirectory",
        ["copytopublishdirectory"] = "CopyToPublishDirectory",
        ["subType"] = "SubType",
        ["generator"] = "Generator",
        ["lastgenoutput"] = "LastGenOutput",
        ["dependentupon"] = "DependentUpon",
        ["privateassets"] = "PrivateAssets",
        ["includeassets"] = "IncludeAssets",
        ["excludeassets"] = "ExcludeAssets",
        ["hintpath"] = "HintPath"
    };

    private static readonly Dictionary<string, string> CanonicalMsBuildAttrNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sdk"] = "Sdk",
        ["include"] = "Include",
        ["exclude"] = "Exclude",
        ["remove"] = "Remove",
        ["update"] = "Update",
        ["version"] = "Version",
        ["condition"] = "Condition",
        ["name"] = "Name",
        ["beforetargets"] = "BeforeTargets",
        ["aftertargets"] = "AfterTargets",
        ["dependsontargets"] = "DependsOnTargets",
        ["command"] = "Command",
        ["workingdirectory"] = "WorkingDirectory",
        ["sourcefiles"] = "SourceFiles",
        ["destinationfolder"] = "DestinationFolder",
        ["directories"] = "Directories",
        ["text"] = "Text",
        ["importance"] = "Importance",
        ["project"] = "Project",
        ["link"] = "Link"
    };

    private static readonly Regex FileBlockRegex = new(
        @"(@+(?:[A-Za-z]_)?FILE:\s*([^\s@<]+)\s*@+)([\s\S]*?)(@+END(?:\\?_FILE)?@+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HtmlTagRegex = new(
        @"<\s*(/?)\s*([a-zA-Z_][a-zA-Z0-9_\-\.:]*)([^>]*)>",
        RegexOptions.Compiled);

    private static readonly Regex AttrNameRegex = new(
        @"\b([a-zA-Z_][a-zA-Z0-9_\-]*)\s*=",
        RegexOptions.Compiled);

    /// <summary>
    /// Scansiona l'HTML alla ricerca di blocchi <c>@@@FILE: path@@@ ... @@@END_FILE@@@</c>.
    /// Se all'interno di un blocco file sono presenti tag non-HTML (es. <c>&lt;Project&gt;</c>,
    /// <c>&lt;PropertyGroup&gt;</c>, <c>&lt;OutputType&gt;</c> o generic C#) fuori da <c>&lt;pre&gt;&lt;code&gt;</c>,
    /// li converte in entità HTML escapate (<c>&amp;lt;...&amp;gt;</c>) racchiuse in <c>&lt;pre&gt;&lt;code&gt;</c>,
    /// ripristinando il PascalCase canonico MSBuild quando il parser HTML5 del browser li ha resi minuscoli.
    /// </summary>
    public static string ProtectCustomTagsInFileBlocks(string html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains("FILE:", StringComparison.OrdinalIgnoreCase))
            return html;

        return FileBlockRegex.Replace(html, match =>
        {
            string startMarker = match.Groups[1].Value;
            string filePath    = match.Groups[2].Value.Trim();
            string innerHtml   = match.Groups[3].Value;
            string endMarker   = match.Groups[4].Value;

            // Se il blocco contiene già un <pre> o <code>, i tag al suo interno sono già escapati dal browser
            if (Regex.IsMatch(innerHtml, @"<\s*(pre|code)\b", RegexOptions.IgnoreCase))
                return match.Value;

            bool isMsBuildOrXmlFile = filePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                                   || filePath.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                                   || filePath.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
                                   || filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
                                   || filePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                                   || filePath.EndsWith(".config", StringComparison.OrdinalIgnoreCase)
                                   || filePath.EndsWith(".txd", StringComparison.OrdinalIgnoreCase)
                                   || filePath.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase)
                                   || filePath.EndsWith(".resx", StringComparison.OrdinalIgnoreCase);

            bool hasCustomTag = false;
            foreach (Match tagMatch in HtmlTagRegex.Matches(innerHtml))
            {
                string tagName = tagMatch.Groups[2].Value;
                if (!StandardHtmlTags.Contains(tagName))
                {
                    hasCustomTag = true;
                    break;
                }
            }

            if (!hasCustomTag)
                return match.Value;

            // Ricostruisce il contenuto convertendo i tag non-standard in testo escapato (&lt;Tag&gt;)
            // e rimuovendo i wrapper di paragrafo/br inseriti dal renderer Markdown attorno a essi.
            string protectedInner = HtmlTagRegex.Replace(innerHtml, tagMatch =>
            {
                string slash   = tagMatch.Groups[1].Value;
                string tagName = tagMatch.Groups[2].Value;
                string attrs   = tagMatch.Groups[3].Value;

                if (StandardHtmlTags.Contains(tagName))
                {
                    if (tagName.Equals("br", StringComparison.OrdinalIgnoreCase))
                        return "\n";
                    if (tagName.Equals("p", StringComparison.OrdinalIgnoreCase) ||
                        tagName.Equals("div", StringComparison.OrdinalIgnoreCase))
                        return slash == "/" ? "\n" : "";
                    return "";
                }

                // Ripristina PascalCase per i tag e gli attributi MSBuild se il DOM HTML5 li ha resi lowercase
                if (isMsBuildOrXmlFile && CanonicalMsBuildTagNames.TryGetValue(tagName, out var canonicalTag))
                {
                    tagName = canonicalTag;
                }

                if (isMsBuildOrXmlFile && !string.IsNullOrEmpty(attrs))
                {
                    attrs = AttrNameRegex.Replace(attrs, attrMatch =>
                    {
                        string attrName = attrMatch.Groups[1].Value;
                        if (CanonicalMsBuildAttrNames.TryGetValue(attrName, out var canonicalAttr))
                            return canonicalAttr + "=";
                        return attrMatch.Value;
                    });
                }

                // Se è un tag vuoto MSBuild (es. <PackageReference ... />), il parser HTML5 potrebbe aver
                // rimosso lo slash finale e aggiunto un tag di chiusura </packagereference>.
                return $"&lt;{slash}{tagName}{attrs}&gt;";
            });

            // Formatta con a capo leggibili se il DOM aveva schiacciato i tag XML su una sola riga
            if (isMsBuildOrXmlFile)
            {
                protectedInner = FormatEscapedXmlNewlines(protectedInner);
            }

            return $"{startMarker}\n<pre><code>{protectedInner.Trim()}</code></pre>\n{endMarker}";
        });
    }

    private static string FormatEscapedXmlNewlines(string escapedXml)
    {
        // Inserisce un ritorno a capo tra un tag di chiusura/apertura consecutivo se sono attaccati: &gt;&lt;
        string formatted = Regex.Replace(escapedXml, @"&gt;\s*&lt;(?!/)", "&gt;\n&lt;");
        formatted = Regex.Replace(formatted, @"(&lt;/[^&]+&gt;)\s*(&lt;)", "$1\n$2");
        return formatted;
    }
}

