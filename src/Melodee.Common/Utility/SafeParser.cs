using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HashidsNet;
using Melodee.Common.Data.Constants;
using Melodee.Common.Extensions;
using NodaTime;

namespace Melodee.Common.Utility;

public static class SafeParser
{
    private const string Salt = "9A0786D9-3DF7-4BE3-AB51-6E1CB91B028A";

    /// <summary>
    ///     Return true if value is null or when string or char either whitespace or empty.
    /// </summary>
    public static bool IsNull(object? value)
    {
        if (value == null)
        {
            return true;
        }

        var vt = value.GetType();
        if (vt == typeof(string) || vt == typeof(char))
        {
            return string.IsNullOrWhiteSpace(value.ToString()?.Nullify());
        }

        return !IsTruthy(value);
    }

    public static bool IsTruthy(bool? value)
    {
        return value ?? false;
    }

    public static bool IsTruthy(bool value)
    {
        return value;
    }


    /// <summary>
    ///     Falsy type checking of given value.
    /// </summary>
    public static bool IsTruthy(object? value)
    {
        if (value == null)
        {
            return false;
        }

        try
        {
            var vt = value.GetType();
            switch (Type.GetTypeCode(vt))
            {
                case TypeCode.Boolean:
                    return (bool)value;

                case TypeCode.Char:
                    return IsCharTruthy((char)value);

                case TypeCode.DateTime:
                    var dt = (DateTime)value;
                    return dt > DateTime.MinValue && dt < DateTime.MaxValue;

                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    return long.Parse(value.ToString() ?? "0") > 0;

                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.Single:
                    return decimal.Parse(value.ToString() ?? "0") > 0;

                case TypeCode.String:
                    return IsCharTruthy(((string)value).Nullify()?.ToLower().First() ?? '0');
            }

            if (vt == typeof(Guid))
            {
                return Guid.Parse(value.ToString() ?? string.Empty) != Guid.Empty;
            }

            if (vt == typeof(DateTimeOffset))
            {
                var dt = (DateTimeOffset)value;
                return dt.DateTime > DateTime.MinValue && dt.DateTime < DateTime.MaxValue;
            }

            if (value.IsEnumerable())
            {
                var ie = typeof(Enumerable).GetMethod("Cast")?
                    .MakeGenericMethod(typeof(object))
                    .Invoke(null, [value]);
                return (ie as IEnumerable<object>)?.Any() ?? false;
            }

            return vt.IsClass || IsCharTruthy(value.ToString()?.Nullify()?.ToLower().First() ?? '0');
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error determining if object is truthy [{value}] [{ex}]",
                TraceWriteLineCategoryRegistry.Error);
        }

