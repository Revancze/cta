using System.Buffers;
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
        // Parameters follow the method: "(x: 1)" in .NET, "[1]" in pytest.
        var parameters = localName.AsSpan().IndexOfAny(ParameterStart);

        return new TestCase
        {
            FullName = fullName,
            ClassName = className,
            MethodName = parameters < 0 ? localName : localName[..parameters],
        };
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

    [GeneratedRegex(@"^([A-Za-z_][\w.`$]*(?:Exception|Error))\s*:\s*")]
    private static partial Regex ExceptionPrefixPattern();
}
