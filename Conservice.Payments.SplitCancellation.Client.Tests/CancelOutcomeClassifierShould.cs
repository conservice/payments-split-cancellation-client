using System.Net;
using Conservice.Payments.SplitCancellation.Client.Classification;
using Conservice.Payments.SplitCancellation.Client.Contracts;

namespace Conservice.Payments.SplitCancellation.Client.Tests;

[TestFixture]
public class CancelOutcomeClassifierShould
{
    private static IEnumerable<SplitCancellationOutcome> AllOutcomes() => Enum.GetValues<SplitCancellationOutcome>();

    // Contract test: adding an outcome without classifying it fails here.
    [TestCaseSource(nameof(AllOutcomes))]
    public void ClassifyEveryOutcomeExplicitly(SplitCancellationOutcome outcome)
    {
        Assert.That(CancelOutcomeClassifier.TryClassifyKnown(outcome, out _), Is.True,
            $"{outcome} has no classification. Add it to CancelOutcomeClassifier.");
    }

    [Test]
    public void KnowExactlyTheDefinedOutcomes()
    {
        Assert.That(CancelOutcomeClassifier.KnownClassifications.Keys, Is.EquivalentTo(AllOutcomes()));
    }

    // Contract test: the numbers are Gatekeeper's wire format.
    [Test]
    public void KeepTheWireNumbersStable()
    {
        var expected = new Dictionary<string, int>
        {
            ["Cancelled"] = 0,
            ["AlreadyCancelled"] = 1,
            ["TaskNotFound"] = 2,
            ["NotCancellable"] = 3,
            ["RaceCondition"] = 4,
            ["ValidationFailed"] = 5,
            ["Failed"] = 6,
            ["Completed"] = 7,
            ["CancelRegistered"] = 8,
        };

        var actual = AllOutcomes().ToDictionary(o => o.ToString(), o => (int)o);

        Assert.That(actual, Is.EquivalentTo(expected),
            "Renumbering or removing an outcome breaks the wire contract. Append new outcomes at the end.");
    }

    [TestCase(SplitCancellationOutcome.Cancelled, CancelClassification.Confirmed)]
    [TestCase(SplitCancellationOutcome.AlreadyCancelled, CancelClassification.Confirmed)]
    [TestCase(SplitCancellationOutcome.CancelRegistered, CancelClassification.Confirmed)]
    [TestCase(SplitCancellationOutcome.Completed, CancelClassification.Terminal)]
    [TestCase(SplitCancellationOutcome.ValidationFailed, CancelClassification.Terminal)]
    [TestCase(SplitCancellationOutcome.NotCancellable, CancelClassification.Retry)]
    [TestCase(SplitCancellationOutcome.RaceCondition, CancelClassification.Retry)]
    [TestCase(SplitCancellationOutcome.Failed, CancelClassification.Retry)]
    [TestCase(SplitCancellationOutcome.TaskNotFound, CancelClassification.Retry)]
    public void ClassifyEachOutcome(SplitCancellationOutcome outcome, CancelClassification expected)
    {
        Assert.That(CancelOutcomeClassifier.Classify(outcome), Is.EqualTo(expected));
    }

    [TestCase(9)]
    [TestCase(42)]
    [TestCase(-1)]
    public void ClassifyUnknownOutcomesAsRetry(int value)
    {
        var outcome = (SplitCancellationOutcome)value;

        Assert.Multiple(() =>
        {
            Assert.That(CancelOutcomeClassifier.Classify(outcome), Is.EqualTo(CancelClassification.Retry));
            Assert.That(CancelOutcomeClassifier.TryClassifyKnown(outcome, out _), Is.False);
        });
    }

    [Test]
    public void DefaultToRetry()
    {
        Assert.That(default(CancelClassification), Is.EqualTo(CancelClassification.Retry));
    }

    [TestCase(HttpStatusCode.BadRequest, CancelClassification.Terminal)]
    [TestCase(HttpStatusCode.InternalServerError, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.BadGateway, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.ServiceUnavailable, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.Unauthorized, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.Forbidden, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.NotFound, CancelClassification.Retry)]
    [TestCase(HttpStatusCode.Conflict, CancelClassification.Retry)]
    public void ClassifyBatchFailureStatuses(HttpStatusCode status, CancelClassification expected)
    {
        Assert.That(CancelOutcomeClassifier.ClassifyBatchFailureStatus(status), Is.EqualTo(expected));
    }
}