        return false;
    }

    /// <summary>
    ///     Safely return a Boolean for a given Input.
    ///     <remarks>Has Additional String Operations</remarks>
    /// </summary>
    public static bool ToBoolean(object? input)
    {
        if (input == null)
        {
            return false;
        }

        switch (input)
        {
            case null:
                return false;

            case bool t:
                return t;

            default:
                switch (input.ToString()?.ToLower())
                {
                    case "true":
                    case "1":
                    case "y":
                    case "yes":
                        return true;

                    default:
                        return false;
                }
        }
    }

    public static DateTime? ToDateTime(object? input)
    {
        if (input == null)
        {
            return null;
        }

        try
        {
            var dt = DateTime.MinValue;
            var i = input as string ?? input.ToString();
            if (!string.IsNullOrEmpty(i))
            {
                if (Regex.IsMatch(i, @"([0-9]{4}).+([0-9]{4})"))
                {
                    i = i.Substring(0, 4);
                }

                i = Regex.Replace(i, @"(\\)", "/");
                i = Regex.Replace(i, @"(;)", "/");
                i = Regex.Replace(i, @"(\/+)", "/");
                i = Regex.Replace(i, @"(-+)", "/");
                i = i.Replace("\"", string.Empty);
                var parts = i.Contains('/') ? i.Split('/').ToList() : [i];
                if (parts.Count == 2)
                {
                    if (parts[0] == parts[1])
                    {
                        parts = [parts[0], "01"];
                    }
                }

                while (parts.Count < 3)
                {
                    parts.Insert(0, "01");
                }

                var tsRaw = string.Empty;
                foreach (var part in parts)
                {
                    if (tsRaw.Length > 0)
                    {
                        tsRaw += "/";
                    }

                    tsRaw += part;
                }

                DateTime.TryParse(tsRaw, out dt);
            }

            try
            {
                return ChangeType<DateTime?>(input);
            }
            catch
            {
                // ignored
            }

            return dt != DateTime.MinValue ? dt : null;
        }
        catch
        {
            return null;
        }
    }

    public static T ToEnum<T>(object? input) where T : struct, IConvertible
    {
        if (input == null)
        {
            return default;
        }

        Enum.TryParse(input.ToString(), true, out T r);
        return r;
    }

    public static Guid? ToGuid(object? input)
    {
        if (input == null)
        {
            return null;
        }

        var i = input.ToString();
        if (!string.IsNullOrEmpty(i) && i.Length > 1 && i[1] == ':')
        {
            i = i.Substring(2, i.Length - 2);
        }

        if (!Guid.TryParse(i, out var result))
        {
            return null;
        }

        return result;
    }

    /// <summary>
    ///     Safely Return a Number For Given Input
    /// </summary>
    public static T? ToNumber<T>(object? input)
    {
        if (input == null)
        {
            return default;
        }

        try
        {
            if (input is JsonElement)
            {
                return ChangeType<T>(input.ToString());
            }

            return ChangeType<T>(input);
        }
        catch
        {
            return default;
        }
    }

    public static string ToString(object? input, string? defaultValue = null)
    {
        defaultValue = defaultValue ?? string.Empty;
        switch (input)
        {
            case null:
                return defaultValue;

            case string r:
                return r.Trim();

            default:
                return defaultValue;
        }
    }

    public static int? ToYear(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        int parsed;
        if (input.Length == 4)
        {
            if (int.TryParse(input, out parsed))
            {
                return parsed > 0 ? parsed : null;
            }
        }
        else if (input.Length > 4)
        {
            if (int.TryParse(input.Substring(0, 4), out parsed))
            {
                return parsed > 0 ? parsed : null;
            }
        }

        return null;
    }

    public static T? ChangeType<T>(object? value)
    {
        return (T?)ChangeType(typeof(T), value);
    }

    public static object? ChangeType(Type toType, object? value)
    {
        if (!toType.IsGenericType || (toType.GetGenericTypeDefinition() != typeof(Nullable<>) && value != null))
        {
            return Convert.ChangeType(value!, toType);
        }

        if (value == null)
        {
            return null;
        }

        var tt = Nullable.GetUnderlyingType(toType);
        return tt == null ? default : Convert.ChangeType(value, tt);
    }

    public static string? ToToken(string input)
    {
        if (input.Nullify() == null)
        {
            return null;
        }

        var hashids = new Hashids(Salt);
        var numbers = 0;
        var bytes = Encoding.ASCII.GetBytes(input);
        var looper = bytes.Length / 4;
        for (var i = 0; i < looper; i++)
        {
            numbers += BitConverter.ToInt32(bytes, i * 4);
        }

        if (numbers < 0)
        {
            numbers *= -1;
        }

        return hashids.Encode(numbers);
    }

    public static string? ToFileNameFriendly(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return null;
        }

        input = input
            .Replace("$", "__x24f")
            .Replace("%", "__x25f")
            .Replace("&", "and")
            .Replace("?", "__x3f");
        return Regex.Replace(PathSanitizer.SanitizeFilename(input, ' ') ?? string.Empty, @"\s+", " ").Trim();
    }

    public static byte[] ReadFile(string filePath)
    {
        byte[] buffer;
        var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        try
        {
            var length = (int)fileStream.Length; // get file length    
            buffer = new byte[length]; // create buffer     
            int count; // actual number of bytes read     
            var sum = 0; // total number of bytes read    

            // read until Read method returns 0 (end of the stream has been reached)    
            while ((count = fileStream.Read(buffer, sum, length - sum)) > 0)
            {
                sum += count; // sum is a buffer offset for next reading
            }
        }
        finally
        {
            fileStream.Close();
        }

        return buffer;
    }

    public static long Hash(params string?[] input)
    {
        return Hash(Encoding.UTF8.GetBytes(string.Join(string.Empty, input)));
    }

    public static bool IsDigitsOnly(this string? str)
    {
        if (str.Nullify() == null)
        {
            return false;
        }

        return str!.All(c => c is >= '0' and <= '9');
    }

    // ReSharper disable once MemberCanBePrivate.Global
    public static long Hash(byte[] dataToHash)
    {
        var dataLength = dataToHash.Length;
        if (dataLength == 0)
        {
            return 0;
        }

        var hash = Convert.ToUInt32(dataLength);
        var remainingBytes = dataLength & 3; // mod 4
        var numberOfLoops = dataLength >> 2; // div 4
        var currentIndex = 0;
        while (numberOfLoops > 0)
        {
            hash += BitConverter.ToUInt16(dataToHash, currentIndex);
            var tmp = (uint)(BitConverter.ToUInt16(dataToHash, currentIndex + 2) << 11) ^ hash;
            hash = (hash << 16) ^ tmp;
            hash += hash >> 11;
            currentIndex += 4;
            numberOfLoops--;
        }

        switch (remainingBytes)
        {
            case 3:
                hash += BitConverter.ToUInt16(dataToHash, currentIndex);
                hash ^= hash << 16;
                hash ^= (uint)dataToHash[currentIndex + 2] << 18;
                hash += hash >> 11;
                break;
            case 2:
                hash += BitConverter.ToUInt16(dataToHash, currentIndex);
                hash ^= hash << 11;
                hash += hash >> 17;
                break;
            case 1:
                hash += dataToHash[currentIndex];
                hash ^= hash << 10;
                hash += hash >> 1;
                break;
        }

        hash ^= hash << 3;
        hash += hash >> 5;
        hash ^= hash << 4;
        hash += hash >> 17;
        hash ^= hash << 25;
        hash += hash >> 6;
        return hash;
    }

    private static bool IsCharTruthy(char value)
    {
        return value is 'y' or 't' or '1';
    }

    public static LocalDate ToLocalDate(int year, int? month = null, int? day = null)
    {
        return new LocalDate(year, month ?? 1, day ?? 1);
    }
}
