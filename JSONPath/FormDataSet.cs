//Taken from https://github.com/AngleSharp/AngleSharp/blob/0f56203a4ee28b3ce7dffaf9d8a5891daf91a243/AngleSharp/Html/FormDataSet.cs
namespace JSONPath
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;

    /// <summary>
    /// Bundles information stored in HTML forms.
    /// </summary>
    sealed class FormDataSet : IEnumerable<String>
    {
        #region Fields

        public readonly List<FormDataSetEntry> _entries;
        String _boundary;

        static readonly String[] NewLines = new[] { "\r\n", "\r", "\n" };

        #endregion

        #region ctor

        public FormDataSet()
        {
            _boundary = Guid.NewGuid().ToString();
            _entries = new List<FormDataSetEntry>();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the chosen boundary.
        /// </summary>
        public String Boundary
        {
            get { return _boundary; }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Applies the multipart/form-data algorithm.
        /// http://www.w3.org/html/wg/drafts/html/master/forms.html#multipart/form-data-encoding-algorithm
        /// </summary>
        /// <param name="encoding">(Optional) Explicit encoding.</param>
        /// <returns>A stream containing the body.</returns>
        public Stream AsMultipart(Encoding encoding = null)
        {
            return Build(encoding, stream =>
            {
                var enc = stream.Encoding;
                var entryWriters = _entries.Select(m => m.AsMultipart(enc)).
                                            Where(m => m != null);

                foreach (var entryWriter in entryWriters)
                {
                    stream.Write("--");
                    stream.WriteLine(_boundary);
                    entryWriter(stream);
                }

                stream.Write("--");
                stream.Write(_boundary);
                stream.Write("--");
            });
        }

        /// <summary>
        /// Applies the urlencoded algorithm.
        /// http://www.w3.org/html/wg/drafts/html/master/forms.html#application/x-www-form-urlencoded-encoding-algorithm
        /// </summary>
        /// <param name="encoding">(Optional) Explicit encoding.</param>
        /// <returns>A stream containing the body.</returns>
        public Stream AsUrlEncoded(Encoding encoding = null)
        {
            return Build(encoding, stream =>
            {
                var offset = 0;
                var enc = stream.Encoding;

                if (offset < _entries.Count &&
                    _entries[offset].HasName &&
                    _entries[offset].Name.Equals("IsIndex") &&
                    _entries[offset].Type.Equals("text", StringComparison.OrdinalIgnoreCase))
                {
                    stream.Write(((TextDataSetEntry)_entries[offset]).Value);
                    offset++;
                }

                var list = _entries.Skip(offset).
                                    Select(m => m.AsUrlEncoded(enc)).
                                    Where(m => m != null).
                                    ToArray();

                for (int i = 0; i < list.Length; i++)
                {
                    if (i > 0)
                        stream.Write('&');

                    stream.Write(list[i].Item1);
                    stream.Write('=');
                    stream.Write(list[i].Item2);
                }
            });
        }

        /// <summary>
        /// Applies the plain encoding algorithm.
        /// http://www.w3.org/html/wg/drafts/html/master/forms.html#text/plain-encoding-algorithm
        /// </summary>
        /// <param name="encoding">(Optional) Explicit encoding.</param>
        /// <returns>A stream containing the body.</returns>
        public Stream AsPlaintext(Encoding encoding = null)
        {
            return Build(encoding, stream =>
            {
                var list = _entries.Select(m => m.AsPlaintext()).
                                    Where(m => m != null).
                                    ToArray();

                for (int i = 0; i < list.Length; i++)
                {
                    if (i > 0)
                        stream.Write("\r\n");

                    stream.Write(list[i].Item1);
                    stream.Write('=');
                    stream.Write(list[i].Item2);
                }
            });
        }

        public void Append(String name, String value, String type)
        {
            if (String.Compare(type, "textarea", StringComparison.OrdinalIgnoreCase) == 0)
            {
                name = Normalize(name);
                value = Normalize(value);
            }

            _entries.Add(new TextDataSetEntry(name, value, type));
        }

        public void Append(String name, IFile value, String type)
        {
            if (String.Compare(type, "file", StringComparison.OrdinalIgnoreCase) == 0)
            {
                name = Normalize(name);
            }

            _entries.Add(new FileDataSetEntry(name, value, type));
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Builds the specific request body / url.
        /// </summary>
        /// <param name="encoding">The encoding to use.</param>
        /// <param name="process">The action to generate the content.</param>
        /// <returns>The constructed stream.</returns>
        Stream Build(Encoding encoding, Action<StreamWriter> process)
        {
            encoding = encoding ?? Encoding.UTF8;
            var ms = new MemoryStream();
            CheckBoundaries(encoding);
            ReplaceCharset(encoding);
            var tw = new StreamWriter(ms, encoding);
            process(tw);
            tw.Flush();
            ms.Position = 0;
            return ms;
        }

        /// <summary>
        /// Replaces a charset field (if any) that is hidden with the given
        /// character encoding.
        /// </summary>
        /// <param name="encoding">The encoding to use.</param>
        void ReplaceCharset(Encoding encoding)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];

                if (!String.IsNullOrEmpty(entry.Name) && entry.Name.Equals("_charset_") &&
                    entry.Type.Equals("hidden", StringComparison.OrdinalIgnoreCase))
                {
                    _entries[i] = new TextDataSetEntry(entry.Name, encoding.WebName, entry.Type);
                }
            }
        }

        /// <summary>
        /// Checks the entries for boundary collisions. If a collision is
        /// detected, then a new boundary string is generated. This algorithm
        /// will produce a boundary string that satisfies all requirements.
        /// </summary>
        /// <param name="encoding">The encoding to use.</param>
        void CheckBoundaries(Encoding encoding)
        {
            var found = false;

            do
            {
                for (int i = 0; i < _entries.Count; i++)
                {
                    if (found = _entries[i].Contains(_boundary, encoding))
                    {
                        _boundary = Guid.NewGuid().ToString();
                        break;
                    }
                }
            } while (found);
        }

        /// <summary>
        /// Replaces every occurrence of a "CR" (U+000D) character not followed
        /// by a "LF" (U+000A) character, and every occurrence of a "LF"
        /// (U+000A) character not preceded by a "CR" (U+000D) character, by a
        /// two-character string consisting of a U+000D CARRIAGE RETURN "CRLF"
        /// (U+000A) character pair.
        /// </summary>
        /// <param name="value">The value to normalize.</param>
        /// <returns>The normalized string.</returns>
        static String Normalize(String value)
        {
            if (!String.IsNullOrEmpty(value))
            {
                var lines = value.Split(NewLines, StringSplitOptions.None);
                return String.Join("\r\n", lines);
            }

            return value;
        }

        #endregion

        #region IEnumerable Implementation

        /// <summary>
        /// Gets an enumerator over all entry names.
        /// </summary>
        /// <returns>The enumerator.</returns>
        public IEnumerator<String> GetEnumerator()
        {
            return _entries.Select(m => m.Name).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }

    /// <summary>
    /// Encapsulates the data contained in an entry.
    /// </summary>
    abstract class FormDataSetEntry
    {
        readonly String _name;
        readonly String _type;

        public FormDataSetEntry(String name, String type)
        {
            _name = name;
            _type = type;
        }

        /// <summary>
        /// Gets if the name has been given.
        /// </summary>
        public Boolean HasName
        {
            get { return _name != null; }
        }

        /// <summary>
        /// Gets the entry's name.
        /// </summary>
        public String Name
        {
            get { return _name ?? String.Empty; }
        }

        /// <summary>
        /// Gets the entry's type.
        /// </summary>
        public String Type
        {
            get { return _type ?? "text"; }
        }

        public abstract Action<StreamWriter> AsMultipart(Encoding encoding);

        public abstract Tuple<String, String> AsPlaintext();

        public abstract Tuple<String, String> AsUrlEncoded(Encoding encoding);

        public abstract Boolean Contains(String boundary, Encoding encoding);
    }

    sealed class TextDataSetEntry : FormDataSetEntry
    {
        readonly String _value;

        public TextDataSetEntry(String name, String value, String type)
            : base(name, type)
        {
            _value = value;
        }

        /// <summary>
        /// Gets if the value has been given.
        /// </summary>
        public Boolean HasValue
        {
            get { return _value != null; }
        }

        /// <summary>
        /// Gets the entry's value.
        /// </summary>
        public String Value
        {
            get { return _value; }
        }

        public override Boolean Contains(String boundary, Encoding encoding)
        {
            if (_value == null)
                return false;

            return _value.Contains(boundary);
        }

        public override Action<StreamWriter> AsMultipart(Encoding encoding)
        {
            if (HasName && HasValue)
            {
                return stream =>
                {
                    stream.WriteLine(String.Concat("Content-Disposition: form-data; name=\"",
                        Name.HtmlEncode(encoding), "\""));
                    stream.WriteLine();
                    stream.WriteLine(_value.HtmlEncode(encoding));
                };
            }

            return null;
        }

        public override Tuple<String, String> AsPlaintext()
        {
            if (HasName && HasValue)
                return Tuple.Create(Name, _value);

            return null;
        }

        public override Tuple<String, String> AsUrlEncoded(Encoding encoding)
        {
            if (HasName && HasValue)
            {
                var name = encoding.GetBytes(Name);
                var value = encoding.GetBytes(_value);
                return Tuple.Create(name.UrlEncode(), value.UrlEncode());
            }

            return null;
        }
    }

    sealed class FileDataSetEntry : FormDataSetEntry
    {
        readonly IFile _value;

        public FileDataSetEntry(String name, IFile value, String type)
            : base(name, type)
        {
            _value = value;
        }

        /// <summary>
        /// Gets if the value has been given.
        /// </summary>
        public Boolean HasValue
        {
            get { return _value != null && _value.Name != null; }
        }

        /// <summary>
        /// Gets if the value has a body and type.
        /// </summary>
        public Boolean HasValueBody
        {
            get { return _value != null && _value.Body != null && _value.Type != null; }
        }

        /// <summary>
        /// Gets the entry's value.
        /// </summary>
        public IFile Value
        {
            get { return _value; }
        }

        public String FileName
        {
            get { return _value != null ? _value.Name : String.Empty; }
        }

        public String ContentType
        {
            get { return _value != null ? _value.Type : "application/octet-stream"; }
        }

        public override Boolean Contains(String boundary, Encoding encoding)
        {
            if (_value == null || _value.Body == null)
                return false;

            //TODO boundary check required?
            return false;
        }

        public override Action<StreamWriter> AsMultipart(Encoding encoding)
        {
            if (HasName)
            {
                return stream =>
                {
                    var hasContent = HasValue && HasValueBody;

                    stream.WriteLine("Content-Disposition: form-data; name=\"{0}\"; filename=\"{1}\"",
                        Name.HtmlEncode(encoding), FileName.HtmlEncode(encoding));

                    stream.WriteLine("Content-Type: " + ContentType);
                    stream.WriteLine();

                    if (hasContent)
                    {
                        stream.Flush();
                        _value.Body.CopyTo(stream.BaseStream);
                    }

                    stream.WriteLine();
                };
            }

            return null;
        }

        public override Tuple<String, String> AsPlaintext()
        {
            if (HasName && HasValue)
                return Tuple.Create(Name, _value.Name);

            return null;
        }

        public override Tuple<String, String> AsUrlEncoded(Encoding encoding)
        {
            if (HasName && HasValue)
            {
                var name = encoding.GetBytes(Name);
                var value = encoding.GetBytes(_value.Name);
                return Tuple.Create(name.UrlEncode(), value.UrlEncode());
            }

            return null;
        }
    }

    /// <summary>
    /// Represents a concrete file.
    /// http://dev.w3.org/2006/webapi/FileAPI/#dfn-file
    /// </summary>
    //[DomName("File")]
    public interface IFile : IBlob
    {
        /// <summary>
        /// Gets the file's name.
        /// </summary>
        //[DomName("name")]
        String Name { get; }

        /// <summary>
        /// Gets the last modified date of the file.
        /// </summary>
        //[DomName("lastModified")]
        DateTime LastModified { get; }
    }

    /// <summary>
    /// Represents a binary large object.
    /// http://dev.w3.org/2006/webapi/FileAPI/#dfn-Blob
    /// </summary>
    //[DomName("Blob")]
    public interface IBlob : IDisposable
    {
        /// <summary>
        /// Gets the length of the blob.
        /// </summary>
        //[DomName("size")]
        Int32 Length { get; }

        /// <summary>
        /// Gets the mime-type of the blob.
        /// </summary>
        //[DomName("type")]
        String Type { get; }

        /// <summary>
        /// Gets if the stream to the blob is closed.
        /// </summary>
        //[DomName("isClosed")]
        Boolean IsClosed { get; }

        /// <summary>
        /// Gets the stream to the file.
        /// </summary>
        Stream Body { get; }

        /// <summary>
        /// Slices a subset of the blob into a another blob.
        /// </summary>
        /// <param name="start">The start of the slicing in bytes.</param>
        /// <param name="end">The end of the slicing in bytes.</param>
        /// <param name="contentType">The mime-type of the new blob.</param>
        /// <returns>A new blob with this blob's subset.</returns>
        //[DomName("slice")]
        IBlob Slice(Int32 start = 0, Int32 end = Int32.MaxValue, String contentType = null);

        /// <summary>
        /// Closes the stream to the blob.
        /// </summary>
        //[DomName("close")]
        void Close();
    }

    public static class StringExtensions
    {
        /// <summary>
        /// Replaces characters in names and values that cannot be expressed by using the given
        /// encoding with &amp;#...; base-10 unicode point.
        /// </summary>
        /// <param name="value">The value to sanatize.</param>
        /// <param name="encoding">The encoding to consider.</param>
        /// <returns>The sanatized value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static String HtmlEncode(this String value, Encoding encoding)
        {
            //TODO Decide if the encoding is sufficient (How?)
            return value;
        }

        /// <summary>
        /// Replaces characters in names and values that should not be in URL
        /// values. Replaces the bytes 0x20 (U+0020 SPACE if interpreted as
        /// ASCII) with a single 0x2B byte ("+" (U+002B) character if
        /// interpreted as ASCII). If a byte is not in the range 0x2A, 0x2D,
        /// 0x2E, 0x30 to 0x39, 0x41 to 0x5A, 0x5F, 0x61 to 0x7A, it is 
        /// replaced with its hexadecimal value (zero-padded if necessary), 
        /// starting with the percent sign.
        /// </summary>
        /// <param name="content">The content to encode.</param>
        /// <returns>The encoded value.</returns>
        public static String UrlEncode(this Byte[] content)
        {
            var builder = new StringBuilder();

            for (int i = 0; i < content.Length; i++)
            {
                var chr = (Char)content[i];

                if (chr == Symbols.Space)
                    builder.Append(Symbols.Plus);
                else if (chr == Symbols.Asterisk || chr == Symbols.Minus || chr == Symbols.Dot || chr == Symbols.Underscore || chr == Symbols.Tilde || chr.IsAlphanumericAscii())
                    builder.Append(chr);
                else
                    builder.Append(Symbols.Percent).Append(content[i].ToString("X2"));
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Useful methods for chars.
    /// </summary>
    [DebuggerStepThrough]
    static class CharExtensions
    {
        /// <summary>
        /// Converts a given character from the hex representation (0-9A-Fa-f)
        /// to an integer.
        /// </summary>
        /// <param name="c">The character to convert.</param>
        /// <returns>
        /// The integer value or undefined behavior if invalid.
        /// </returns>
        public static Int32 FromHex(this Char c)
        {
            return c.IsDigit() ? c - 0x30 : c - (c.IsLowercaseAscii() ? 0x57 : 0x37);
        }

        /// <summary>
        /// Transforms the given number to a hexadecimal string.
        /// </summary>
        /// <param name="num">The number (0-255).</param>
        /// <returns>A 2 digit upper case hexadecimal string.</returns>
        public static String ToHex(this Byte num)
        {
            var chrs = new Char[2];
            var rem = num >> 4;
            chrs[0] = (Char)(rem + (rem < 10 ? 48 : 55));
            rem = num - 16 * rem;
            chrs[1] = (Char)(rem + (rem < 10 ? 48 : 55));
            return new String(chrs);
        }

        /// <summary>
        /// Transforms the given character to a hexadecimal string.
        /// </summary>
        /// <param name="character">The single character.</param>
        /// <returns>A minimal digit lower case hexadecimal string.</returns>
        public static String ToHex(this Char character)
        {
            return ((Int32)character).ToString("x");
        }

        /// <summary>
        /// Determines if the given character is in the given range.
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <param name="lower">The lower bound of the range.</param>
        /// <param name="upper">The upper bound of the range.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsInRange(this Char c, Int32 lower, Int32 upper)
        {
            return c >= lower && c <= upper;
        }

        /// <summary>
        /// Determines if the given character is allowed as-it-is in queries.
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsNormalQueryCharacter(this Char c)
        {
            return c.IsInRange(0x21, 0x7e) && c != Symbols.DoubleQuote &&
                c != Symbols.CurvedQuote && c != Symbols.Num &&
                c != Symbols.LessThan && c != Symbols.GreaterThan;
        }

        /// <summary>
        /// Determines if the given character is allowed as-it-is in paths.
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsNormalPathCharacter(this Char c)
        {
            return c.IsInRange(0x20, 0x7e) && c != Symbols.DoubleQuote &&
                c != Symbols.CurvedQuote && c != Symbols.Num &&
                c != Symbols.LessThan && c != Symbols.GreaterThan &&
                c != Symbols.Space && c != Symbols.QuestionMark;
        }

        /// <summary>
        /// Determines if the given character is a uppercase character (A-Z) as
        /// specified here:
        /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#uppercase-ascii-letters
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsUppercaseAscii(this Char c)
        {
            return c >= 0x41 && c <= 0x5a;
        }

        /// <summary>
        /// Determines if the given character is a lowercase character (a-z) as
        /// specified here:
        /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#lowercase-ascii-letters
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsLowercaseAscii(this Char c)
        {
            return c >= 0x61 && c <= 0x7a;
        }

        /// <summary>
        /// Determines if the given character is a alphanumeric character
        /// (0-9a-zA-z) as specified here:
        /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#alphanumeric-ascii-characters
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsAlphanumericAscii(this Char c)
        {
            return c.IsDigit() || c.IsUppercaseAscii() || c.IsLowercaseAscii();
        }

        /// <summary>
        /// Determines if the given character is a hexadecimal (0-9a-fA-F) as
        /// specified here:
        /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#ascii-hex-digits
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsHex(this Char c)
        {
            return c.IsDigit() || (c >= 0x41 && c <= 0x46) || (c >= 0x61 && c <= 0x66);
        }

        /// <summary>
        /// Gets if the character is actually a non-ascii character.
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsNonAscii(this Char c)
        {
            return c >= 0x80;
        }

        /// <summary>
        /// Gets if the character is actually a non-printable (special)
        /// character.
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsNonPrintable(this Char c)
        {
            return (c >= 0x0 && c <= 0x8) || (c >= 0xe && c <= 0x1f) || (c >= 0x7f && c <= 0x9f);
        }

        /// <summary>
        /// Gets if the character is actually a (A-Z,a-z) letter.
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsLetter(this Char c)
        {
            return IsUppercaseAscii(c) || IsLowercaseAscii(c);
        }

        /// <summary>
        /// Gets if the character is actually a name character.
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsName(this Char c)
        {
            return c >= 0x80 || c.IsLetter() || c == Symbols.Underscore || c == Symbols.Minus || c.IsDigit();
        }

        /// <summary>
        /// Determines if the given character is a valid character for starting
        /// an identifier.
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsNameStart(this Char c)
        {
            return c >= 0x80 || IsUppercaseAscii(c) || IsLowercaseAscii(c) || c == Symbols.Underscore;
        }

        /// <summary>
        /// Determines if the given character is a line break character as
        /// specified here:
        /// http://www.w3.org/TR/html401/struct/text.html#h-9.3.2
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsLineBreak(this Char c)
        {
            return c == Symbols.LineFeed || c == Symbols.CarriageReturn;
        }

        /// <summary>
        /// Determines if the given character is a space character as specified
        /// here:
        /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#space-character
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsSpaceCharacter(this Char c)
        {
            return c == Symbols.Space || c == Symbols.Tab || c == Symbols.LineFeed || c == Symbols.CarriageReturn || c == Symbols.FormFeed;
        }

        /// <summary>
        /// Determines if the given character is a white-space character as
        /// specified here:
        /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#white_space
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsWhiteSpaceCharacter(this Char c)
        {
            return c.IsInRange(0x0009, 0x000d) || c == 0x0020 || c == 0x0085 || c == 0x00a0 ||
                    c == 0x1680 || c == 0x180e || c.IsInRange(0x2000, 0x200a) || c == 0x2028 ||
                    c == 0x2029 || c == 0x202f || c == 0x205f || c == 0x3000;
        }

        /// <summary>
        /// Determines if the given character is a digit (0-9) as specified
        /// here:
        /// http://www.whatwg.org/specs/web-apps/current-work/multipage/common-microsyntaxes.html#ascii-digits
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsDigit(this Char c)
        {
            return c >= 0x30 && c <= 0x39;
        }

        /// <summary>
        /// Determines if the given character is a valid url code point as specified here:
        /// http://url.spec.whatwg.org/#url-code-points
        /// </summary>
        /// <param name="c">The character to examine.</param>
        /// <returns>The result of the test.</returns>
        public static Boolean IsUrlCodePoint(this Char c)
        {
            return c.IsAlphanumericAscii() || c == Symbols.ExclamationMark || c == Symbols.Dollar || c == Symbols.Ampersand ||
                   c == Symbols.SingleQuote || c == Symbols.RoundBracketOpen || c == Symbols.RoundBracketClose ||
                   c == Symbols.Asterisk || c == Symbols.Plus || c == Symbols.Minus || c == Symbols.Comma ||
                   c == Symbols.Dot || c == Symbols.Solidus || c == Symbols.Colon || c == Symbols.Semicolon ||
                   c == Symbols.Equality || c == Symbols.QuestionMark || c == Symbols.At || c == Symbols.Underscore ||
                   c == Symbols.Tilde || c.IsInRange(0xa0, 0xd7ff) || c.IsInRange(0xe000, 0xfdcf) || c.IsInRange(0xfdf0, 0xfffd) ||
                   c.IsInRange(0x10000, 0x1FFFD) || c.IsInRange(0x20000, 0x2fffd) || c.IsInRange(0x30000, 0x3fffd) || c.IsInRange(0x40000, 0x4fffd) ||
                   c.IsInRange(0x50000, 0x5fffd) || c.IsInRange(0x60000, 0x6fffd) || c.IsInRange(0x70000, 0x7fffd) || c.IsInRange(0x80000, 0x8fffd) ||
                   c.IsInRange(0x90000, 0x9fffd) || c.IsInRange(0xa0000, 0xafffd) || c.IsInRange(0xb0000, 0xbfffd) || c.IsInRange(0xc0000, 0xcfffd) ||
                   c.IsInRange(0xd0000, 0xdfffd) || c.IsInRange(0xe0000, 0xefffd) || c.IsInRange(0xf0000, 0xffffd) || c.IsInRange(0x100000, 0x10fffd);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Boolean IsInvalid(this Int32 utf32)
        {
            return utf32 == 0 || utf32 > Symbols.MaximumCodepoint || (utf32 > 0xD800 && utf32 < 0xDFFF);
        }
    }

    /// <summary>
    /// Contains useful information from the specification.
    /// </summary>
    static class Symbols
    {
        /// <summary>
        /// The end of file character 26.
        /// </summary>
        public const Char EndOfFile = (Char)0x1a;

        /// <summary>
        /// The tilde character ( ~ ).
        /// </summary>
        public const Char Tilde = (Char)0x7e;

        /// <summary>
        /// The pipe character ( | ).
        /// </summary>
        public const Char Pipe = (Char)0x7c;

        /// <summary>
        /// The null character.
        /// </summary>
        public const Char Null = (Char)0x0;

        /// <summary>
        /// The ampersand character ( &amp; ).
        /// </summary>
        public const Char Ampersand = (Char)0x26;

        /// <summary>
        /// The number sign character ( # ).
        /// </summary>
        public const Char Num = (Char)0x23;

        /// <summary>
        /// The dollar sign character ( $ ).
        /// </summary>
        public const Char Dollar = (Char)0x24;

        /// <summary>
        /// The semicolon sign ( ; ).
        /// </summary>
        public const Char Semicolon = (Char)0x3b;

        /// <summary>
        /// The asterisk character ( * ).
        /// </summary>
        public const Char Asterisk = (Char)0x2a;

        /// <summary>
        /// The equals sign ( = ).
        /// </summary>
        public const Char Equality = (Char)0x3d;

        /// <summary>
        /// The plus sign ( + ).
        /// </summary>
        public const Char Plus = (Char)0x2b;

        /// <summary>
        /// The dash ( hypen minus, - ) character.
        /// </summary>
        public const Char Minus = (Char)0x2d;

        /// <summary>
        /// The comma character ( , ).
        /// </summary>
        public const Char Comma = (Char)0x2c;

        /// <summary>
        /// The full stop ( . ).
        /// </summary>
        public const Char Dot = (Char)0x2e;

        /// <summary>
        /// The circumflex accent ( ^ ) character.
        /// </summary>
        public const Char Accent = (Char)0x5e;

        /// <summary>
        /// The commercial at ( @ ) character.
        /// </summary>
        public const Char At = (Char)0x40;

        /// <summary>
        /// The opening angle bracket ( LESS-THAN-SIGN ).
        /// </summary>
        public const Char LessThan = (Char)0x3c;

        /// <summary>
        /// The closing angle bracket ( GREATER-THAN-SIGN ).
        /// </summary>
        public const Char GreaterThan = (Char)0x3e;

        /// <summary>
        /// The single quote / quotation mark ( ' ).
        /// </summary>
        public const Char SingleQuote = (Char)0x27;

        /// <summary>
        /// The (double) quotation mark ( " ).
        /// </summary>
        public const Char DoubleQuote = (Char)0x22;

        /// <summary>
        /// The (curved) quotation mark ( ` ).
        /// </summary>
        public const Char CurvedQuote = (Char)0x60;

        /// <summary>
        /// The question mark ( ? ).
        /// </summary>
        public const Char QuestionMark = (Char)0x3f;

        /// <summary>
        /// The tab character.
        /// </summary>
        public const Char Tab = (Char)0x09;

        /// <summary>
        /// The line feed character.
        /// </summary>
        public const Char LineFeed = (Char)0x0a;

        /// <summary>
        /// The carriage return character.
        /// </summary>
        public const Char CarriageReturn = (Char)0x0d;

        /// <summary>
        /// The form feed character.
        /// </summary>
        public const Char FormFeed = (Char)0x0c;

        /// <summary>
        /// The space character.
        /// </summary>
        public const Char Space = (Char)0x20;

        /// <summary>
        /// The slash (solidus, /) character.
        /// </summary>
        public const Char Solidus = (Char)0x2f;

        /// <summary>
        /// The no break space character.
        /// </summary>
        public const Char NoBreakSpace = (Char)0xa0;

        /// <summary>
        /// The backslash ( reverse-solidus, \ ) character.
        /// </summary>
        public const Char ReverseSolidus = (Char)0x5c;

        /// <summary>
        /// The colon ( : ) character.
        /// </summary>
        public const Char Colon = (Char)0x3a;

        /// <summary>
        /// The exclamation mark ( ! ) character.
        /// </summary>
        public const Char ExclamationMark = (Char)0x21;

        /// <summary>
        /// The replacement character in case of errors.
        /// </summary>
        public const Char Replacement = (Char)0xfffd;

        /// <summary>
        /// The low line ( _ ) character.
        /// </summary>
        public const Char Underscore = (Char)0x5f;

        /// <summary>
        /// The round bracket open ( ( ) character.
        /// </summary>
        public const Char RoundBracketOpen = (Char)0x28;

        /// <summary>
        /// The round bracket close ( ) ) character.
        /// </summary>
        public const Char RoundBracketClose = (Char)0x29;

        /// <summary>
        /// The square bracket open ( [ ) character.
        /// </summary>
        public const Char SquareBracketOpen = (Char)0x5b;

        /// <summary>
        /// The square bracket close ( ] ) character.
        /// </summary>
		public const Char SquareBracketClose = (Char)0x5d;

        /// <summary>
        /// The curly bracket open ( { ) character.
        /// </summary>
        public const Char CurlyBracketOpen = (Char)0x7b;

        /// <summary>
        /// The curly bracket close ( } ) character.
        /// </summary>
        public const Char CurlyBracketClose = (Char)0x7d;

        /// <summary>
        /// The percent ( % ) character.
        /// </summary>
        public const Char Percent = (Char)0x25;

        /// <summary>
        /// The maximum allowed codepoint (defined in Unicode).
        /// </summary>
        public const Int32 MaximumCodepoint = 0x10FFFF;

        /// <summary>
        /// A list of available punycode character mappings.
        /// </summary>
        public static Dictionary<Char, Char> Punycode = new Dictionary<Char, Char>
        {
            { '。', '.' },
            { '．', '.' },
            { 'Ｇ', 'g' },
            { 'ｏ', 'o' },
            { 'ｃ', 'c' },
            { 'Ｘ', 'x' },
            { '０', '0' },
            { '１', '1' },
            { '２', '2' },
            { '５', '5' },
        };
    }
}
