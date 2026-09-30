# 06. Widget session identity and bootstrap trust

Read [StartConversation.cs](../../src/NetIntercom.Application/Widget/StartConversation.cs), [WidgetController.cs](../../src/NetIntercom.Api/Controllers/WidgetController.cs), and [widget api.ts](../../frontend/widget/src/api.ts). The session endpoint creates or resumes a visitor and returns a visitor token with the latest conversation and public transcript. It is therefore an identity and data-access boundary, not merely a convenient initial fetch.

This chapter uses source inspection. No live visitor identities, real tokens, browser requests, or production data are involved. Proposed authentication and merge contracts remain design exercises.

## The client identity is persistent but client-controlled

getAnonymousId reads a named browser cookie and returns its value when present. Otherwise it creates an identifier using a UUID with hyphens removed, writes a one-year cookie with path and SameSite attributes, and returns the value. This gives a browser a stable label across visits under the selected storage behavior.

Persistence is not the same as authenticity. A value supplied by a browser is not automatically proof that the caller owns an existing visitor record. The session request also accepts optional external identity and profile attributes from widget configuration. A stronger identification contract would need to explain who vouches for those values and how the server verifies that evidence.

Do not infer such proof from the name ExternalId. An identifier can be useful for correlation while remaining untrusted input. The selected controller marks session startup AllowAnonymous, and the displayed command contains no signature or host-authentication proof. That is a concrete boundary to review, not an invitation to test other people's identities.

A safe exercise uses two synthetic visitors and controlled request objects. It asks which record the current predicate selects and which proof a proposed production identification mode should require. The output should contain synthetic labels rather than usable visitor tokens.

## The lookup combines two alternatives

The handler searches for a visitor satisfying a nonnull matching ExternalId or matching AnonymousId. Parentheses in your worksheet should reflect the actual boolean grouping: the external comparison is conditional on request.ExternalId being nonnull; the anonymous comparison remains an alternative.

If external identity points to visitor A while anonymous identity points to visitor B, both records can satisfy different sides of the predicate. FirstOrDefault does not express a conflict policy between them. The displayed query adds no ordering that would turn this into a deliberate identity precedence rule.

This is an important merge boundary. It is not enough to say external identity wins because it appears first in the expression. Boolean alternatives determine membership, not ranking. A proposed contract should reject conflicting matches, choose an explicitly authorized identity, or invoke a separately defined merge workflow.

The existing visitor then receives nonnull name, email, external identity, and attributes from the request. Null preserves an existing field. Empty strings are nonnull and can therefore have different update semantics, subject to any validation. A null-aware patch is not equivalent to a nonempty-only patch.

## Creating and updating before token issuance

When no visitor matches, the handler constructs a new visitor with the supplied anonymous identity and a clock-derived creation time. It adds that visitor, applies profile updates, records last seen, and saves before reading conversations and issuing the returned token.

The source makes several clock calls rather than capturing one command time. A deterministic service fixture should define its clock behavior and compare expected values accordingly. It should not silently assume creation and last-seen timestamps are identical under an advancing clock.

After save, the handler finds the visitor's latest conversation by descending LastMessageAtUtc. Closed conversations are not excluded by this displayed query. Session resume can therefore return a closed conversation; later send behavior decides whether to reopen it or start a new one according to its command.

Equal LastMessageAtUtc values are another tie case. A stable secondary ordering is not shown. If a product requires deterministic latest-conversation selection under ties, specify the tie-breaker and create a fixture where insertion and timestamp order differ.

## Transcript projection is audience-specific

When a conversation exists, the handler loads its public messages, excluding IsInternalNote, ordered by CreatedAtUtc. Agent sender names come from the agent navigation. Other sender names use the visitor's name or You under this path. The returned session contains visitor DTO, conversation DTO, and message DTOs.

The confidentiality filter is an explicit query condition. A test should insert a public agent reply, a visitor message, and an internal note with similar timestamps, then compare the exact returned identity set. A count-only assertion can miss replacing one public message with one private note.

The conversation DTO in this startup response is broader than the small realtime visitor projection. That difference is visible in the types and return construction. A privacy review should inspect each visitor surface independently rather than assume every path shares the notifier's reduced payload.

The query shown has no transcript page bound. Returning all messages can be acceptable for a small teaching dataset, but a production pagination proposal needs continuation and initial-render semantics. Adding a limit without saying how the widget obtains older history silently changes the user's access to the conversation.

## Token issuance does not repair identity selection

The token service receives the selected visitor after the lookup and save. A correctly signed token can faithfully represent the wrong visitor if the preceding selection used insufficient proof. Cryptographic integrity and identity authorization are separate boundaries.

A test plan should therefore first establish visitor selection and ownership, then token claims and validation. Starting with a valid token and testing only later message endpoints cannot prove the anonymous bootstrap boundary is correct. Conversely, a perfect bootstrap selector does not prove the token validator accepts only intended audiences and issuers.

Keep these tests separate enough to diagnose failure. One fixture can provide a token-service double and inspect which visitor it receives. Another can exercise the real token service with synthetic configuration. A final host test can establish the HTTP policy and resulting access. None should expose raw authentication material in a shared report.

## Proposed conflict contract

Choose a simple teaching contract: anonymous-only resume may use a server-issued opaque credential, identified resume requires a trusted host assertion, and contradictory identities return a bounded conflict without changing either visitor. This is a proposal, not current behavior.

The unchanged-state criterion includes both visitor profiles, conversation ownership, last-seen timestamps, and any accepted identity-link audit. Updating last seen before discovering a conflict can make a supposedly rejected command partially effective. Decide whether attempted-access telemetry is separate and allowed.

A merge, if offered, should preserve conversation and message relationships and require explicit authority. It should not be hidden inside FirstOrDefault. Once two records exist, choosing one does not explain what happens to the other's history or cookies. The independent capstone later in the course can use this conflict as its starting dataset.

## Independent exercises

Exercise NI06-A: construct anonymous-only, external-only, both-matching-one, and conflicting-two visitor fixtures. Predict the current predicate's candidate set without assuming order means precedence.

Exercise NI06-B: trace null versus empty profile updates, a closed latest conversation, and equal last-message timestamps. State which behaviors are current and which need an explicit product policy.

Exercise NI06-C: design an identity-proof and unchanged-state rejection contract for bootstrap conflict. Separate selection evidence, token-signing evidence, and later visitor endpoint authorization.

Answers appear in [widget workflow solutions](solutions-widget-workflows.md). Use synthetic identities throughout.
