using System.Xml;
using Cta.Contracts;
using static Cta.Core.ReportParsing;

namespace Cta.Core;

/// <summary>
/// An NUnit 3 XML report read as a stream in a single pass. All totals are on the root element,
/// so nothing per test stays in memory (ADR-0007). The rules come from real reports of
/// nunit3-console and NunitXml.TestLogger (ADR-0012).
/// </summary>
public sealed class NUnitReport
{
    private const int Passed = 0;
    private const int Failed = 1;
    private const int Warnings = 2;
    private const int Inconclusive = 3;
    private const int Skipped = 4;

    private static readonly string[] CounterNames =
    [
        "passed",
        "failed",
        "warnings",
        "inconclusive",
        "skipped",
    ];

    private readonly string _path;
    private readonly int _declaredTotal;
    private readonly int?[] _declared;

    private NUnitReport(
        string path,
        int declaredTotal,
        int?[] declared,
        DateTimeOffset? started,
        DateTimeOffset? finished
    )
    {
        _path = path;
        _declaredTotal = declaredTotal;
        _declared = declared;
        Started = started;
        Finished = finished;
    }

    public DateTimeOffset? Started { get; }

    public DateTimeOffset? Finished { get; }

    public static NUnitReport Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var reader = CreateReader(path);
        reader.MoveToContent();
        if (reader.LocalName != "test-run")
        {
            throw new InvalidDataException(
                $"Not an NUnit 3 XML report: root is <{reader.LocalName}>."
            );
        }

        var declaredTotal =
            ReadCount(reader, "total")
            ?? throw new InvalidDataException("NUnit 3 XML report has no total.");
        var declared = Array.ConvertAll(CounterNames, name => ReadCount(reader, name));

        return new NUnitReport(
            path,
            declaredTotal,
            declared,
            ReadZonedTime(reader, "start-time"),
            ReadZonedTime(reader, "end-time")
        );
    }

    /// <summary>
    /// Reads test cases one by one. Throws when the total or the number of any result differs
    /// from what the report declares, so an incomplete report can never pass as a complete one.
    /// </summary>
    public IEnumerable<TestResult> ReadResults()
    {
        using var reader = CreateReader(_path);
        reader.MoveToContent();

        var counts = new int[CounterNames.Length];
        var total = 0;
        string? assembly = null;

        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            switch (reader.LocalName)
            {
                case "test-run":
                    reader.Read();
                    break;
                case "test-suite":
                    if (reader.GetAttribute("type") == "Assembly")
                    {
                        assembly = reader.GetAttribute("name");
                    }

                    reader.Read();
                    break;
                case "test-case":
                    var result = Required(reader, "result");
                    var testResult = ReadTestCase(reader, result, assembly);
                    counts[CounterOf(result)]++;
                    total++;
                    yield return testResult;
                    break;
                default:
                    // Settings, environment and the failure of a whole suite belong to no test.
                    reader.Skip();
                    break;
            }
        }

        EnsureCount(_declaredTotal, total, "total");
        for (var i = 0; i < CounterNames.Length; i++)
        {
            if (_declared[i] is { } declared)
            {
                EnsureCount(declared, counts[i], CounterNames[i]);
            }
        }
    }

    private static TestResult ReadTestCase(XmlReader reader, string result, string? assembly)
    {
        var fullName = Required(reader, "fullname");
        var className = reader.GetAttribute("classname");
        var methodName = reader.GetAttribute("methodname");
        var label = reader.GetAttribute("label");
        var site = reader.GetAttribute("site");
        var duration = ReadSeconds(reader, "duration");

        string? message = null;
        string? stackTrace = null;
        string? output = null;

        using (var testCase = reader.ReadSubtree())
        {
            testCase.Read();
            testCase.Read();
            while (!testCase.EOF)
            {
                if (testCase.NodeType != XmlNodeType.Element)
                {
                    testCase.Read();
                    continue;
                }

                // ReadElementContentAsString and Skip already move to the next node.
                switch (testCase.LocalName)
                {
                    case "failure" or "reason":
                        testCase.Read();
                        break;
                    case "message":
                        message = testCase.ReadElementContentAsString();
                        break;
                    case "stack-trace":
                        stackTrace = testCase.ReadElementContentAsString();
                        break;
                    case "output":
                        output = testCase.ReadElementContentAsString();
                        break;
                    default:
                        // Assertions repeat the failure message and would overwrite it.
                        testCase.Skip();
                        break;
                }
            }
        }

        reader.Read();

        return new TestResult
        {
            // The full name is taken as it is. It may differ from class + method, for example
            // Sample.Fixture(1).Test.
            TestCase = new TestCase
            {
                FullName = fullName,
                AssemblyName = assembly,
                ClassName = className,
                MethodName = methodName is null ? null : WithoutParameters(methodName),
            },
            Outcome = ToOutcome(result, label),
            OutcomeDetail = ToDetail(result, label, site, message),
            Duration = duration,
            Failure = CreateFailure(declaredType: null, message, stackTrace),
            Output = JoinOutput(output, stdErr: null),
        };
    }

    private static TestOutcome ToOutcome(string result, string? label) =>
        result switch
        {
            "Passed" => TestOutcome.Passed,
            "Failed" when label is "Error" or "Invalid" or "Cancelled" => TestOutcome.Error,
            "Failed" => TestOutcome.Failed,
            // The test ran and nothing failed. The warning itself stays in Failures.
            "Warning" => TestOutcome.Passed,
            "Skipped" => TestOutcome.Skipped,
            "Inconclusive" => TestOutcome.Inconclusive,
            _ => throw new InvalidDataException($"Unknown NUnit result '{result}'."),
        };

    /// <summary>
    /// The most specific thing the report says. An unknown label is kept as it is: the
    /// verdict does not depend on it, and it would be lost once the XML is deleted.
    /// </summary>
    private static string? ToDetail(string result, string? label, string? site, string? message)
    {
        if (result == "Warning")
        {
            return "Warning";
        }

        if (message?.StartsWith("Test exceeded Timeout value", StringComparison.Ordinal) == true)
        {
            return "Timeout";
        }

        if (label is not null && label != "Error")
        {
            return label;
        }

        if (message?.StartsWith("TearDown :", StringComparison.Ordinal) == true)
        {
            return "Teardown";
        }

        return site switch
        {
            "SetUp" => "Setup",
            "TearDown" => "Teardown",
            "Parent" => "Parent",
            _ => null,
        };
    }

    private static int CounterOf(string result) =>
        result switch
        {
            "Passed" => Passed,
            "Failed" => Failed,
            "Warning" => Warnings,
            "Inconclusive" => Inconclusive,
            "Skipped" => Skipped,
            _ => throw new InvalidDataException($"Unknown NUnit result '{result}'."),
        };

    private static void EnsureCount(int declared, int actual, string name)
    {
        if (declared != actual)
        {
            throw new InvalidDataException(
                $"NUnit 3 XML report declares {declared} {name} but contains {actual}."
            );
        }
    }
}
