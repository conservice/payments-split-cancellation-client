# payments-split-cancellation-client

`Conservice.Payments.SplitCancellation.Client` cancels payment splits at Gatekeeper and tells the
calling service what Gatekeeper's answer means for the payment. Every lane that cancels at
Gatekeeper uses this package instead of keeping its own copy of Gatekeeper's answers, so the same
answer means the same thing everywhere.

Gatekeeper owns the tasks and does the cancelling. This package only sends the request, reads the
answer and classifies it. It has no database, no queue and no endpoints of its own: it is a
library that services reference.

Story: **[CC-Lane] CX-04 — Shared Gatekeeper cancel client and outcome contract for all lanes.**

## Why this exists

Three services (copa-eft, portal-api and the Packager) each wrote their own code to cancel
payments at Gatekeeper, each with its own copy of Gatekeeper's answers. The copies drifted, so the
same answer meant different things in different services:

| Gatekeeper answer | copa-eft | portal-api | Packager |
|---|---|---|---|
| `TaskNotFound` (pre-2.22.0) | defer | confirmed | confirmed |
| `Completed` | escalate | skip silently | defer forever |
| `NotCancellable` (redirect) | defer | skip silently | defer |

The Packager deferring `Completed` forever caused a production deadlock (2026-07-19), and a new
answer from Gatekeeper broke one consumer silently. This package replaces those copies with one
set of DTOs, one outcome enum and one classifier.

## What the package contains

```
Conservice.Payments.SplitCancellation.Client/
├── IGatekeeperCancelClient.cs           The client: CancelSplitAsync / CancelSplitsAsync
├── GatekeeperCancelClient.cs            Implementation over HttpClient (internal)
├── GatekeeperCancelClientOptions.cs     BaseAddress and Timeout
├── IGatekeeperAccessTokenProvider.cs    Implemented by each service over its own auth
├── ServiceCollectionExtensions.cs       AddGatekeeperCancelClient(...) registration
├── SplitCancelDecision.cs               SplitCancelDecision (one split) and CancelSplitsResult (one request)
├── Classification/
│   ├── CancelClassification.cs          Retry (0, default) / Confirmed / Terminal
│   └── CancelOutcomeClassifier.cs       The one outcome → classification mapping
└── Contracts/                           Gatekeeper's wire contract
    ├── CancelBatchContracts.cs          CancelBatchRequest, CancelBatchResponse, SplitCancellationResult
    ├── SplitCancellationOutcome.cs      Gatekeeper's per-split outcome enum (numbers are the wire values)
    └── CancelConflictProblem.cs         409 problem `type` constants of the single-split route

Conservice.Payments.SplitCancellation.Client.Tests/
├── CancelOutcomeClassifierShould.cs     Every outcome classified; wire numbers pinned
├── GatekeeperCancelClientShould.cs      HTTP behaviour: statuses, bodies, timeouts, tokens
├── ServiceCollectionExtensionsShould.cs Registration and options validation
└── TestDoubles.cs                       Fake HTTP handler and token provider
```

Dependencies: `Microsoft.Extensions.Http` only. There is no Flurl or authentication package
dependency, so the package never forces a version on a consumer.

## The Gatekeeper endpoint it calls

The client calls a single endpoint:

```
POST {BaseAddress}tasks/cancel-batch
Authorization: Bearer <token from IGatekeeperAccessTokenProvider>
Content-Type: application/json

{ "splitCorrelations": [ "3f2b…", "9c1d…" ] }
```

Duplicate split correlations are sent once. Gatekeeper accepts at most 100 distinct split
correlations per call (`TaskCancellation.MaxBatchSize`) and answers 400 above that, so the client
sends a larger request as several calls of up to 100 and merges the decisions in request order.
Each call's answer classifies only its own splits. A 200 answer has one result per split:

```json
{
  "results": [
    { "splitCorrelationId": "3f2b…", "outcome": 0, "message": "Task cancelled." },
    { "splitCorrelationId": "9c1d…", "outcome": 7, "message": "Task already completed." }
  ]
}
```

`outcome` is a number (`SplitCancellationOutcome`). Names are also accepted when reading.

### Why `tasks/cancel-batch` and not `tasks/{splitCorrelation}/cancel`

- It answers per split with an outcome that separates a **completed** task (Terminal) from one
  **in execution** (Retry).
- The single-split route answers the same 409 `task-not-cancellable` for both, so a caller cannot
  tell "ask again later" from "stop asking". That ambiguity is how the Packager came to defer
  completed splits forever.
- It takes one split or many, so one client covers every lane.

`CancelConflictProblem` still carries the single-split route's 409 `type` values
(`urn:conservice:gatekeeper:problem:task-not-cancellable`,
`urn:conservice:gatekeeper:problem:cancel-state-changed`) for any caller that has not moved off
that route yet.

## The three classifications

| Classification | Meaning | What the caller does |
|---|---|---|
| **Confirmed** | The split will not execute. | May fail or redirect the payment. |
| **Terminal** | Asking again will never confirm the cancel. | Stop asking. Do **not** fail the payment; reconcile the split with its execution outcome and alert. |
| **Retry** | Not resolved yet. | Do not act on the payment; ask again later. |

**Only Confirmed allows a payment to be failed or redirected.** Retry is the default value and the
answer for anything the client does not recognise, so an unknown answer can never fail a payment.

