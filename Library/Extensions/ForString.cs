﻿using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BitFn.Core.Helpers;

namespace BitFn.Core.Extensions
{
	/// <summary>
	///     Extension methods for the <see cref="string" /> class.
	/// </summary>
	public static class ForString
	{
		private static readonly Lazy<Regex> UnescapeRegex = new Lazy<Regex>(() => new Regex(
			@"\\(['""\\0abfnrtv]|x[a-fA-F0-9]{1,4}|u[a-fA-F0-9]{4}|U[a-fA-F0-9]{8}|)",
			RegexOptions.Compiled | RegexOptions.ExplicitCapture));

		/// <summary>
		///     Removes non-spacing marks from all characters, such as the accent in 'resumé'.
		/// </summary>
		/// <param name="s">A string to remove non-spacing marks from.</param>
		/// <returns>A string with non-spacing marks removed.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
		/// <seealso cref="UnicodeCategory.NonSpacingMark" />
		[Pure]
		[Obsolete("This functionality is an incomplete subset of that provided by ToAscii.")]
		public static string RemoveDiacritics(this string s)
		{
			Contract.Requires<ArgumentNullException>(s != null);
			Contract.Ensures(Contract.Result<string>() != null);

			var d = s.Normalize(NormalizationForm.FormD);
			var sb = new StringBuilder(s.Length);
			// ReSharper disable LoopCanBePartlyConvertedToQuery
			foreach (var ch in d)
			{
				var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
				if (uc != UnicodeCategory.NonSpacingMark)
					sb.Append(ch);
			}
			// ReSharper restore LoopCanBePartlyConvertedToQuery
			return (sb.ToString().Normalize(NormalizationForm.FormC));
		}

		/// <summary>
		///     Removes non-spacing marks from all characters, and replaces certain non-ASCII characters with similar ASCII
		///     strings.
		/// </summary>
		/// <param name="s">A string whose letters to convert to ASCII.</param>
		/// <returns>A string with certain non-ASCII characters replaced with similar ASCII strings.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
		[Pure]
		public static string ToAscii(this string s)
		{
			Contract.Requires<ArgumentNullException>(s != null);
			Contract.Ensures(Contract.Result<string>() != null);

			var result = new string(EnumerateAsciiCharacters(s).ToArray());
			return (result.Normalize(NormalizationForm.FormC));
		}

