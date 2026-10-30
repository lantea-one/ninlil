using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Web.OpenApi;

/// <summary>
///     This class rewrites the XML documentation cross references that reach the published OpenAPI
///     document into names a reader of the API understands. Swashbuckle copies the text of
///     <c>Ninlil.Web.xml</c> into summaries and descriptions. A <c>&lt;see cref="..." /&gt;</c> to
///     a method keeps its raw documentation ID with the full parameter list
///     (<c>M:Ninlil.Web.Controllers.AuthController.Login(Ninlil.Web.Models.LoginRequest)</c>),
///     and a cref to a type or property keeps its full dotted name without the <c>T:</c> or
///     <c>P:</c> prefix (<c>Ninlil.Web.Models.ClusterNodeWriteRequest</c>). Both are internal names that mean
///     nothing to an API client or to the generated TypeScript client that copies the text.
///     <para>
///         <see cref="XmlCrefTextDocumentFilter" /> calls <see cref="Render" /> on every summary
///         and description of the document. The rules:
///     </para>
///     <list type="bullet">
///         <item>
///             A method ID of a controller action becomes the operation it serves, such as
///             <c>`POST /api/auth/login`</c>. The caller supplies the map, built from the API
///             descriptions; an action served by several routes is absent from it.
///         </item>
///         <item>
///             Any other method or event ID, and an action missing from the map, becomes
///             <c>`Type.Member`</c> with the generic arity and the parameter list dropped.
///         </item>
///         <item>
///             A dotted name becomes <c>`Type`</c> when it names a type and <c>`Type.Member`</c>
///             when it names a member of one. Nested types are written with a dot.
///         </item>
///     </list>
/// </summary>
public static partial class XmlCrefText
{
    /// <summary>
    ///     This pattern matches a Ninlil documentation ID or a dotted Ninlil name: an optional
    ///     one-letter kind prefix (<c>M:</c>, <c>T:</c>, <c>P:</c>, <c>F:</c>, <c>E:</c>), the
    ///     namespace root, one or more dotted segments (a segment may end in the backtick arity of
    ///     a generic, and <c>#ctor</c> is one) and an optional parameter list. The namespace roots
    ///     are the ones the guard test forbids. A trailing sentence period is not part of the name because
    ///     every segment must start with a letter.
    /// </summary>
    [GeneratedRegex(
        @"(?<kind>[MTPFE]:)?(?<name>Ninlil\.(?:Web|Dns|Dhcp|Common)(?:\.[#A-Za-z_]\w*(?:`\d+)?)+)(?<parameters>\([^()]*\))?"
    )]
    private static partial Regex CrefPattern();

    /// <summary>
    ///     This method replaces every Ninlil documentation ID and namespace-qualified Ninlil name
    ///     in <paramref name="text" /> with a name for a reader. Text without such a name comes
    ///     back unchanged.
    /// </summary>
    /// <param name="text">The summary or description to rewrite; null and empty pass through.</param>
    /// <param name="operationsByAction">
    ///     Controller action to operation: the key is the declaring type's full name, a dot and the
    ///     method name; the value is the HTTP method, a space and the path, such as
    ///     <c>POST /api/auth/login</c>.
    /// </param>
    /// <param name="isType">
    ///     Says whether a dotted name (nested types written with a dot) is a type. The filter backs
    ///     it with reflection over the loaded Ninlil assemblies.
    /// </param>
    /// <returns>The rewritten text.</returns>
    public static string Render(
        string text,
        IReadOnlyDictionary<string, string> operationsByAction,
        Func<string, bool> isType
    )
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return CrefPattern()
            .Replace(
                text,
                match =>
                {
                    string rendered = RenderOne(
                        match.Groups["kind"].Value,
                        match.Groups["name"].Value,
                        operationsByAction,
                        isType
                    );

                    // Text already inside backticks keeps one pair, not two.
                    bool insideBackticks =
                        match.Index > 0
                        && text[match.Index - 1] == '`'
                        && match.Index + match.Length < text.Length
                        && text[match.Index + match.Length] == '`';
                    return insideBackticks ? rendered : "`" + rendered + "`";
                }
            );
    }

    /// <summary>
    ///     This method renders one matched name, without the surrounding backticks.
    /// </summary>
    private static string RenderOne(
        string kind,
        string dottedName,
        IReadOnlyDictionary<string, string> operationsByAction,
        Func<string, bool> isType
    )
    {
        string[] segments = dottedName.Split('.').Select(StripArity).ToArray();
        bool isMember = kind is "M:" or "P:" or "F:" or "E:";

        if (isMember)
        {
            string member = segments[^1];
            string typeName = string.Join('.', segments[..^1]);

            // The key is built from the declaring type's CLR name, which the ID spells with the
            // arity of a generic type; controllers are never generic, so the stripped name matches.
            if (
                kind == "M:"
                && operationsByAction.TryGetValue(typeName + "." + member, out string operation)
            )
                return operation;

            string shortType = ShortTypeName(segments[..^1], isType);

            // A constructor reads best as the type it builds.
            return member == "#ctor" ? shortType : shortType + "." + member;
        }

        return ShortName(segments, isType);
    }

    /// <summary>
    ///     This method drops the generic arity (<c>`1</c>) of a segment.
    /// </summary>
    private static string StripArity(string segment)
    {
        int tick = segment.IndexOf('`');
        return tick < 0 ? segment : segment[..tick];
    }

    /// <summary>
    ///     This method names a type from its dotted segments by dropping the namespace. The
    ///     namespace ends at the first prefix that is a type; nested types extend it. A name no
    ///     prefix of which is a type keeps its last segment.
    /// </summary>
    private static string ShortTypeName(string[] segments, Func<string, bool> isType)
    {
        (int start, int end) = FindType(segments, isType);
        return start < 0 ? segments[^1] : string.Join('.', segments[start..end]);
    }

    /// <summary>
    ///     This method names a type, or a member of a type, from the dotted segments of a name
    ///     whose kind is unknown. A name no prefix of which is a type is taken as
    ///     <c>Type.Member</c> from its last two segments.
    /// </summary>
    private static string ShortName(string[] segments, Func<string, bool> isType)
    {
        (int start, int end) = FindType(segments, isType);
        if (start < 0)
            return string.Join('.', segments[Math.Max(0, segments.Length - 2)..]);

        string typeName = string.Join('.', segments[start..end]);
        return end == segments.Length
            ? typeName
            : typeName + "." + string.Join('.', segments[end..]);
    }

    /// <summary>
    ///     This method finds the type inside a dotted name. It returns the index of the first
    ///     segment after the namespace and the index after the last segment of the (possibly
    ///     nested) type, or <c>(-1, -1)</c> when no prefix is a type.
    /// </summary>
    private static (int Start, int End) FindType(string[] segments, Func<string, bool> isType)
    {
        for (int length = 1; length <= segments.Length; length++)
        {
            if (!isType(string.Join('.', segments[..length])))
                continue;

            int end = length;
            while (end < segments.Length && isType(string.Join('.', segments[..(end + 1)])))
                end++;

            // The type starts after the last namespace segment, which is the longest prefix that
            // is not a type itself; nesting makes the outer type part of the name.
            int start = length - 1;
            return (start, end);
        }

        return (-1, -1);
    }
}
