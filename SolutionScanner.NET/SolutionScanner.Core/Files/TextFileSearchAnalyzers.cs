using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SolutionScanner.Core.Abstractions;
using SolutionScanner.Core.Infrastructure;
using SolutionScanner.Core.Models;

namespace SolutionScanner.Core.Files;

/// <summary>
/// Wyszukiwanie fraz w plikach XML (.xml, .config, .edmx, .sqlproj).
/// Przeszukiwane są nazwy elementów, nazwy atrybutów i wartości atrybutów (tokeny identyfikatorów).
/// Komentarze XML i zwykła treść tekstowa elementów są pomijane.
/// </summary>
public sealed class XmlSearchAnalyzer : IFileAnalyzer
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".xml", ".config", ".edmx", ".sqlproj" };

    public string Name => "Wyszukiwanie XML";

    public bool CanAnalyze(FileContext context) =>
        context.Settings.AnalyzeOtherFiles && context.Matcher.HasPhrases && Extensions.Contains(context.Extension);

    public Task AnalyzeAsync(FileContext context)
    {
        var text = context.Content.Text;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true };
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), settings);
            var info = (IXmlLineInfo)reader;
            while (reader.Read())
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element) continue;
                var elementName = reader.LocalName;
                Report(context, elementName, ElementKind.XmlElement, info.LineNumber, info.LinePosition, elementName);
                if (!reader.HasAttributes) continue;
                for (var i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);
                    if (reader.Prefix == "xmlns" || reader.LocalName == "xmlns") continue;
                    Report(context, reader.LocalName, ElementKind.XmlAttribute, info.LineNumber, info.LinePosition, $"{elementName}@{reader.LocalName}");
                    foreach (var (token, offset) in SearchEngine.EnumerateIdentifierTokens(reader.Value))
                    {
                        // pozycja wartości: po nazwie atrybutu, znaku = i cudzysłowie
                        var column = info.LinePosition + reader.Name.Length + 2 + offset;
                        Report(context, token, ElementKind.XmlAttributeValue, info.LineNumber, column, $"{elementName}@{reader.LocalName}");
                    }
                }
                reader.MoveToElement();
            }
        }
        catch (XmlException ex)
        {
            context.Run.AddLimitation(LimitationKind.ParseError, context.ProjectName,
                $"Niepoprawny XML ({ex.Message}) – wyszukiwanie w pliku przerwane w miejscu błędu.", context.Loc(ex.LineNumber, ex.LinePosition));
        }
        return Task.CompletedTask;
    }

    private static void Report(FileContext ctx, string token, ElementKind kind, int line, int column, string symbol)
    {
        foreach (var phrase in ctx.Matcher.Match(token))
        {
            ctx.Result.SearchHits.Add(new SearchHit
            {
                Phrase = phrase,
                MatchedText = token,
                ElementKind = kind,
                UsageKind = UsageKind.Unknown,
                Language = ctx.Extension.TrimStart('.').ToUpperInvariant(),
                Method = AnalysisMethod.Xml,
                SymbolName = symbol,
                CodeLine = ctx.Content.GetLine(line),
                Location = ctx.Loc(line, column)
            });
        }
    }
}

/// <summary>
/// Wyszukiwanie fraz w plikach JSON – nazwy właściwości oraz wartości tekstowe będące pojedynczym identyfikatorem.
/// Komentarze są pomijane. Pozycje są wyliczane z offsetów bajtowych czytnika Utf8JsonReader.
/// </summary>
public sealed class JsonSearchAnalyzer : IFileAnalyzer
{
    private static readonly Regex IdentifierValue = new(@"^[\p{L}_][\p{L}\p{N}_\.]*$", RegexOptions.Compiled);

    public string Name => "Wyszukiwanie JSON";

    public bool CanAnalyze(FileContext context) =>
        context.Settings.AnalyzeOtherFiles && context.Matcher.HasPhrases && context.Extension == ".json";

    public Task AnalyzeAsync(FileContext context)
    {
        var bytes = Encoding.UTF8.GetBytes(context.Content.Text);
        var lineStarts = BuildByteLineStarts(bytes);
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is not (JsonTokenType.PropertyName or JsonTokenType.String)) continue;
                var value = reader.GetString() ?? string.Empty;
                var isProperty = reader.TokenType == JsonTokenType.PropertyName;
                if (!isProperty && !IdentifierValue.IsMatch(value)) continue;
                var (line, column) = TextUtil.GetLineColumn(lineStarts, (int)reader.TokenStartIndex);
                foreach (var phrase in context.Matcher.Match(value))
                {
                    context.Result.SearchHits.Add(new SearchHit
                    {
                        Phrase = phrase,
                        MatchedText = value,
                        ElementKind = isProperty ? ElementKind.JsonProperty : ElementKind.JsonValue,
                        UsageKind = UsageKind.Unknown,
                        Language = "JSON",
                        Method = AnalysisMethod.Json,
                        SymbolName = value,
                        CodeLine = context.Content.GetLine(line),
                        Location = context.Loc(line, column)
                    });
                }
            }
        }
        catch (JsonException ex)
        {
            context.Run.AddLimitation(LimitationKind.ParseError, context.ProjectName,
                $"Niepoprawny JSON ({ex.Message}) – wyszukiwanie w pliku przerwane w miejscu błędu.", context.Loc((int)(ex.LineNumber ?? 0) + 1, (int)(ex.BytePositionInLine ?? 0) + 1));
        }
        return Task.CompletedTask;
    }

    private static int[] BuildByteLineStarts(byte[] bytes)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] == (byte)'\n') starts.Add(i + 1);
        return starts.ToArray();
    }
}