		/// <summary>
		///     Converts the string to a url-safe slug containing only alphanumerics and the dash character. Whitespace and
		///     punctuation are either ignored or replaced with dashes, while diacritics are removed and certain common Latin
		///     ligatures are replaced. Optionally converts open-close punctuation to parenthesis.
		/// </summary>
		/// <param name="s">A string to convert to a slug.</param>
		/// <param name="lowercase">Whether or not to convert all alpha characters to lowercase.</param>
		/// <param name="parenthetical">Whether or not to convert open and close punctuations to matched parentheses.</param>
		/// <param name="strict">Whether or not to fail when encountering unhandled characters.</param>
		/// <returns>A url-safe slug containing only alphanumerics, the dash character, and optional parentheses.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
		/// <exception cref="ArgumentOutOfRangeException">
		///     <paramref name="s" /> contains unhandled characters and <paramref name="strict" /> is <c>true</c>.
		/// </exception>
		/// <remarks>
		///     This method takes a very visually-biased and linguistically incorrect approach to certain foreign characters, such
		///     as mu (µ) to u, thorn (þ) to p, etc. Any more-delicate handling of these characters should be done beforehand.
		/// </remarks>
		/// <seealso cref="UnicodeCategory" />
		[Pure]
		public static string ToSlug(this string s, bool lowercase = false,
			bool parenthetical = false, bool strict = false)
		{
			// TODO: Refactor to use ToAscii method

			Contract.Requires<ArgumentNullException>(s != null);
			Contract.Ensures(Contract.Result<string>() != null);

			if (s.Length == 0) return string.Empty;

			var sb = new StringBuilder(s.Length);

			var wordbreak = false;
			var skipbreak = false;
			var parenCount = 0;
			foreach (var ch in EnumerateAsciiCharacters(s))
			{
				string append;
				var skipnext = false;
				if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_')
				{
					// Character is an alphanumeric
					if (lowercase && (ch >= 'A' && ch <= 'Z'))
					{
						// Append the lowercase form of this character
						append = char.ToLower(ch).ToString();
					}
					else
					{
						// Append this character as-is
						append = ch.ToString();
					}
				}
				else if (char.IsWhiteSpace(ch) || char.IsSeparator(ch) ||
				         ch == '-' || ch == '–' || ch == '—' || ch == '/' || ch == '\\')
				{
					// Break on this character.
					// Catches control-characters and other punctuations that UnicodeCategory does not.
					// It also catches certain common characters to avoid dictionary and category lookups.
					wordbreak = true;
					continue;
				}
				else
				{
					var uc = CharUnicodeInfo.GetUnicodeCategory(ch);
					switch (uc)
					{
						case UnicodeCategory.SpaceSeparator:
						case UnicodeCategory.DashPunctuation:
						case UnicodeCategory.ConnectorPunctuation:
							// Character causes word-breaks when between two alphanumeric characters.
							wordbreak = true;
							continue;
						case UnicodeCategory.OpenPunctuation:
							if (parenthetical)
							{
								// Append parenthese and skip next wordbreak
								append = "(";
								parenCount++;
								skipnext = true;
								break;
							}
							wordbreak = true;
							goto default;
						case UnicodeCategory.ClosePunctuation:
							if (parenthetical && parenCount > 0)
							{
								// Skip current wordbreak, append parenthese,
								append = ")";
								parenCount--;
								skipbreak = true;
								break;
							}
							wordbreak = true;
							goto default;
						case UnicodeCategory.MathSymbol:
							wordbreak = true;
							goto default;
						default:
							if (!strict) continue;
							var character = uc != UnicodeCategory.Control ? ch.ToString() : string.Empty;
							throw new ArgumentOutOfRangeException(nameof(s), ch,
								$"Unhandled character in UnicodeCategory.{uc}: '{character}' (0x{ch.ToHex()}).");
					}
				}
				if (wordbreak && !skipbreak && sb.Length != 0)
				{
					// We've added at least one alphanumeric character, we're
					// about to add another, and there was a break inbetween.
					sb.Append('-');
				}
				// Append character and reset whether or not we're in a break.
				sb.Append(append);
				wordbreak = false;
				skipbreak = skipnext;
			}
			if (parenthetical)
			{
				for (; parenCount > 0; parenCount--)
					sb.Append(")");
			}
			// Return slug re-normalized to FormC.
			return (sb.ToString().Normalize(NormalizationForm.FormC));
		}

		/// <summary>
		///     Converts the string to an unescaped string, following the rules for C# string literal escaping.
		/// </summary>
		/// <param name="s">A string to unescape.</param>
		/// <returns>An unescaped string.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
		/// <exception cref="FormatException"><paramref name="s" /> contains a backslash followed by invalid characters.</exception>
		/// <seealso cref="UnescapeVerbatim" />
		/// <remarks>
		///     The valid escape sequences can be found here under "Escaping in character and string literals":
		///     http://www.codeproject.com/Articles/371232/Escaping-in-Csharp-characters-strings-string-forma
		/// </remarks>
		[Pure]
		public static string Unescape(this string s)
		{
			Contract.Requires<ArgumentNullException>(s != null);
			Contract.Ensures(Contract.Result<string>() != null);

			if (s.Length == 0) return string.Empty;
			var match = UnescapeRegex.Value.Match(s);
			if (match.Success == false) return s;

			var position = 0;
			var sb = new StringBuilder(s.Length);
			do
			{
				if (match.Value.Length == 1)
				{
					throw new FormatException($"Invalid escape sequence found at character {match.Index}.");
				}
				// Append non-escaped characters between last match and this match
				if (match.Index > position) sb.Append(s, position, match.Index - position);
				// Append escaped character
				switch (match.Value[1])
				{
					case '\'':
						sb.Append('\'');
						break;
					case '"':
						sb.Append('\"');
						break;
					case '\\':
						sb.Append('\\');
						break;
					case '0':
						sb.Append('\0');
						break;
					case 'a':
						sb.Append('\a');
						break;
					case 'b':
						sb.Append('\b');
						break;
					case 'f':
						sb.Append('\f');
						break;
					case 'n':
						sb.Append('\n');
						break;
					case 'r':
						sb.Append('\r');
						break;
					case 't':
						sb.Append('\t');
						break;
					case 'v':
						sb.Append('\v');
						break;
					case 'u':
					case 'x':
						// Append single escaped Unicode character
						var c = (char)int.Parse(match.Value.Substring(2), NumberStyles.HexNumber);
						sb.Append(c);
						break;
					case 'U':
						// Append surrogate pair of Unicode characters
						var p = char.ConvertFromUtf32(int.Parse(match.Value.Substring(2), NumberStyles.HexNumber));
						sb.Append(p);
						break;
				}
				// Increment position to end oft his match
				position = match.Index + match.Length;
				// Move to the next match, if there is one
			} while ((match = match.NextMatch()).Success);
			// Append non-escaped characters between last match and end
			if (position < s.Length) sb.Append(s, position, s.Length - position);
			return sb.ToString();
		}