| Gatekeeper answer | Number | Classification |
|---|---|---|
| `Cancelled` | 0 | Confirmed |
| `AlreadyCancelled` | 1 | Confirmed |
| `TaskNotFound` (Gatekeeper before 2.22.0 only) | 2 | Retry |
| `NotCancellable` (in execution) | 3 | Retry |
| `RaceCondition` | 4 | Retry |
| `ValidationFailed` | 5 | Terminal |
| `Failed` | 6 | Retry |
| `Completed` | 7 | Terminal |
| `CancelRegistered` | 8 | Confirmed |
| An outcome number this version does not know | — | Retry |
| A split missing from Gatekeeper's answer | — | Retry |
| Any non-200 status for the whole request (including 400), network failure, timeout, unreadable body, no access token | — | Retry |

A whole-request 400 is Retry, not Terminal. The client never sends a request Gatekeeper's own
validation rejects (an empty list or more than 100 splits), so a 400 means a contract break or a
proxy rejected the call. It says nothing about the splits, which are most likely still cancellable.
Terminal therefore always comes with a Gatekeeper `Outcome` (`Completed` or `ValidationFailed`).

The client does not throw for anything Gatekeeper or the network does. It throws only for invalid
arguments (null or empty list) and when the caller's cancellation token is cancelled. These are
logged as errors, so they show up in monitoring:

- Terminal answers.
- HTTP 400, 401, 403 and 404 for the whole request. They classify as Retry but will not clear on
  their own: a contract break, a wrong audience or role, or a wrong `BaseAddress`.
- No access token, or the token provider throwing.

Unknown outcomes and transient failures (5xx, network, timeout) are logged as warnings.

## Usage

Register the client and give it a token provider over your service's own authentication
(`IAuthManager` is on 0.* in copa-eft and portal-api and on 1.0.0 in the Packager; each service
wraps its own).

```csharp
public sealed class GatekeeperTokenProvider(IAuthManager authManager) : IGatekeeperAccessTokenProvider
{
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var token = await authManager.GetAuthToken(cancellationToken: cancellationToken);
        return token.Failed ? null : token.Value.access_token;
    }
}

services
    .AddGatekeeperCancelClient<GatekeeperTokenProvider>(o =>
    {
        o.BaseAddress = new Uri(configuration["PaymentGatekeeperApi"]!); // the root that "tasks/cancel-batch" is appended to
        o.Timeout = TimeSpan.FromSeconds(30);                             // default 30s; a timeout classifies as Retry
    });
    // Returns an IHttpClientBuilder: add resilience handlers here if you want them.
```

`BaseAddress` must be absolute; a missing trailing slash is added. Options are validated at
startup.

Cancel and act on the classification:

```csharp
var decision = await cancelClient.CancelSplitAsync(splitCorrelation, cancellationToken);

switch (decision.Classification)
{
    case CancelClassification.Confirmed: /* fail or redirect the payment */ break;
    case CancelClassification.Terminal:  /* stop, reconcile with the execution outcome, alert */ break;
    default:                             /* Retry: leave the payment, ask again later */ break;
}
```

For several splits, `CancelSplitsAsync` returns a `CancelSplitsResult` with one
`SplitCancelDecision` per distinct split in request order, the `Confirmed`, `Terminal` and `Retry`
views of them, `AllConfirmed`, and Gatekeeper's `HttpStatusCode` (`null` when no answer arrived).
Each decision carries Gatekeeper's `Outcome` (`null` when there was none) and `Message`.

To classify an outcome you already have without calling Gatekeeper, use
`CancelOutcomeClassifier.Classify(outcome)`. `CancelOutcomeClassifier.KnownClassifications` lists
every outcome this version knows.

## Consumers and rollout

| Consumer | Plan |
|---|---|
| copa-eft | Replace its own outcome enum and cancel DTOs with this package. |
| portal-api | Replace its own outcome enum and cancel DTOs with this package. |
| Packager | Decision pending: adopt the package (and move to `cancel-batch`) or leave it until decommissioning (M-05). |
| payments-gatekeeper | Add a test that fails when Gatekeeper's `SplitCancellationOutcome` has a value this package does not classify. |

## Adding an outcome when Gatekeeper adds one

1. Append the value to `SplitCancellationOutcome` with the number Gatekeeper uses. Never renumber.
2. Classify it in `CancelOutcomeClassifier`. `ClassifyEveryOutcomeExplicitly` fails until you do.
3. Add it to the pinned numbers in `KeepTheWireNumbersStable`.
4. Bump the minor version and release. Consumers that have not upgraded classify the new value as
   Retry until they do.

Changing an existing classification changes what callers do with payments: bump the major version.

## Build, test, release

```
dotnet build
dotnet test
```

Pull requests run the unit tests through `.github/workflows/ci.yml` (the shared
`dotnet-unit-tests` workflow).

Releases use `.github/workflows/release.yaml`, which calls the shared `dotnet-nuget-build-deploy`
workflow from `infrastructure-github-actions-workflows`, the same as CommonTypes:

1. Set `<Version>` in `Conservice.Payments.SplitCancellation.Client.csproj` and merge.
2. Push a tag `v<Version>` (for example `v0.1.0`), or publish a GitHub release with that tag.
3. The workflow fails if the tag and `<Version>` differ; otherwise it builds, packs and pushes
   the package to GitHub Packages (`https://nuget.pkg.github.com/conservice/index.json`).
