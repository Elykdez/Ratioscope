using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Hypocycloid.Utils
{
    /// <summary>
    /// 字符串工具类
    /// </summary>
    public static class StringHelper
    {
        public const string ZeroWidthSpace = "\u200B";
        public const string JS_NULL = "null";

        const string PATTERN_CSV = "(?:^|,)(\"(?:[^\"]+|\"\")*\"|[^,]*)";

        #region String

        public static bool IsWhitespaceOrNonBreakingSpace(char value) =>
            char.IsWhiteSpace(value) || value == '\u200B';

        /// <summary>
        /// Returns true if the string contains any character outside the ASCII range (code point > 127),
        /// which indicates the presence of non-English scripts (CJK, Cyrillic, Arabic, etc.).
        /// </summary>
        public static bool HasNonAscii(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            foreach (char c in text)
                if (c > 127)
                    return true;
            return false;
        }

        public static string Truncate(string input, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
                return input;

            return input[..(maxLength - suffix.Length)] + suffix;
        }

        public static string TruncateWords(string input, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            var words = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (words.Length <= maxWords)
                return input;

            var truncated = string.Join(" ", words.Take(maxWords));
            return truncated;
        }

        public static string ToTitleCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
        }

        public static string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var words = input.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var result = words[0].ToLower();
            for (int i = 1; i < words.Length; i++)
            {
                result += char.ToUpper(words[i][0]) + words[i][1..].ToLower();
            }
            return result;
        }

        public static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var words = input.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var result = string.Empty;
            foreach (var word in words)
            {
                result += char.ToUpper(word[0]) + word[1..].ToLower();
            }
            return result;
        }

        public static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return string.Concat(
                    input.Select(
                        (x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString()
                    )
                )
                .ToLower();
        }

        public static string NicifyVariableName(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            int startIndex = input.StartsWith("m_", StringComparison.Ordinal) ? 2 : 0;
            int outputLength = GetNicifiedVariableNameLength(input, startIndex, out bool changed);

            if (outputLength == 0)
                return string.Empty;

            if (!changed)
                return input;

            return string.Create(
                outputLength,
                (Input: input, StartIndex: startIndex),
                static (destination, state) =>
                    WriteNicifiedVariableName(destination, state.Input, state.StartIndex)
            );
        }

        static int GetNicifiedVariableNameLength(string input, int startIndex, out bool changed)
        {
            int length = 0;
            bool firstOutput = true;
            bool pendingSpace = false;
            changed = startIndex > 0;

            for (int i = startIndex; i < input.Length; i++)
            {
                char current = input[i];
                if (IsWordSeparator(current))
                {
                    if (!firstOutput)
                        pendingSpace = true;
                    changed = true;
                    continue;
                }

                if (NeedsWordBreak(input, i, startIndex))
                    pendingSpace = true;

                if (pendingSpace)
                {
                    length++;
                    pendingSpace = false;
                    changed = true;
                }

                char output = firstOutput ? char.ToUpperInvariant(current) : current;
                if (output != current)
                    changed = true;

                length++;
                firstOutput = false;
            }

            if (pendingSpace)
                changed = true;

            return length;
        }

        static void WriteNicifiedVariableName(Span<char> destination, string input, int startIndex)
        {
            int position = 0;
            bool firstOutput = true;
            bool pendingSpace = false;

            for (int i = startIndex; i < input.Length; i++)
            {
                char current = input[i];
                if (IsWordSeparator(current))
                {
                    if (!firstOutput)
                        pendingSpace = true;
                    continue;
                }

                if (NeedsWordBreak(input, i, startIndex))
                    pendingSpace = true;

                if (pendingSpace)
                {
                    destination[position++] = ' ';
                    pendingSpace = false;
                }

                destination[position++] = firstOutput ? char.ToUpperInvariant(current) : current;
                firstOutput = false;
            }
        }

        static bool NeedsWordBreak(string input, int index, int startIndex)
        {
            if (index <= startIndex)
                return false;

            char current = input[index];
            char previous = input[index - 1];

            if (char.IsWhiteSpace(current) || char.IsWhiteSpace(previous))
                return false;

            if (char.IsUpper(current))
            {
                if (char.IsLower(previous))
                    return true;

                if (char.IsDigit(previous))
                    return index + 1 < input.Length && char.IsLower(input[index + 1]);

                return char.IsUpper(previous)
                    && index + 1 < input.Length
                    && char.IsLower(input[index + 1]);
            }

            return char.IsDigit(current) && !char.IsDigit(previous);
        }

        public static string RemoveWord(string value, string word)
        {
            int index = value.IndexOf(word, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                int end = index + word.Length;
                bool startsOnBoundary = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
                bool endsOnBoundary = end >= value.Length || !char.IsLetterOrDigit(value[end]);

                if (startsOnBoundary && endsOnBoundary)
                    value = value.Remove(index, word.Length);
                else
                    index = end;

                index = value.IndexOf(word, index, StringComparison.OrdinalIgnoreCase);
            }

            return value;
        }

        public static string RemoveToken(string value, string token)
        {
            int index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                value = value.Remove(index, token.Length);
                index = value.IndexOf(token, index, StringComparison.OrdinalIgnoreCase);
            }

            return value;
        }

        public static string CollapseSpaces(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            char[] buffer = new char[value.Length];
            int length = 0;
            bool previousWasSpace = false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isSpace = char.IsWhiteSpace(c);
                if (isSpace)
                {
                    if (previousWasSpace)
                        continue;

                    buffer[length++] = ' ';
                    previousWasSpace = true;
                    continue;
                }

                buffer[length++] = c;
                previousWasSpace = false;
            }

            return new string(buffer, 0, length).Trim();
        }

        static bool IsWordSeparator(char value) =>
            value == '_' || value == '-' || char.IsWhiteSpace(value);

        public static string ClearLastChar(string str)
        {
            return string.IsNullOrEmpty(str) ? string.Empty : str[..^1];
        }

        public static string TrimJoin(char separator, params string[] values)
        {
            if (values == null)
                return string.Empty;

            return string.Join(
                    separator,
                    values.Where(v => !string.IsNullOrEmpty(v)).Select(v => v.Trim())
                )
                .TrimStart(separator)
                .TrimEnd(separator);
        }

        public static bool Justify(string strValue)
        {
            bool flag = false;
            char[] str = "^<>'=&*, ".ToCharArray(0, 8);
            for (int i = 0; i < 8; i++)
            {
                if (strValue.IndexOf(str[i]) != -1)
                {
                    flag = true;
                    break;
                }
            }
            return flag;
        }

        /// <summary>
        /// Parses a command string into its individual components.
        /// Supports arguments enclosed in single or double quotes as a single parameter.
        /// </summary>
        /// <param name="args">The raw command input string.</param>
        /// <returns>
        /// A string array where the first element is the command name,
        /// and the remaining elements are the command parameters.
        /// If a quoted string is found, it is treated as a single parameter.
        /// </returns>
        public static string[] GetProperties(string args)
        {
            if (string.IsNullOrEmpty(args))
                return new string[0];

            // Find if there are any quotes in the input
            int startQuote = args.IndexOfAny(new char[] { '"', '\'' });
            if (startQuote != -1)
            {
                char quoteChar = args[startQuote];
                int endQuote = args.LastIndexOf(quoteChar);

                if (endQuote > startQuote)
                {
                    // Split the command part (before quotes)
                    string command = args[..startQuote].Trim();
                    // Get the quoted content without the quotes
                    string quotedContent = args[(startQuote + 1)..endQuote];

                    return new string[] { command, quotedContent };
                }
            }

            // If no quotes or invalid quotes, fall back to simple space split
            return args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>
        /// Check if a string is likely to be Base64 encoded
        /// <br/> WARN: This is a heuristic check and may not be 100% accurate
        /// </summary>
        public static bool IsLikelyBase64(string str)
        {
            if (str.Length < 100)
                return false;

            if (str.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (char c in str)
            {
                if (!(char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '='))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Check if an array content string contains large array data (floating point numbers)
        /// </summary>
        public static bool IsLikelyDataArray(string arrayContent)
        {
            if (string.IsNullOrEmpty(arrayContent) || arrayContent.Length < 10)
                return false;

            // Heuristic: 0.1765, 0.1234, 0.5678, etc.
            int commaCount = arrayContent.Count(c => c == ',');
            int dotCount = arrayContent.Count(c => c == '.');
            int digitCount = arrayContent.Count(char.IsDigit);

            return commaCount > 5 && dotCount > 5 && digitCount > 20;
        }

        /// <summary>
        /// 判断 Base64 是 PNG 或 JPG，并返回字符串 "png" / "jpg" / ""。
        /// </summary>
        public static string CheckBase64ImageType(string base64)
        {
            if (string.IsNullOrEmpty(base64))
                return "";

            try
            {
                // 去除 data:image/...;base64, 前缀
                int commaIndex = base64.IndexOf(',');
                if (commaIndex >= 0)
                    base64 = base64[(commaIndex + 1)..];

                // 只取前 32 个字符（足够判断文件头）
                int length = Math.Min(base64.Length, 32);
                var headerBytes = Convert.FromBase64String(base64[..length]);

                // PNG 魔数: 89 50 4E 47
                if (
                    headerBytes.Length >= 8
                    && headerBytes[0] == 0x89
                    && headerBytes[1] == 0x50
                    && headerBytes[2] == 0x4E
                    && headerBytes[3] == 0x47
                )
                    return "png";

                // JPEG 魔数: FF D8 FF
                if (
                    headerBytes.Length >= 3
                    && headerBytes[0] == 0xFF
                    && headerBytes[1] == 0xD8
                    && headerBytes[2] == 0xFF
                )
                    return "jpg";

                return "";
            }
            catch
            {
                return "";
            }
        }

        #endregion

        #region XML

        /// <summary>
        /// 清除xml中的不合法字符
        /// </summary>
        /// <remarks>
        /// 无效字符：
        /// 0x00 - 0x08
        /// 0x0b - 0x0c
        /// 0x0e - 0x1f
        /// </remarks>
        public static string CleanInvalidCharsForXML(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;
            else
            {
                StringBuilder checkedStringBuilder = new();
                char[] chars = input.ToCharArray();
                for (int i = 0; i < chars.Length; i++)
                {
                    int charValue = Convert.ToInt32(chars[i]);

                    if (
                        (charValue >= 0x00 && charValue <= 0x08)
                        || (charValue >= 0x0b && charValue <= 0x0c)
                        || (charValue >= 0x0e && charValue <= 0x1f)
                    )
                        continue;
                    else
                        checkedStringBuilder.Append(chars[i]);
                }

                return checkedStringBuilder.ToString();

                //string result = checkedStringBuilder.ToString();
                //result = result.Replace("�", "");
                //return Regex.Replace(result, @"[\u0000-\u0008\u000B\u000C\u000E-\u001A\uD800-\uDFFF]", delegate(Match m) { int code = (int)m.Value.ToCharArray()[0]; return (code > 9 ? "&#" + code.ToString() : "�" + code.ToString()) + ";"; });
            }
        }

        #endregion

        #region HTML

        public static string ConvertUnityLinkToHTML(string message)
        {
            // Regex pattern to find <link> tags in Unity's rich text
            string pattern = @"<link=""(.*?)"">(.*?)<\/link>";

            // Replace <link> tag with <a> tag
            string convertedMessage = Regex.Replace(
                message,
                pattern,
                m =>
                {
                    string url = m.Groups[1].Value; // Extract the URL
                    string linkText = m.Groups[2].Value; // Extract the link text
                    return $"<a href=\"{url}\" target=\"_blank\">{linkText}</a>"; // Convert to HTML <a> tag
                }
            );

            return convertedMessage;
        }

        /// <summary>
        /// html编码
        /// </summary>
        /// <param name="chr"></param>
        /// <returns></returns>
        public static string Html_text(string chr)
        {
            if (chr == null)
                return "";
            chr = chr.Replace("'", "''");
            chr = chr.Replace("<", "<");
            chr = chr.Replace(">", ">");
            return chr;
        }

        /// <summary>
        /// html解码
        /// </summary>
        /// <param name="chr"></param>
        /// <returns></returns>
        public static string Text_html(string chr)
        {
            if (chr == null)
                return "";
            chr = chr.Replace("<", "<");
            chr = chr.Replace(">", ">");
            return chr;
        }

        public static string CheckOutputString(string key)
        {
            string OutputString = key.Replace("<br>", "\n")
                .Replace("<", "<")
                .Replace(">", ">")
                .Replace(" ", " ");
            return OutputString;
        }

        #endregion

        #region CSV

        public static List<string[]> CSV2List(string csvText, bool includeHeader = false)
        {
            List<string[]> data = new();
            string[] lines = csvText.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length < 2)
                return data; // Ensure there is at least a header

            string[] headers = ParseCSVLine(lines[0]);
            if (includeHeader)
                data.Add(headers);

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue; // Skip empty lines
                string[] row = ParseCSVLine(line);
                string[] fullRow = Enumerable
                    .Range(0, headers.Length)
                    .Select(j => row.ElementAtOrDefault(j))
                    .ToArray();
                data.Add(fullRow);
            }
            return data;
        }

        public static string[] ParseCSVLine(string line)
        {
            return string.IsNullOrEmpty(line)
                ? new string[0]
                : Regex
                    .Matches(line.Trim(), PATTERN_CSV)
                    .Select(m => m.Groups[1].Value.Trim('"').Replace("\"\"", "\""))
                    .ToArray();
        }

        #endregion
    }
}
