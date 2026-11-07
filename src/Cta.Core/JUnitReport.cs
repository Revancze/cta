using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using Cta.Contracts;
using static Cta.Core.ReportParsing;

namespace Cta.Core;

/// <summary>
/// A JUnit XML report read as a stream in a single pass. JUnit XML has no single standard, so the
/// parser follows what pytest, CTest and JunitXml.TestLogger really write (ADR-0011). Nothing per
/// test stays in memory (ADR-0007).
/// </summary>
public sealed partial class JUnitReport
{
    private readonly string _path;

    private JUnitReport(string path, DateTimeOffset? started)
    {
        _path = path;
        Started = started;
    }

    /// <summary>
    /// Start of the first suite, but only when the report states the time zone. A time without
    /// an offset may be local or UTC, and a guess would shift the history by hours.
    /// </summary>
    public DateTimeOffset? Started { get; }

    public static JUnitReport Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var reader = CreateReader(path);
        reader.MoveToContent();
        if (reader.LocalName is not ("testsuites" or "testsuite"))
        {
            throw new InvalidDataException(
                $"Not a JUnit XML report: root is <{reader.LocalName}>."
            );
        }

        DateTimeOffset? started = null;
        if (reader.LocalName == "testsuite" || reader.ReadToDescendant("testsuite"))
        {
            started = ReadTimestamp(reader);
        }

        return new JUnitReport(path, started);
    }

    /// <summary>
    /// Reads test cases one by one. Throws when their number differs from what the suites
    /// declare, so an incomplete report can never pass as a complete one.
    /// </summary>
    public IEnumerable<TestResult> ReadResults()
    {
        using var reader = CreateReader(_path);
        reader.MoveToContent();

        // Suites may be nested, and each one counts its nested suites too, so only the top
        // level is summed.
        var topLevel = reader.LocalName == "testsuite" ? 0 : 1;
        var declaredByRoot = topLevel == 1 ? OptionalCount(reader) : null;
        var declaredBySuites = 0;
        var count = 0;

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "testsuite" when reader.Depth == topLevel:
                        declaredBySuites +=
                            OptionalCount(reader)
                            ?? throw new InvalidDataException(
                                "JUnit XML suite does not declare its number of tests."
                            );
                        break;
                    case "testcase":
                        yield return ReadTestCase(reader);
                        count++;
                        continue;
                    case "system-out" or "system-err":
                        // Output of a whole suite belongs to no single test.
                        reader.Skip();
                        continue;
                }
            }

            reader.Read();
        }

        EnsureCount(declaredBySuites, count, "its suites");
        if (declaredByRoot is { } declared)
        {
            EnsureCount(declared, count, "<testsuites>");
        }
    }

    private static TestResult ReadTestCase(XmlReader reader)
    {
        var name = Required(reader, "name");
        var className = reader.GetAttribute("classname");
        var duration = ReadDuration(reader);
        var status = reader.GetAttribute("status");

        var outcome = TestOutcome.Passed;
        Failure? failure = null;
        string? stdOut = null;
        string? stdErr = null;

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
                    case "failure" or "error" or "skipped":
                        var problem = ToOutcome(testCase.LocalName);
                        var type = testCase.GetAttribute("type");
                        var message = testCase.GetAttribute("message");
                        var text = testCase.ReadElementContentAsString();
                        // Some tools write more problems for one test, the worst one wins.
                        if (Severity(problem) > Severity(outcome))
                        {
                            outcome = problem;
                            failure = CreateFailure(type, message, text);
                        }

                        break;
                    case "system-out":
                        stdOut = testCase.ReadElementContentAsString();
                        break;
                    case "system-err":
                        stdErr = testCase.ReadElementContentAsString();
                        break;
                    default:
                        testCase.Skip();
                        break;
                }
            }
        }

        reader.Read();

        // CTest marks a disabled test only by its status. Calling it passed would be a lie.
        if (outcome == TestOutcome.Passed && status is "disabled" or "notrun")
        {
            outcome = TestOutcome.NotRun;
        }

        return new TestResult
        {
            TestCase = CreateTestCase(name, className),
            Outcome = outcome,
            Duration = duration,
            Failure = failure,
            Output = JoinOutput(stdOut, stdErr),
        };
    }

    private static TestOutcome ToOutcome(string element) =>
        element switch
        {
            "failure" => TestOutcome.Failed,
            "error" => TestOutcome.Error,
            "skipped" => TestOutcome.Skipped,
            _ => throw new ArgumentOutOfRangeException(nameof(element)),
        };

    /// <summary>A test case without any problem element passed, so Passed is the lowest.</summary>
    private static int Severity(TestOutcome outcome) =>
        outcome switch
        {
            TestOutcome.Skipped => 1,
            TestOutcome.Failed => 2,
            TestOutcome.Error => 3,
            _ => 0,
        };

    private static TimeSpan ReadDuration(XmlReader reader)
    {
        var text = reader.GetAttribute("time");
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
            throw new InvalidDataException($"JUnit XML test case has invalid time '{text}'.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static int? OptionalCount(XmlReader reader)
    {
        var text = reader.GetAttribute("tests");
        if (text is null)
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new InvalidDataException($"JUnit XML has invalid number of tests '{text}'.");
    }

    private static void EnsureCount(int declared, int count, string source)
    {
        if (declared != count)
        {
            throw new InvalidDataException(
                $"JUnit XML report declares {declared} tests in {source} but contains {count}."
            );
        }
    }

    private static DateTimeOffset? ReadTimestamp(XmlReader reader)
    {
        var text = reader.GetAttribute("timestamp");
        return text is not null && TimeZonePattern().IsMatch(text)
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture)
            : null;
    }

    [GeneratedRegex(@"(?:Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex TimeZonePattern();
}
