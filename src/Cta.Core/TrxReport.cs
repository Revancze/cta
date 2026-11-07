using System.Globalization;
using System.Xml;
using Cta.Contracts;
using static Cta.Core.ReportParsing;

namespace Cta.Core;

/// <summary>
/// A TRX report read as a stream. The test class is defined after the results, so the file is read
/// twice: once for definitions and totals, then once more while results are enumerated. Only
/// test ids and shared class names stay in memory, never messages or output (ADR-0007).
/// </summary>
public sealed class TrxReport
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
            TestCase = CreateTestCase(testName, className),
            Outcome = outcome,
            Duration = duration,
            // TRX uses ErrorInfo also for the reason of a skipped test, so it is kept for every
            // outcome.
            Failure = CreateFailure(declaredType: null, message, stackTrace),
            Output = JoinOutput(stdOut, stdErr),
        };
    }

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
}
