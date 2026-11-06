using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Cta.Contracts;

namespace Cta.Core;

/// <summary>
/// A TRX report read as a stream. The test class is defined after the results, so the file is read
/// twice: once for definitions and totals, then once more while results are enumerated. Only
/// test ids and shared class names stay in memory, never messages or output (ADR-0007).
/// </summary>
public sealed partial class TrxReport
{
    private readonly string _path;
    private readonly Dictionary<Guid, string> _classNames;
    private readonly int _declaredTotal;

    private TrxReport(
        string path,
        Dictionary<Guid, string> classNames,
        int declaredTotal,
        DateTimeOffset? started,
        DateTimeOffset? finished
    )
    {
        _path = path;
        _classNames = classNames;
        _declaredTotal = declaredTotal;
        Started = started;
        Finished = finished;
    }

    public DateTimeOffset? Started { get; }

    public DateTimeOffset? Finished { get; }

    public static TrxReport Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var classNames = new Dictionary<Guid, string>();
        var sharedNames = new HashSet<string>(StringComparer.Ordinal);
        int? declaredTotal = null;
        DateTimeOffset? started = null;
        DateTimeOffset? finished = null;

        using var reader = CreateReader(path);
        reader.MoveToContent();
        if (reader.LocalName != "TestRun")
        {
            throw new InvalidDataException($"Not a TRX report: root is <{reader.LocalName}>.");
        }

        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            switch (reader.LocalName)
            {
                case "Results":
                    reader.Skip();
                    continue;
                case "Times":
                    started = ReadTime(reader, "start");
                    finished = ReadTime(reader, "finish");
                    break;
                case "UnitTest":
                    ReadDefinition(reader, classNames, sharedNames);
                    continue;
                case "Counters":
                    declaredTotal = int.Parse(
                        Required(reader, "total"),
                        CultureInfo.InvariantCulture
                    );
                    break;
            }

