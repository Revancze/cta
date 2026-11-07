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
    /// Reads test cases one by one. Throws when the number of tests, failures, errors, skipped
    /// or disabled tests differs from what the report declares, so an incomplete report can
    /// never pass as a complete one.
    /// </summary>
    public IEnumerable<TestResult> ReadResults()
    {
        using var reader = CreateReader(_path);
        reader.MoveToContent();

        // Suites may be nested, and each one counts its nested suites too, so only the top
        // level is summed.
        var topLevel = reader.LocalName == "testsuite" ? 0 : 1;
        var declaredByRoot = topLevel == 1 ? Counts.Read(reader) : Counts.Unknown;
        var declaredBySuites = Counts.Zero;
        var actual = Counts.Zero;

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "testsuite" when reader.Depth == topLevel:
                        var suite = Counts.Read(reader);
                        if (suite.Tests is null)
                        {
                            throw new InvalidDataException(
                                "JUnit XML suite does not declare its number of tests."
                            );
                        }

                        declaredBySuites = declaredBySuites.Plus(suite);
                        break;
                    case "testcase":
                        var result = ReadTestCase(reader);
                        actual = actual.Plus(Counts.Of(result.Outcome));
                        yield return result;
                        continue;
                    case "system-out" or "system-err":
                        // Output of a whole suite belongs to no single test.
                        reader.Skip();
                        continue;
                }
            }

            reader.Read();
        }

        declaredBySuites.EnsureMatches(actual, "its suites");
        declaredByRoot.EnsureMatches(actual, "<testsuites>");
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

    /// <summary>
    /// Numbers of tests by outcome. A number the tool does not write stays null and is not
    /// checked, so a missing attribute never pretends to be zero.
    /// </summary>
    private readonly record struct Counts(
        int? Tests,
        int? Failures,
        int? Errors,
        int? Skipped,
        int? Disabled
    )
    {
        public static Counts Zero { get; } = new(0, 0, 0, 0, 0);

        public static Counts Unknown { get; } = new(null, null, null, null, null);

        public static Counts Read(XmlReader reader) =>
            new(
                Number(reader, "tests"),
                Number(reader, "failures"),
                Number(reader, "errors"),
                Number(reader, "skipped"),
                Number(reader, "disabled")
            );

        public static Counts Of(TestOutcome outcome) =>
            new(
                1,
                outcome == TestOutcome.Failed ? 1 : 0,
                outcome == TestOutcome.Error ? 1 : 0,
                outcome == TestOutcome.Skipped ? 1 : 0,
                outcome == TestOutcome.NotRun ? 1 : 0
            );

        public Counts Plus(Counts other) =>
            new(
                Add(Tests, other.Tests),
                Add(Failures, other.Failures),
                Add(Errors, other.Errors),
                Add(Skipped, other.Skipped),
                Add(Disabled, other.Disabled)
            );

        public void EnsureMatches(Counts actual, string source)
        {
            Ensure(Tests, actual.Tests, "tests", source);
            Ensure(Failures, actual.Failures, "failures", source);
            Ensure(Errors, actual.Errors, "errors", source);
            Ensure(Skipped, actual.Skipped, "skipped", source);
            Ensure(Disabled, actual.Disabled, "disabled", source);
        }

        /// <summary>One suite without the number makes the whole sum unknown.</summary>
        private static int? Add(int? left, int? right) => left + right;

        private static void Ensure(int? declared, int? actual, string name, string source)
        {
            if (declared is not null && declared != actual)
            {
                throw new InvalidDataException(
                    $"JUnit XML report declares {declared} {name} in {source} but contains {actual}."
                );
            }
        }

        private static int? Number(XmlReader reader, string attribute)
        {
            var text = reader.GetAttribute(attribute);
            if (text is null)
            {
                return null;
            }

            return int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value
            )
                ? value
                : throw new InvalidDataException(
                    $"JUnit XML has invalid number of {attribute} '{text}'."
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