/// <summary>
/// Wyszukiwanie fraz w plikach Razor (.razor, .cshtml, .vbhtml).
/// <para>
/// Ograniczenie: brak analizy semantycznej (kod Razor jest kompilowany przez generator źródeł, którego wynik
/// nie jest dostępny w modelu MSBuildWorkspace). Z plików wycinane są bloki kodu (@code, @functions, @{ }, @( ))
/// oraz wyrażenia @identyfikator, które następnie są tokenizowane lekserem C# – komentarze i literały tekstowe
/// są w ten sposób pomijane. Treść HTML nie jest przeszukiwana.
/// </para>
/// </summary>
public sealed class RazorSearchAnalyzer : IFileAnalyzer
{
    private static readonly Regex ImplicitExpression = new(@"(?<![\w@])@(?<expr>[A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*)*)", RegexOptions.Compiled);
    private static readonly HashSet<string> RazorKeywords = new(StringComparer.Ordinal)
    {
        "code", "functions", "if", "else", "for", "foreach", "while", "do", "switch", "using", "model", "inject", "page",
        "inherits", "implements", "layout", "namespace", "attribute", "typeparam", "section", "try", "lock", "helper", "rendermode"
    };

    public string Name => "Wyszukiwanie Razor";

    public bool CanAnalyze(FileContext context) =>
        context.Settings.AnalyzeOtherFiles && context.Matcher.HasPhrases && context.Extension is ".razor" or ".cshtml" or ".vbhtml";

    public Task AnalyzeAsync(FileContext context)
    {
        var text = context.Content.Text;
        var masked = MaskComments(text);
        var lineStarts = TextUtil.BuildLineStarts(text);
        var regions = new List<(int Start, int Length)>();

        // Bloki @code { }, @functions { }, @{ }, @( ), @if (...) { }
        for (var i = 0; i < masked.Length - 1; i++)
        {
            if (masked[i] != '@') continue;
            var j = i + 1;
            while (j < masked.Length && (char.IsLetter(masked[j]))) j++;
            var keyword = masked[(i + 1)..j];
            var k = j;
            while (k < masked.Length && char.IsWhiteSpace(masked[k])) k++;
            if (k >= masked.Length) break;
            if (masked[k] == '{' && (keyword.Length == 0 || keyword is "code" or "functions"))
            {
                var end = FindMatching(masked, k, '{', '}');
                if (end > k) { regions.Add((k + 1, end - k - 1)); i = end; }
            }
            else if (masked[k] == '(' && keyword.Length == 0)
            {
                var end = FindMatching(masked, k, '(', ')');
                if (end > k) { regions.Add((k + 1, end - k - 1)); i = end; }
            }
        }

        // Wyrażenia niejawne @Model.Kwota
        foreach (Match m in ImplicitExpression.Matches(masked))
        {
            var expr = m.Groups["expr"];
            var first = expr.Value.Split('.')[0];
            if (RazorKeywords.Contains(first)) continue;
            if (regions.Any(r => expr.Index >= r.Start && expr.Index < r.Start + r.Length)) continue;
            regions.Add((expr.Index, expr.Length));
        }

        foreach (var (start, length) in regions)
        {
            var code = text.Substring(start, length);
            foreach (var token in SyntaxFactory.ParseTokens(code))
            {
                if (!token.IsKind(SyntaxKind.IdentifierToken)) continue;
                var phrases = context.Matcher.Match(token.ValueText);
                if (phrases.Count == 0) continue;
                var (line, column) = TextUtil.GetLineColumn(lineStarts, start + token.SpanStart);
                foreach (var phrase in phrases)
                {
                    context.Result.SearchHits.Add(new SearchHit
                    {
                        Phrase = phrase,
                        MatchedText = token.ValueText,
                        ElementKind = ElementKind.RazorIdentifier,
                        UsageKind = UsageKind.Unknown,
                        Language = "Razor",
                        Method = AnalysisMethod.Token,
                        SymbolName = token.ValueText,
                        CodeLine = context.Content.GetLine(line),
                        Note = "Razor – tylko analiza leksykalna (bez modelu semantycznego)",
                        Location = context.Loc(line, column, start + token.SpanStart, token.Span.Length)
                    });
                }
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Zastępuje komentarze @* *@ i &lt;!-- --&gt; spacjami (zachowując offsety).</summary>
    private static string MaskComments(string text)
    {
        var chars = text.ToCharArray();
        MaskBetween(chars, text, "@*", "*@");
        MaskBetween(chars, text, "<!--", "-->");
        return new string(chars);
    }

    private static void MaskBetween(char[] chars, string text, string open, string close)
    {
        var idx = 0;
        while ((idx = text.IndexOf(open, idx, StringComparison.Ordinal)) >= 0)
        {
            var end = text.IndexOf(close, idx + open.Length, StringComparison.Ordinal);
            end = end < 0 ? text.Length : end + close.Length;
            for (var i = idx; i < end; i++)
                if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
            idx = end;
        }
    }

    private static int FindMatching(string text, int openIndex, char open, char close)
    {
        var depth = 0;
        for (var i = openIndex; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' || c == '\'')
            {
                // pomiń literał tekstowy/znakowy
                var q = c;
                i++;
                while (i < text.Length && text[i] != q) { if (text[i] == '\\') i++; i++; }
                continue;
            }
            if (c == open) depth++;
            else if (c == close && --depth == 0) return i;
        }
        return -1;
    }
}
