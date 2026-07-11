using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Fastenshtein;
using Markdig;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Utility;

namespace Melodee.Common.Extensions;

public static partial class StringExtensions
{
    public const char TagsSeparator = '|';

    private const string YearParseRegex = "(19|20)\\d{2}";

    private static readonly string SongNumberParseRegex = @"\s*\d{2,}\s*-*\s*";

    public static readonly Regex HasEpFragmentsRegex =
        new(@"(?<![a-zA-Z])(ep)(?![a-zA-Z])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly Regex HasOtherTypeFragmentsRegex = new(@"(?<![a-zA-Z])(live|tribute)(?![a-zA-Z])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly Regex HasSingleFragmentsRegex = new(@"(((\(|\[)|(\s-\s))+(single)+(\)|\])?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly Regex HasWithFragmentsRegex = new(@"(\s*[\(\[]*with\s+)+", RegexOptions.Compiled);

    public static readonly Regex HasFeatureFragmentsRegex =
        new(@"(\s[\(\[]*ft[\s\.]|\s*[\(\[]*feat[\s\.]|[\(\[]*(featuring))+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly string RomanRegex =
        @"\b((?:[Xx]{1,3}|[Xx][Ll]|[Ll][Xx]{0,3})?(?:[Ii]{1,3}|[Ii][VvXx]|[Vv][Ii]{0,3})?)\b";

    private static readonly string[] Conjunctions = ["Y", "E", "I"];

    private static readonly Dictionary<char, string> UnicodeAccents = new()
    {
        { 'À', "A" }, { 'Á', "A" }, { 'Â', "A" }, { 'Ã', "A" }, { 'Ä', "Ae" }, { 'Å', "A" }, { 'Æ', "Ae" },
        { 'Ç', "C" },
        { 'È', "E" }, { 'É', "E" }, { 'Ê', "E" }, { 'Ë', "E" },
        { 'Ì', "I" }, { 'Í', "I" }, { 'Î', "I" }, { 'Ï', "I" },
        { 'Ð', "Dh" }, { 'Þ', "Th" },
        { 'Ñ', "N" },
        { 'Ò', "O" }, { 'Ó', "O" }, { 'Ô', "O" }, { 'Õ', "O" }, { 'Ö', "Oe" }, { 'Ø', "Oe" },
        { 'Ù', "U" }, { 'Ú', "U" }, { 'Û', "U" }, { 'Ü', "Ue" },
        { 'Ý', "Y" },
        { 'ß', "ss" },
        { 'à', "a" }, { 'á', "a" }, { 'â', "a" }, { 'ã', "a" }, { 'ä', "ae" }, { 'å', "a" }, { 'æ', "ae" },
        { 'ç', "c" },
        { 'è', "e" }, { 'é', "e" }, { 'ê', "e" }, { 'ë', "e" },
        { 'ì', "i" }, { 'í', "i" }, { 'î', "i" }, { 'ï', "i" },
        { 'ð', "dh" }, { 'þ', "th" },
        { 'ñ', "n" },
        { 'ò', "o" }, { 'ó', "o" }, { 'ô', "o" }, { 'õ', "o" }, { 'ö', "oe" }, { 'ø', "oe" },
        { 'ù', "u" }, { 'ú', "u" }, { 'û', "u" }, { 'ü', "ue" },
        { 'ý', "y" }, { 'ÿ', "y" }
    }; // ReSharper disable StringLiteralTypo        

    private static readonly Dictionary<string, string> MacExceptions = new()
    {
        { @"\bMacEdo", "Macedo" },
        { @"\bMacEvicius", "Macevicius" },
        { @"\bMacHado", "Machado" },
        { @"\bMacHar", "Machar" },
        { @"\bMacHin", "Machin" },
        { @"\bMacHlin", "Machlin" },
        { @"\bMacIas", "Macias" },
        { @"\bMacIulis", "Maciulis" },
        { @"\bMacKie", "Mackie" },
        { @"\bMacKle", "Mackle" },
        { @"\bMacKlin", "Macklin" },
        { @"\bMacKmin", "Mackmin" },
        { @"\bMacQuarie", "Macquarie" },
        { @"\bMacEy ", "Macey " }
    };
    // ReSharper enable StringLiteralTypo

    private static readonly Dictionary<string, string> NameCaseReplacements = new()
    {
        { "o'reilly", "O'Reilly" }
    };

    public static string? Nullify(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Equals("\"\"") || input.Equals("''"))
        {
            return null;
        }

        var inputValue = input.Trim();
        if (string.Equals("null", inputValue, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return inputValue;
    }

    public static string? TruncateLongString(this string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        return input[..Math.Min(input.Length, maxLength)];
    }

    public static string ToBase64(this string text)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string FromBase64(this string text)
    {
        text = text.Replace('_', '/').Replace('-', '+');
        switch (text.Length % 4)
        {
            case 2:
                text += "==";
                break;
            case 3:
                text += "=";
                break;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(text));
    }

    public static string? ToTitleCase(this string input, bool doPutTheAtEnd = true)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        input = input.Replace("’", "'");
        var textInfo = new CultureInfo("en-US", false).TextInfo;
        var r = textInfo.ToTitleCase(input.Trim().ToLower());
        r = Regex.Replace(r, @"\s+", " ");
        if (doPutTheAtEnd)
        {
            if (r.StartsWith("The "))
            {
                r = $"{r.Replace("The ", string.Empty)}, The";
            }
        }

        return r.NameCase();
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static string NameCase(this string nameString, bool doFixConjunction = false)
    {
        nameString = Capitalize(nameString);
        nameString = UpdateRoman(nameString);
        nameString = UpdateIrish(nameString);
        if (doFixConjunction)
        {
            nameString = FixConjunction(nameString);
        }

        nameString = Regex.Replace(nameString, @"('[A-Z])", m => m.ToString().ToLower(), RegexOptions.IgnoreCase);
        foreach (var replacement in NameCaseReplacements.Keys)
        {
            nameString = nameString.Replace(replacement, NameCaseReplacements[replacement],
                StringComparison.OrdinalIgnoreCase);
        }

        return nameString;
    }

    private static string FixConjunction(string nameString)
    {
        foreach (var conjunction in Conjunctions)
        {
            nameString = Regex.Replace(nameString, @"\b" + conjunction + @"\b", x => x.ToString().ToLower());
        }

        return nameString;
    }

    private static string UpdateRoman(string nameString)
    {
        var matches = Regex.Matches(nameString, RomanRegex);
        if (matches.Count > 1)
        {
            foreach (Match match in matches)
            {
                if (!string.IsNullOrEmpty(match.Value))
                {
                    // Use Regex.Escape to prevent regex injection from user input
                    nameString = Regex.Replace(nameString, Regex.Escape(match.Value), x => x.ToString().ToUpper());
                }
            }
        }

        return nameString;
    }

    private static string UpdateIrish(string nameString)
    {
        if (Regex.IsMatch(nameString, @".*?\bMac[A-Za-z^aciozj]{2,}\b") || Regex.IsMatch(nameString, @".*?\bMc"))
        {
            nameString = UpdateMac(nameString);
        }

        return nameString;
    }

    /// <summary>
    ///     Updates Irish Mac and Mc name prefixes.
    /// </summary>
    /// <param name="nameString"></param>
    /// <returns></returns>
    private static string UpdateMac(string nameString)
    {
        var matches = Regex.Matches(nameString, @"\b(Ma?c)([A-Za-z]+)");
        if (matches is [{ Groups.Count: 3 }])
        {
            var replacement = matches[0].Groups[1].Value;
            replacement += matches[0].Groups[2].Value.Substring(0, 1).ToUpper();
            replacement += matches[0].Groups[2].Value.Substring(1);
            nameString = nameString.Replace(matches[0].Groups[0].Value, replacement);

            // Now fix "Mac" exceptions
            foreach (var exception in MacExceptions.Keys)
            {
                nameString = Regex.Replace(nameString, exception, MacExceptions[exception]);
            }
        }

        return nameString;
    }

    private static string Capitalize(string nameString)
    {
        nameString = nameString.ToLower();
        nameString = Regex.Replace(nameString, @"\b\w", x => x.ToString().ToUpper());
        nameString = Regex.Replace(nameString, @"'\w\b", x => x.ToString().ToLower()); // Lowercase 's
        return nameString;
    }

    public static bool IsValueInDelimitedList(this string input, string value, char delimiter = '|')
    {
        if (string.IsNullOrEmpty(input))
        {
            return false;
        }

        var p = input.Split(delimiter);
        return p.Any() && p.Any(x => x.Trim().Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    public static string? AddToDelimitedList(this string? input, IEnumerable<string>? values, char delimiter = '|')
    {
        var vv = values?.ToArray() ?? [];
        if (string.IsNullOrEmpty(input) && (values == null || vv.Length != 0))
        {
            return null;
        }

        if (string.IsNullOrEmpty(input))
        {
            return string.Join(delimiter.ToString(), vv);
        }

        if (values == null || vv.Length == 0)
        {
            return input;
        }

        foreach (var value in vv)
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!input.IsValueInDelimitedList(value, delimiter))
            {
                if (!input.EndsWith(delimiter.ToString()))
                {
                    input += delimiter;
                }

                input += value;
            }
        }

        return input;
    }

    /// <summary>
    ///     Return the cleaned version of the string without changing chase or manipulating the "THE" part.
    /// </summary>
    public static string? CleanStringAsIs(this string input)
    {
        return CleanString(input, false, false);
    }

    public static string? CleanString(this string? input, bool? doPutTheAtEnd = false, bool? doTitleCase = true)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var result = input;
        result = result.Trim();
        if (doTitleCase ?? true)
        {
            result = result.ToTitleCase(doPutTheAtEnd ?? false);
        }

        if (string.IsNullOrEmpty(result))
        {
            return input;
        }

        return ReplaceMultipleSpacesRegex().Replace(CleanStringReplacementRegex().Replace(result, string.Empty), " ")
            .Trim();
    }

    public static bool ContainsUnicodeCharacter(this string input)
    {
        const int maxAnsiCode = 255;
        return input.Any(c => c > maxAnsiCode);
    }

    public static string? OnlyAlphaNumeric(this string? input)
    {
        return string.IsNullOrEmpty(input) ? null : OnlyAlphaNumericRegex().Replace(input, " ").Nullify();
    }

    public static string? ToNumberOnly(this string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        return new string(input.Where(char.IsDigit).ToArray());
    }

    public static string? RemoveNumbers(this string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        return string.Join(" ",
            input.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(word => new string(word.Where(c => !char.IsDigit(c)).ToArray()))
                .Where(word => !string.IsNullOrWhiteSpace(word)));
    }


    public static int? ExtractNumber(this string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        var numbers = new string(input.Where(char.IsDigit).ToArray());
        return int.TryParse(numbers, out var result) ? result : null;
    }

    public static string? ExtractLetters(this string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        return new string(input.ToLower()
            .Where(c => c is >= 'a' and <= 'z')
            .ToArray());
    }


    /// <summary>
    ///     If the given input is a tag delimited string, return an enumeration of tags, otherwise return null.
    /// </summary>
    /// <param name="str">Nullable string of tag delimited values</param>
    /// <returns>Enumerable collection of tags</returns>
    public static IEnumerable<string>? ToTags(this string? str)
    {
        if (SafeParser.IsNull(str))
        {
            return null;
        }

        if (!str!.Contains(TagsSeparator))
        {
            return [str];
        }

        return str.Split(TagsSeparator).Where(x => !SafeParser.IsNull(x));
    }

    /// <summary>
    ///     Add tags to the given string value.
    /// </summary>
    /// <param name="str">Tag value to return tags joined by split value</param>
    /// <param name="value">Value to add to Tag result, can be Tags also joined by split value</param>
    /// <param name="tagSplit">Tag split value</param>
    /// <param name="dontLowerCase">Don't return lowercase value, leave value case as is</param>
    /// <returns>Unique and sorted Tags joined by split value</returns>
    public static string? AddTags(this string? str, string? value, char? tagSplit = TagsSeparator,
        bool? dontLowerCase = false)
    {
        return AddTags(str, value?.Split(tagSplit ?? TagsSeparator), tagSplit, dontLowerCase);
    }

    /// <summary>
    ///     Add tags to the given string value.
    /// </summary>
    /// <param name="str">Tag value to return tags joined by split value</param>
    /// <param name="values">Values to add to Tag result</param>
    /// <param name="tagSplit">Tag split value</param>
    /// <param name="dontLowerCase">Don't return lowercase value, leave value case as is</param>
    /// <param name="doReorder">Reorder values</param>
    /// <param name="doNormalize">Apply normalization to values</param>
    /// <returns>Unique and sorted Tags joined by split value</returns>
    public static string? AddTags(this string? str, IEnumerable<string?>? values, char? tagSplit = TagsSeparator,
        bool? dontLowerCase = false, bool? doReorder = true, bool? doNormalize = false)
    {
        var vv = values as string[] ?? values?.ToArray() ?? [];
        if (SafeParser.IsNull(str) && vv.Length == 0)
        {
            return null;
        }

        var ts = tagSplit ?? TagsSeparator;
        var value = string.Join(ts,
            vv.Where(x => !string.IsNullOrEmpty(x))
                .Select(x => doNormalize ?? false ? x.ToNormalizedString() ?? x : x));
        var tags = (str ?? value).Nullify()?.Split(ts).Select(x => x.Nullify()).ToList() ?? [];
        if (value.Contains(ts))
        {
            tags.AddRange(value.Split(ts));
        }
        else if (!SafeParser.IsTruthy(tags.Any(x =>
                     string.Equals(x, value.Nullify(), StringComparison.OrdinalIgnoreCase))))
        {
            tags.Add(value.Nullify());
        }

        var tv = tags.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.CurrentCultureIgnoreCase);
        if (doReorder ?? true)
        {
            tv = tv.Order();
        }

        var result = string.Join(ts, tv).Nullify();
        if ((dontLowerCase ?? false) || (doNormalize ?? false))
        {
            return result;
        }

        return result?.ToLower();
    }

    public static string? TrimAndNullify(this string? value)
    {
        return value?.Nullify();
    }

    public static string? ToNormalizedString(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var v = input.CleanStringAsIs();
        if (string.IsNullOrWhiteSpace(v))
        {
            // Cheap way to handle non english, RTL, etc.
            return input.ToBase64();
        }

        v = ReplaceWithCharacter().Replace(v, string.Empty);
        return ReplaceDuplicatePipeCharacters()
            .Replace(RemoveNonAlphanumericCharacters()
                    .Replace(v.RemoveAccents()
                                 ?.ToUpperInvariant() ??
                             string.Empty,
                        string.Empty),
                "|")
            .Nullify() ?? input.ToBase64();
    }

    public static string ToAlphanumericName(this string input, bool stripSpaces = true, bool stripCommas = true)
    {
        const char separator = '_';
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        input = input.ToLower()
            .Replace("$", "__x24f")
            .Replace("%", "__x25f")
            .Replace("?", "__x3f")
            .Replace("!", "__x21");
        input = WebUtility.HtmlDecode(input);
        input = input.ScrubHtml().ToLower()
            .Replace("&", "and");
        var arr = input.ToCharArray();
        arr = Array.FindAll(arr,
            c => (c == ',' && !stripCommas) || (char.IsWhiteSpace(c) && !stripSpaces) || c == separator ||
                 char.IsLetterOrDigit(c));
        input = new string(arr).RemoveDiacritics().RemoveUnicodeAccents().Transliteration();
        input = Regex.Replace(input,
            $"[^A-Za-z0-9_{(!stripSpaces ? @"\s" : string.Empty)}{(!stripCommas ? "," : string.Empty)}]+",
            string.Empty);
        return input;
    }

    public static string? ToDirectoryNameFriendly(this string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        input = input.Replace("$", "s");
        return Regex.Replace(PathSanitizer.SanitizeFilename(input, ' ') ?? string.Empty, @"\s+", " ").Trim()
            .TrimEnd('.');
    }

    private static string ScrubHtml(this string value)
    {
        var step1 = Regex.Replace(value, @"<[^>]+>|&nbsp;", string.Empty).Trim();
        var step2 = Regex.Replace(step1, @"\s{2,}", " ");
        return step2;
    }

    public static string RemoveStartsWith(this string input, string remove = "")
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var index = input.IndexOf(remove, StringComparison.OrdinalIgnoreCase);
        var result = input;
        while (index == 0)
        {
            result = result.Remove(index, remove.Length).Trim();
            index = result.IndexOf(remove, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string RemoveDiacritics(this string s)
    {
        var normalizedString = s.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();
        foreach (var c in normalizedString)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString();
    }

    private static string RemoveUnicodeAccents(this string text)
    {
        return text.Aggregate(
            new StringBuilder(),
            (sb, c) =>
            {
                if (UnicodeAccents.TryGetValue(c, out var r))
                {
                    return sb.Append(r);
                }

                return sb.Append(c);
            }).ToString();
    }

    private static string Transliteration(this string str)
    {
        string[] latUp =
        [
            "A", "B", "V", "G", "D", "E", "Yo", "Zh", "Z", "I", "Y", "K", "L", "M", "N", "O", "P", "R", "S", "T",
            "U", "F", "Kh", "Ts", "Ch", "Sh", "Shch", "\"", "Y", "'", "E", "Yu", "Ya"
        ];
        string[] latLow =
        [
            "a", "b", "v", "g", "d", "e", "yo", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p", "r", "s", "t",
            "u", "f", "kh", "ts", "ch", "sh", "shch", "\"", "y", "'", "e", "yu", "ya"
        ];
        string[] rusUp =
        [
            "А", "Б", "В", "Г", "Д", "Е", "Ё", "Ж", "З", "И", "Й", "К", "Л", "М", "Н", "О", "П", "Р", "С", "Т", "У",
            "Ф", "Х", "Ц", "Ч", "Ш", "Щ", "Ъ", "Ы", "Ь", "Э", "Ю", "Я"
        ];
        string[] rusLow =
        [
            "а", "б", "в", "г", "д", "е", "ё", "ж", "з", "и", "й", "к", "л", "м", "н", "о", "п", "р", "с", "т", "у",
            "ф", "х", "ц", "ч", "ш", "щ", "ъ", "ы", "ь", "э", "ю", "я"
        ];
        for (var i = 0; i <= 32; i++)
        {
            str = str.Replace(rusUp[i], latUp[i]);
            str = str.Replace(rusLow[i], latLow[i]);
        }

        return str;
    }

    public static bool DoStringsMatch(this string? string1, string? string2)
    {
        var a1 = string1.Nullify();
        var a2 = string2.Nullify();
        if (a1 == a2)
        {
            return true;
        }

        if (a1 == null && a2 != null)
        {
            return false;
        }

        if (a1 != null && a2 == null)
        {
            return false;
        }

        return string.Equals(a1?.ToAlphanumericName(), a2?.ToAlphanumericName());
    }

    public static string? TryToGetAlbumTitle(this string? input)
    {
        if (input.Nullify() == null)
        {
            return null;
        }

        var result = Regex.Replace(input!, @"[-_]", TagsSeparator.ToString());
        result = Regex.Replace(result, @"(\(|\[)?\b(19|20)\d{2}\b(\)|\])?", string.Empty);
        if (result.Contains(TagsSeparator))
        {
            result = result.Split(TagsSeparator).Last();
        }

        return result.CleanString();
    }

    public static int? TryToGetYearFromString(this string input)
    {
        if (input.Nullify() == null)
        {
            return null;
        }

        if (Regex.IsMatch(input, YearParseRegex, RegexOptions.RightToLeft))
        {
            return SafeParser.ToNumber<int?>(Regex.Match(input, YearParseRegex, RegexOptions.RightToLeft).Value);
        }

        return null;
    }

    public static int? TryToGetSongNumberFromString(this string input)
    {
        if (input.Nullify() == null)
        {
            return null;
        }

        if (Regex.IsMatch(input, SongNumberParseRegex))
        {
            var v = new string(Regex.Match(input, SongNumberParseRegex).Value.Where(char.IsDigit).ToArray());
            return SafeParser.ToNumber<int?>(v);
        }

        return null;
    }

    public static string? RemoveSongNumberFromString(this string input)
    {
        if (input.Nullify() == null)
        {
            return null;
        }

        return Regex.IsMatch(input, SongNumberParseRegex)
            ? Regex.Replace(input, SongNumberParseRegex, string.Empty)
            : input;
    }


    public static bool HasWithFragments(this string? input)
    {
        return input.Nullify() != null && HasWithFragmentsRegex.IsMatch(input!);
    }

    public static bool HasFeaturingFragments(this string? input)
    {
        return input.Nullify() != null && HasFeatureFragmentsRegex.IsMatch(input!);
    }

    public static int FeaturingAndWithFragmentsCount(this string? input)
    {
        if (input.Nullify() == null)
        {
            return 0;
        }

        return HasWithFragmentsRegex.Matches(input!).Count + HasFeatureFragmentsRegex.Matches(input!).Count;
    }

    public static AlbumType TryToDetectAlbumType(this string? input)
    {
        if (input.Nullify() == null)
        {
            return AlbumType.NotSet;
        }

        if (HasEpFragmentsRegex.Matches(input!).Count > 0)
        {
            return AlbumType.EP;
        }

        if (HasSingleFragmentsRegex.Matches(input!).Count > 0)
        {
            return AlbumType.Single;
        }

        if (HasOtherTypeFragmentsRegex.Matches(input!).Count > 0)
        {
            return AlbumType.Other;
        }

        return AlbumType.Album;
    }

    public static string? RemoveFileExtension(this string? input)
    {
        return input.Nullify() == null ? null : Path.GetFileNameWithoutExtension(input!);
    }

    public static FileSystemDirectoryInfo ToFileSystemDirectoryInfo(this string? input)
    {
        return !string.IsNullOrWhiteSpace(input)
            ? new DirectoryInfo(input).ToDirectorySystemInfo()
            : FileSystemDirectoryInfo.Blank();
    }

    public static string?[]? PeopleFromValueWithSeparator(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Replace(";", "/").Replace(";", "/").Split('/').Select(x => x.Nullify()).Where(x => x != null)
            .ToArray();
    }

    public static string? ToCleanedMultipleArtistsValue(this string? input)
    {
        if (input.Nullify() == null)
        {
            return null;
        }

        var result = ReplaceMultipleSpacesRegex()
            .Replace(
                input!.Replace("«multiple values»", string.Empty).Trim().Replace(";;", ";").Replace("; ", "/")
                    .Replace(";", "/"), string.Empty);
        if (result.StartsWith('/'))
        {
            return result[1..];
        }

        return result;
    }

    public static string? ToHexString(this string? input)
    {
        if (input.Nullify() == null)
        {
            return null;
        }

        var sb = new StringBuilder();
        var bytes = Encoding.UTF8.GetBytes(input!);
        foreach (var t in bytes)
        {
            sb.Append(t.ToString("X2"));
        }

        return sb.ToString();
    }

    public static string? FromHexString(this string? input)
    {
        if (input.Nullify() == null)
        {
            return null;
        }

        var inputVal = input?.StartsWith("enc:") ?? false ? input[4..] : input ?? string.Empty;
        if (inputVal.Nullify() == null)
        {
            return null;
        }

        var bytes = new byte[inputVal.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(inputVal.Substring(i * 2, 2), 16);
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public static string ReplaceNonCharacters(this string aString, char replacement)
    {
        var sb = new StringBuilder(aString.Length);
        for (var i = 0; i < aString.Length; i++)
        {
            if (char.IsSurrogatePair(aString, i))
            {
                var c = char.ConvertToUtf32(aString, i);
                i++;
                if (IsCharacter(c))
                {
                    sb.Append(char.ConvertFromUtf32(c));
                }
                else
                {
                    sb.Append(replacement);
                }
            }
            else
            {
                var c = aString[i];
                if (IsCharacter(c))
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(replacement);
                }
            }
        }

        return sb.ToString();
    }

    public static bool IsCharacter(int point)
    {
        return point < 0xFDD0 || // everything below here is fine
               (point > 0xFDEF && // exclude the 0xFFD0...0xFDEF non-characters
                (point & 0xfffE) != 0xFFFE); // exclude all other non-characters
    }


    /// <summary>
    ///     Removes diacritics (accents) from a string.
    /// </summary>
    /// <param name="text">The string to remove diacritics from.</param>
    /// <returns>The string without diacritics.</returns>
    public static string? RemoveAccents(this string text)
    {
        if (text.Nullify() == null)
        {
            return null;
        }

        var originalText = text;

        text = text.ReplaceNonCharacters(' ');

        // Normalize the string to decompose characters into their base form and combining characters
        try
        {
            text = text.Normalize(NormalizationForm.FormD);
        }
        catch (Exception e)
        {
            // Sanitize user input before logging to prevent log forging
            Trace.WriteLine($"Error: [{e.Message}] during text normalization");
        }


        // Create a StringBuilder to store the result
        var result = new StringBuilder();

        foreach (var c in text)
        {
            // Check if the character is a non-spacing mark (accent)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                result.Append(c);
            }
        }

        // Return the normalized string
        try
        {
            return result.ToString().Normalize(NormalizationForm.FormC);
        }
        catch (Exception e)
        {
            var invalidCharactersRegex =
                new Regex(
                    "([\ud800-\udbff](?![\udc00-\udfff]))|((?<![\ud800-\udbff])[\udc00-\udfff])",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(5));
            text = invalidCharactersRegex.Replace(text, "");
            // Sanitize user input before logging to prevent log forging
            Trace.WriteLine($"Error: [{e.Message}] during text normalization (invalid characters removed)");
        }

        return text;
    }

    public static bool IsSimilar(this string? input, string? compareTo, int threshold = 2)
    {
        if (input == null || compareTo == null)
        {
            return true;
        }

        return Levenshtein.Distance(input.Nullify() ?? string.Empty, compareTo.Nullify() ?? string.Empty) <= threshold;
    }

    public static string? ToSafeXmlString(this string? input)
    {
        if (input == null)
        {
            return null;
        }

        var cleanedInput = RemoveXmlInvalidCharactersRegex().Replace(input, string.Empty);
        return SecurityElement.Escape(cleanedInput);
    }

    public static string? ToHtmlString(this string? input)
    {
        return input.Nullify() == null ? null : Markdown.ToHtml(input!);
    }

    /// <summary>
    ///     Normalizes line endings by converting all CR, LF, or CR+LF combinations to a single LF (\n) character.
    /// </summary>
    /// <param name="input">The input string with potentially mixed line endings</param>
    /// <returns>A string with all line endings normalized to LF (\n)</returns>
    public static string? NormalizeLineEndings(this string? input)
    {
        if (input.Nullify() == null)
        {
            return null;
        }

        return Regex.Replace(input!, "\r\n|\r", "\n");
    }

    public static string? ToMd5(this string? input) => HashHelper.CreateMd5(input);

    public static bool ContainsAny(this string text, params string[] testMatches)
    {
        foreach (var testMatch in testMatches)
        {
            if (text.Contains(testMatch))
                return true;
        }
        return false;
    }

    [GeneratedRegex("[^a-zA-Z0-9 -.:]")]
    private static partial Regex OnlyAlphaNumericRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ReplaceMultipleSpacesRegex();

    [GeneratedRegex("[^A-Z0-9|]")]
    private static partial Regex RemoveNonAlphanumericCharacters();

    [GeneratedRegex("(\\|{2,})")]
    public static partial Regex ReplaceDuplicatePipeCharacters();

    [GeneratedRegex("␀+|\t+|\r+|\\s+|\r+|@+")]
    public static partial Regex ReplaceWithCharacter();

    //Regex.Replace(input, @"[\uFEFF\u0000-\u001F\u007F\u0080-\uFFFF]|\p{C}", string.Empty)
    [GeneratedRegex(@"(\\0|\\u0+|\\x0+|\u0000|\uFEFF|\t|\r|\n|␀)", RegexOptions.IgnoreCase)]
    private static partial Regex CleanStringReplacementRegex();

    [GeneratedRegex("[^a-z0-9-]")]
    private static partial Regex SlugInvalidCharRegex();

    [GeneratedRegex("[\x00-\x08\x0B\x0C\x0E-\x1F]")]
    private static partial Regex RemoveXmlInvalidCharactersRegex();

    [GeneratedRegex("-+")]
    private static partial Regex SlugMultipleDashRegex();

    public static string ToSlug(this string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var slug = input.ToLowerInvariant().Trim();
        slug = slug.Replace(' ', '-');
        slug = SlugInvalidCharRegex().Replace(slug, string.Empty);
        slug = SlugMultipleDashRegex().Replace(slug, "-");
        slug = slug.Trim('-');
        return slug.Length > 200 ? slug[..200] : slug;
    }

    /// <summary>
    ///     Strips leading articles from a string and returns the normalized result.
    ///     Articles are removed only when they appear at the beginning followed by a space.
    /// </summary>
    /// <param name="input">The input string to normalize.</param>
    /// <param name="articles">Pipe-delimited list of articles to strip (e.g., "THE|A|AN").</param>
    /// <returns>The normalized string with leading articles removed, or null if input is null/empty.</returns>
    public static string? StripLeadingArticles(this string? input, string? articles)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(articles))
        {
            return input;
        }

        var result = input;
        foreach (var article in articles.ToTags() ?? [])
        {
            var articleWithSpace = $"{article} ";
            if (result.StartsWith(articleWithSpace, StringComparison.OrdinalIgnoreCase))
            {
                result = result[articleWithSpace.Length..];
                break;
            }
        }

        return result.Nullify();
    }

    /// <summary>
    ///     Normalizes a string for comparison by stripping leading articles and applying standard normalization.
    ///     This is useful for detecting duplicates like "The Beatles" vs "Beatles".
    /// </summary>
    /// <param name="input">The input string to normalize.</param>
    /// <param name="articles">Pipe-delimited list of articles to strip (e.g., "THE|A|AN").</param>
    /// <returns>The fully normalized string suitable for comparison.</returns>
    public static string? ToNormalizedStringWithoutArticles(this string? input, string? articles)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var withoutArticles = input.StripLeadingArticles(articles);
        return withoutArticles?.ToNormalizedString();
    }
}