            reader.Read();
        }

        if (declaredTotal is null)
        {
            throw new InvalidDataException("TRX report has no result counters.");
        }

        return new TrxReport(path, classNames, declaredTotal.Value, started, finished);
    }

    /// <summary>
    /// Reads results one by one. Throws when their number differs from the total the report
    /// declares, so an incomplete report can never pass as a complete one.
    /// </summary>
    public IEnumerable<TestResult> ReadResults()
    {
        using var reader = CreateReader(_path);
        var count = 0;

        if (reader.ReadToFollowing("Results", TrxNamespace) && !reader.IsEmptyElement)
        {
            reader.Read();
            while (reader.NodeType != XmlNodeType.EndElement)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "UnitTestResult")
                {
                    yield return ReadResult(reader);
                    count++;
                }
                else
                {
                    reader.Skip();
                }
            }
        }

        if (count != _declaredTotal)
        {
            throw new InvalidDataException(
                $"TRX report declares {_declaredTotal} results but contains {count}."
            );
        }
    }

    private const string TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private static XmlReader CreateReader(string path) =>
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

    private static void ReadDefinition(
        XmlReader reader,
        Dictionary<Guid, string> classNames,
        HashSet<string> sharedNames
    )
    {
        var id = Guid.Parse(Required(reader, "id"));
        using var definition = reader.ReadSubtree();
        while (definition.Read())
        {
            if (definition.NodeType == XmlNodeType.Element && definition.LocalName == "TestMethod")
            {
                var className = Required(definition, "className");
                // Most tests share a class, so the same text is kept in memory only once.
                sharedNames.Add(className);
                sharedNames.TryGetValue(className, out var shared);
                classNames[id] = shared!;
            }
        }

        reader.Read();
    }

    private TestResult ReadResult(XmlReader reader)
    {
        var testName = Required(reader, "testName");
        var outcome = ToOutcome(Required(reader, "outcome"));
        var durationText = reader.GetAttribute("duration");
        var duration = durationText is null
            ? TimeSpan.Zero
            : TimeSpan.Parse(durationText, CultureInfo.InvariantCulture);
        _classNames.TryGetValue(Guid.Parse(Required(reader, "testId")), out var className);

        string? stdOut = null;
        string? stdErr = null;
        string? message = null;
        string? stackTrace = null;

        using (var result = reader.ReadSubtree())
        {
            result.Read();
            result.Read();
            while (!result.EOF)
            {
                // ReadElementContentAsString already moves to the next node.
                switch (result.NodeType, result.LocalName)
                {
                    case (XmlNodeType.Element, "StdOut"):
                        stdOut = result.ReadElementContentAsString();
                        break;
                    case (XmlNodeType.Element, "StdErr"):
                        stdErr = result.ReadElementContentAsString();
                        break;
                    case (XmlNodeType.Element, "Message"):
                        message = result.ReadElementContentAsString();
                        break;
                    case (XmlNodeType.Element, "StackTrace"):
                        stackTrace = result.ReadElementContentAsString();
                        break;
                    default:
                        result.Read();
                        break;
                }
            }
        }

        reader.Read();

        return new TestResult
        {
            TestCase = ToTestCase(testName, className),
            Outcome = outcome,
            Duration = duration,
            Failure = ToFailure(message, stackTrace),
            Output = JoinOutput(stdOut, stdErr),
        };
    }

    private static TestCase ToTestCase(string testName, string? className)
    {
        // xUnit writes the full name into testName, NUnit only the method.
        var fullName =
            className is null || testName.StartsWith(className + ".", StringComparison.Ordinal)
                ? testName
                : $"{className}.{testName}";
        var localName = className is null ? testName : fullName[(className.Length + 1)..];
        var parameters = localName.IndexOf('(', StringComparison.Ordinal);

        return new TestCase
        {
            FullName = fullName,
            ClassName = className,
            MethodName = parameters < 0 ? localName : localName[..parameters],
        };
    }

    /// <summary>
    /// TRX uses ErrorInfo also for the reason of a skipped test, so it is kept for every outcome.
    /// </summary>
    private static Failure? ToFailure(string? message, string? stackTrace)
    {
        if (message is null && stackTrace is null)
        {
            return null;
        }

        var match = message is null ? Match.Empty : ExceptionPattern().Match(message);
        var exceptionType = match.Success ? match.Groups[1].Value : null;
        var text = match.Success ? message![match.Length..] : message;

        return new Failure
        {
            Signature = FailureSignature.Create(exceptionType, text, stackTrace),
            ExceptionType = exceptionType,
            Message = message,
            StackTrace = stackTrace,
        };
    }

    private static string? JoinOutput(string? stdOut, string? stdErr) =>
        stdOut is null ? stdErr
        : stdErr is null ? stdOut
        : $"{stdOut}\n{stdErr}";

    private static TestOutcome ToOutcome(string outcome) =>
        outcome switch
        {
            "Passed" => TestOutcome.Passed,
            "Failed" => TestOutcome.Failed,
            "Error" or "Timeout" or "Aborted" or "Disconnected" => TestOutcome.Error,
            "NotExecuted" => TestOutcome.Skipped,
            "NotRunnable" => TestOutcome.NotRun,
            "Inconclusive" => TestOutcome.Inconclusive,
            _ => throw new InvalidDataException($"Unknown TRX outcome '{outcome}'."),
        };

    private static DateTimeOffset? ReadTime(XmlReader reader, string attribute)
    {
        var text = reader.GetAttribute(attribute);
        return text is null ? null : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }

    private static string Required(XmlReader reader, string attribute) =>
        reader.GetAttribute(attribute)
        ?? throw new InvalidDataException(
            $"Element <{reader.LocalName}> has no attribute '{attribute}'."
        );

    [GeneratedRegex(@"^([A-Za-z_][\w.`]*(?:Exception|Error))\s*:\s*")]
    private static partial Regex ExceptionPattern();
}
