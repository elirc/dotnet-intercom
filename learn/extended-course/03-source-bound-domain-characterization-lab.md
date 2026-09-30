# 03. Source-bound domain characterization lab

The [domain runner](labs/domain_lab.py) copies eleven selected source files into a disposable project and executes their actual C# implementations. It does not replace them with a Python behavioral model. Python orchestrates copying, compilation, hash comparison, and cleanup; the assertions run in C# against the copied source.

This distinction matters because a model can be internally consistent while missing a detail in the real method. The runner catches source behavior such as null assignment semantics, snooze preserving a timer, and the exact placeholder regular expression. It remains narrower than application execution: no EF context, HTTP host, SignalR connection, routing service, or database participates.

## Why the lab uses a disposable project

The application targets .NET 8. The available machine has SDK 10.0.302. Rather than change application configuration or install a dependency, the runner builds a package-free net10 console project in a uniquely named temporary directory. Its NuGet configuration clears package sources, and its project contains no package references.

This demonstrates selected source behavior under the available runtime. It does not prove that the original application restores or builds under its pinned environment. Report the target and scope honestly. A successful console compilation is neither a full solution build nor an execution of the existing xUnit test project.

The copied files include entity dependencies needed to compile Conversation, timers, office hours, and the generated-regex placeholder renderer. No stand-in entity classes are substituted. The original files are hashed before and after execution. Cleanup checks that the resolved temporary path is directly under the operating system temporary directory and uses the expected unique prefix before removing it.

## Predict before executing

Write the expected timeline from chapter one without consulting the runner's assertions. Include timer kinds, deadlines, first-reply timestamp, and the state after snooze and reply. Then add a closed conversation receiving an agent reply directly. The domain method stamps first reply but does not reopen Closed; a visitor message later does reopen it.

For office hours, predict empty windows, exact start, exact end, an unknown timezone, and an overnight Friday window. Empty means open. Start is inclusive and end exclusive. The method catches TimeZoneNotFoundException and falls back to UTC. Its same-day start/end comparison cannot represent a Friday interval that extends through midnight into Saturday.

For placeholders, use visitor.name mapped to Pat and agent.name mapped to an empty string. Spaces inside the braces are accepted. An empty replacement leaves the original token intact. A hyphenated token name is outside the selected character class and remains unchanged. Repeated valid tokens are replaced independently.

These predictions are current behavior. They are not universal product requirements. An overnight-hours repair, empty-name policy, or closed-reply restriction should first be specified as a proposed change, then receive a regression test with a deliberately changed expectation.

## Run the bounded command

From the course directory:

```powershell
python labs/domain_lab.py
```

The runner emits an assertion result and a source-hash record after successful execution. Read both. Compilation success without assertion completion is not a passed lab. Likewise, a runner that exercises copied files without checking original hashes cannot establish preservation as directly as this one.

The test count is thirty-two. It includes ordinary behavior, rejection behavior, unchanged local state, and explicit current-edge characterizations. It does not include semantic mutants. If you create a disposable mutant as an exercise, report its execution separately rather than add imagined detections to the baseline count.

## What the oracles actually inspect

The timeline assertions compare concrete timestamps and statuses. They establish that later visitor messages do not extend a currently running timer and that snooze does not suspend it. The assignment assertion checks both cleared agent and retained team. Rejection checks establish the expected DomainException category and selected unchanged snapshot fields.

The timer cancellation test starts with a breached timer, calls Cancel, and verifies both status and completion timestamp remain unchanged. This is stronger than checking that no exception occurred. A faulty cancellation implementation could quietly overwrite completion time while appearing idempotent to a count-only test.

The office-hours fixture uses a Tuesday UTC instant and an explicit synthetic nonexistent timezone identifier. It avoids depending on a real user's local timezone. The overnight fixture fixes Friday and UTC so its false result follows from the implementation's comparison rather than daylight-saving behavior.

The placeholder assertions compare exact strings, including braces preserved for unknown or empty values. They do not render HTML or exercise a browser. Any claim about escaping untrusted HTML requires inspecting the eventual rendering boundary; the string renderer alone neither proves nor disproves that separate protection.

## Extensions that require a different harness

To establish database concurrency, use separate real contexts and controlled barriers around reads and saves. To establish notification privacy, provide a notifier or hub fixture that records exact groups and payloads. To establish browser reconnect recovery, control connection lifecycle and transcript responses in the widget interaction.

Do not accumulate unrelated mocks into this small domain runner until it appears to test everything. Its strength is that the copied source and assertions have a clear relationship. A larger service harness can be useful, but it should name its dependencies and evidence limits just as precisely.

A future compiler change or new runtime dependency can make copied source fail to build. That does not automatically mean the business invariant regressed. Separate harness compatibility from assertion failure. Record whether the file compiled, whether the target method was reached, and which expected result differed.

## Independent exercises

Exercise NI03-A: select three assertions that characterize potentially surprising current behavior. For each, write a distinct proposed requirement and explain which expected value would change after an approved implementation.

Exercise NI03-B: design a disposable mutation that changes cancellation of a nonrunning timer and one that changes the office-hours end comparison. Predict the exact failing oracle and state why a compile failure would not count as detection.

Exercise NI03-C: map domain, service, persistence, hub, and browser evidence for one visitor-send scenario. Explain what this runner establishes and identify the smallest next fixture for each unsupported boundary.

Use [foundation solutions](solutions-foundation.md) after completing the prediction sheet. Leave the application source unchanged throughout this exercise.
