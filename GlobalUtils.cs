global using static AzdoPackageScrape.GlobalUtils;
using System;
using System.Linq;
using System.Text;

namespace AzdoPackageScrape
{
    internal static class GlobalUtils
    {
        public static string CsvEscape(object input)
        {
            string inputString = input?.ToString();
            return (inputString is not null && inputString.Any(MustEscape))
                ? $"\\\"{DoEscape(inputString)}\\\""
                : inputString;

            // Search for control chars, comma, and quotes.
            static bool MustEscape(char ch)
            {
                return char.IsControl(ch) || ch is ',' or '\"';
            }

            static string DoEscape(string input)
            {
                StringBuilder builder = new StringBuilder();
                foreach (char ch in input)
                {
                    if (MustEscape(ch)) { builder.Append('\\'); }
                    builder.Append(ch);
                }
                return builder.ToString();
            }
        }

        public static string UrlEncode(object input)
        {
            string toString = input?.ToString();
            if (string.IsNullOrEmpty(toString)) { return toString; }
            return Uri.EscapeDataString(toString);
        }

        public static string QueryEncode(object input)
        {
            string urlEncoded = UrlEncode(input);
            if (string.IsNullOrEmpty(urlEncoded)) { return urlEncoded; }
            return urlEncoded.Replace("%2F", "/");
        }
    }
}