		/// <summary>
		///     Converts the string to an unescaped string, following the rules for C# verbatim string escaping.
		/// </summary>
		/// <param name="s">A string to unescape.</param>
		/// <returns>An unescaped string.</returns>
		/// <exception cref="ArgumentNullException"><paramref name="s" /> is <c>null</c>.</exception>
		/// <exception cref="FormatException"><paramref name="s" /> contains a quotation mark not immediately followed by another.</exception>
		/// <seealso cref="Unescape" />
		[Pure]
		public static string UnescapeVerbatim(this string s)
		{
			Contract.Requires<ArgumentNullException>(s != null);
			Contract.Ensures(Contract.Result<string>() != null);

			if (s.Length == 0) return string.Empty;
			var match = s.IndexOf('"');
			if (match == -1) return s;

			var sb = new StringBuilder(s.Length);
			var position = 0;
			do
			{
				if (match == s.Length - 1 || s[match + 1] != '"')
				{
					throw new FormatException($"Invalid escape sequence found at character {match}.");
				}
				// Append non-escaped characters between last match and this match
				if (match > position) sb.Append(s, position, match - position);
				sb.Append('"');
				// Increment position to end oft his match
				position = match + 2;
				// Move to the next match, if there is one
			} while ((match = s.IndexOf('"', position)) != -1);
			// Append non-escaped characters between last match and end
			if (position < s.Length) sb.Append(s, position, s.Length - position);
			return sb.ToString();
		}

		[Pure]
		private static IEnumerable<char> EnumerateAsciiCharacters(string s)
		{
			var formD = s.Normalize(NormalizationForm.FormD);
			UnicodeCategory? lastCategory = null;
			UnicodeCategory? breakIfNext = null;
			foreach (var ch in formD)
			{
				// First, check if this is a non-spacing mark. If so, ignore it completely.
				var category = CharUnicodeInfo.GetUnicodeCategory(ch);
				if (category == UnicodeCategory.NonSpacingMark) continue;

				// Next, check if this character non-ASCII and has a defined unicode equivalent.
				AsciiEquivalent equivalent;
				if (ch > 127 && UnicodeHelpers.TryGetAsciiEquivalent(ch, out equivalent))
				{
					// Check if we're waiting to break or should break.
					var value = equivalent.Value;
					if ((equivalent.BreakIfAfter == lastCategory && lastCategory != null) ||
					    breakIfNext == CharUnicodeInfo.GetUnicodeCategory(value[0]))
					{
						yield return ' ';
					}

					// Yield each character in the equivalent string.
					foreach (var ch2 in value)
					{
						yield return ch2;
					}

					// Set category variables for next iteration.
					breakIfNext = equivalent.BreakIfBefore;
					lastCategory = CharUnicodeInfo.GetUnicodeCategory(value[value.Length - 1]);
					continue;
				}

				// Next, check if we're waiting to break on this given category.
				if (breakIfNext == category)
				{
					yield return ' ';
				}

				// Yield the character at hand, then reset category variables.
				yield return ch;
				lastCategory = category;
				breakIfNext = null;
			}
		}
	}
}
