using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Cta.Contracts;

namespace Cta.Core;

/// <summary>
/// What every report parser does the same way: safe XML reading, the test name, the failure and
/// the output. A new format then only maps its own elements.
/// </summary>
internal static partial class ReportParsing
{
    public const string TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private static readonly SearchValues<char> ParameterStart = SearchValues.Create("([");

    /// <summary>No DTD and no external resources, so a report cannot make the parser fetch or
    /// expand anything.</summary>
    public static XmlReader CreateReader(string path) =>
        XmlReader.Create(
            path,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreWhitespace = true,
                IgnoreComments = true,
            }
        );

    public static string Required(XmlReader reader, string attribute) =>
        reader.GetAttribute(attribute)
        ?? throw new InvalidDataException(
            $"Element <{reader.LocalName}> has no attribute '{attribute}'."
        );

    public static TestCase CreateTestCase(string name, string? className)
    {
        // CTest repeats the test name as the class name, it has no classes.
        if (string.IsNullOrEmpty(className) || className == name)
        {
            className = null;
        }

        // xUnit writes the full name, NUnit and most JUnit tools only the method.
        var fullName =
            className is null || name.StartsWith(className + ".", StringComparison.Ordinal)
                ? name
                : $"{className}.{name}";
        var localName = className is null ? fullName : fullName[(className.Length + 1)..];

        return new TestCase
        {
            FullName = fullName,
            ClassName = className,
            MethodName = WithoutParameters(localName),
        };
    }

    /// <summary>Parameters follow the method: "(x: 1)" in .NET, "[1]" in pytest.</summary>
    public static string WithoutParameters(string name)
    {
        var parameters = name.AsSpan().IndexOfAny(ParameterStart);
        return parameters < 0 ? name : name[..parameters];
    }

    /// <summary>A number the tool does not write is null, never zero.</summary>
    public static int? ReadCount(XmlReader reader, string attribute)
    {
        var text = reader.GetAttribute(attribute);
        if (text is null)
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"Report has invalid number of {attribute} '{text}'.");
    }

    public static TimeSpan ReadSeconds(XmlReader reader, string attribute)
    {
        var text = reader.GetAttribute(attribute);
        if (string.IsNullOrEmpty(text))
        {
            return TimeSpan.Zero;
        }

        if (
            !double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var seconds
            )
            || !double.IsFinite(seconds)
            || seconds < 0
        )
        {
            throw new InvalidDataException($"Report has invalid time '{text}'.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// A time without a time zone may be local or UTC, and a guess would shift the history by
    /// hours, so it counts as unknown.
    /// </summary>
    public static DateTimeOffset? ReadZonedTime(XmlReader reader, string attribute)
    {
        var text = reader.GetAttribute(attribute);
        return text is not null && TimeZonePattern().IsMatch(text)
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>
    /// The exception type comes from the report when it names one, otherwise from a message
    /// written as "Type: text". Without either the failure is grouped by message only.
    /// </summary>
    public static Failure? CreateFailure(string? declaredType, string? message, string? stackTrace)
    {
        message = EmptyAsNull(message);
        stackTrace = EmptyAsNull(stackTrace);
        if (message is null && stackTrace is null)
        {
            return null;
        }

        string? exceptionType = null;
        var text = message;
        if (declaredType is not null && ExceptionNamePattern().IsMatch(declaredType))
        {
            exceptionType = declaredType;
        }
        else if (message is not null)
        {
            var match = ExceptionPrefixPattern().Match(message);
            if (match.Success)
            {
                exceptionType = match.Groups[1].Value;
                text = message[match.Length..];
            }
        }

        return new Failure
        {
            Signature = FailureSignature.Create(exceptionType, text, stackTrace),
            ExceptionType = exceptionType,
            Message = message,
            StackTrace = stackTrace,
        };
    }

    public static string? JoinOutput(string? stdOut, string? stdErr)
    {
        stdOut = EmptyAsNull(stdOut);
        stdErr = EmptyAsNull(stdErr);
        return stdOut is null ? stdErr
            : stdErr is null ? stdOut
            : $"{stdOut}\n{stdErr}";
    }

    private static string? EmptyAsNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    [GeneratedRegex(@"^[A-Za-z_][\w.`$]*(?:Exception|Error)$")]
    private static partial Regex ExceptionNamePattern();

    // NUnit puts the place of the failure before the exception.
    [GeneratedRegex(
        @"^(?:(?:OneTimeSetUp|OneTimeTearDown|SetUp|TearDown)\s*:\s*)?([A-Za-z_][\w.`$]*(?:Exception|Error))\s*:\s*"
    )]
    private static partial Regex ExceptionPrefixPattern();

    [GeneratedRegex(@"(?:Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex TimeZonePattern();
}
