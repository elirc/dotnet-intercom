"""Runs copied actual NetIntercom domain source in a disposable package-free net10 project."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import tempfile

PROJECT = Path(__file__).resolve().parents[3]
SOURCES = [
    "src/NetIntercom.Domain/Common/Entity.cs",
    "src/NetIntercom.Domain/Common/DomainException.cs",
    "src/NetIntercom.Domain/Enums.cs",
    *["src/NetIntercom.Domain/Entities/" + name + ".cs" for name in
      ("Conversation", "Sla", "OfficeHours", "Team", "AgentUser", "Visitor", "Message")],
    "src/NetIntercom.Application/Common/PlaceholderRenderer.cs",
]
PROGRAM = r"""
using NetIntercom.Domain;
using NetIntercom.Domain.Common;
using NetIntercom.Domain.Entities;
using NetIntercom.Application.Common;
using System.Text.Json;
int count = 0;
void Check(bool value, string name) { count++; if (!value) throw new Exception(name); }
void Reject(Action action, string name) { bool threw = false; try { action(); } catch (DomainException) { threw = true; } Check(threw, name); }
var now = new DateTime(2026, 6, 9, 12, 0, 0, DateTimeKind.Utc);
var c = new Conversation { SlaPolicy = new SlaPolicy { FirstResponseMinutes = 5, NextResponseMinutes = 10 } };
c.RecordVisitorMessage(now);
Check(c.State == ConversationState.Open, "visitor open");
Check(c.SlaTimers.Single().BreachAtUtc == now.AddMinutes(5), "first deadline");
c.RecordAgentReply(now.AddMinutes(3));
Check(c.FirstAgentReplyAtUtc == now.AddMinutes(3), "first reply");
Check(c.SlaTimers.Single().Status == SlaTimerStatus.Met, "first met");
c.RecordVisitorMessage(now.AddMinutes(4));
c.RecordVisitorMessage(now.AddMinutes(6));
Check(c.SlaTimers.Count == 2, "no stacking");
Check(c.SlaTimers.Single(x => x.Status == SlaTimerStatus.Running).BreachAtUtc == now.AddMinutes(14), "deadline not extended");
c.Snooze(now.AddMinutes(60), now.AddMinutes(7));
Check(c.State == ConversationState.Snoozed, "snoozed");
Check(c.SlaTimers.Count(x => x.Status == SlaTimerStatus.Running) == 1, "snooze keeps timer");
c.RecordAgentReply(now.AddMinutes(8));
Check(c.State == ConversationState.Open && c.SnoozedUntilUtc == null, "reply reopens");
Check(c.FirstAgentReplyAtUtc == now.AddMinutes(3), "first unchanged");
Check(c.SlaTimers.All(x => x.Status == SlaTimerStatus.Met), "both met");
c.Close(now.AddMinutes(9)); c.Reopen(); c.RecordVisitorMessage(now.AddMinutes(11));
Check(c.SlaTimers.Single(x => x.Status == SlaTimerStatus.Running).Kind == SlaTimerKind.NextResponse, "next after reopen");
Check(c.SlaTimers.Single(x => x.Status == SlaTimerStatus.Running).BreachAtUtc == now.AddMinutes(21), "new deadline");
var before = (c.State, c.SnoozedUntilUtc, c.ClosedAtUtc, c.FirstAgentReplyAtUtc, c.SlaTimers.Count);
Reject(() => c.Snooze(now, now), "equal snooze rejected");
Check(before == (c.State, c.SnoozedUntilUtc, c.ClosedAtUtc, c.FirstAgentReplyAtUtc, c.SlaTimers.Count), "rejected snooze unchanged");
var team = Guid.NewGuid(); c.Assign(Guid.NewGuid(), team); c.Assign(null, null);
Check(c.AssigneeId == null && c.TeamId == team, "null semantics");
c.Close(now.AddMinutes(12));
Check(c.SlaTimers.Count(x => x.Status == SlaTimerStatus.Cancelled) == 1, "close cancels only running");
Reject(() => c.Close(now.AddMinutes(13)), "close twice");
Reject(() => c.Assign(Guid.NewGuid(), null), "assign closed");
var naked = new Conversation(); naked.Close(now); naked.RecordAgentReply(now.AddMinutes(1));
Check(naked.State == ConversationState.Closed, "agent reply does not reopen closed");
naked.RecordVisitorMessage(now.AddMinutes(2));
Check(naked.State == ConversationState.Open && naked.SlaTimers.Count == 0, "visitor reopens without policy");
var timer = SlaTimer.Start(Guid.NewGuid(), SlaTimerKind.FirstResponse, now, TimeSpan.FromMinutes(5));
timer.MarkBreached(now.AddMinutes(6)); var completed = timer.CompletedAtUtc; timer.Cancel(now.AddMinutes(7));
Check(timer.Status == SlaTimerStatus.Breached && timer.CompletedAtUtc == completed, "cancel nonrunning noop");
Reject(() => timer.MarkMet(now.AddMinutes(8)), "met after breach rejected");
var hours = new OfficeHours();
Check(hours.IsOpenAt(now), "empty always open");
hours.Windows.Add(new OfficeHoursWindow { Day = DayOfWeek.Tuesday, Start = new TimeOnly(9,0), End = new TimeOnly(17,0) });
Check(hours.IsOpenAt(now.Date.AddHours(9)), "start inclusive");
Check(!hours.IsOpenAt(now.Date.AddHours(17)), "end exclusive");
hours.TimeZoneId = "Synthetic/NotAZone";
Check(hours.IsOpenAt(now), "unknown zone UTC fallback");
hours.TimeZoneId = "UTC"; hours.Windows.Clear();
hours.Windows.Add(new OfficeHoursWindow { Day = DayOfWeek.Friday, Start = new TimeOnly(22,0), End = new TimeOnly(6,0) });
Check(!hours.IsOpenAt(new DateTime(2026,6,12,23,30,0,DateTimeKind.Utc)), "overnight current characterization");
var values = new Dictionary<string,string?> { ["visitor.name"] = "Pat", ["agent.name"] = "" };
Check(PlaceholderRenderer.Render("Hi {{ visitor.name }}!", values) == "Hi Pat!", "spaced placeholder");
Check(PlaceholderRenderer.Render("I'm {{agent.name}}", values) == "I'm {{agent.name}}", "empty remains");
Check(PlaceholderRenderer.Render("Ref {{visitor-name}}", values) == "Ref {{visitor-name}}", "hyphen not matched");
Check(PlaceholderRenderer.Render("{{visitor.name}}{{visitor.name}}", values) == "PatPat", "repeated tokens");
Console.WriteLine(JsonSerializer.Serialize(new { assertions = count, result = "passed", scope = "copied actual domain and placeholder source; no API or database" }));
"""

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    original = {name: digest(PROJECT / name) for name in SOURCES}
    temp = Path(tempfile.mkdtemp(prefix="netintercom-domain-course-"))
    try:
        for index, name in enumerate(SOURCES):
            shutil.copyfile(PROJECT / name, temp / (str(index) + "-" + Path(name).name))
        (temp / "Lab.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>', encoding="utf-8")
        (temp / "NuGet.Config").write_text('<configuration><packageSources><clear /></packageSources></configuration>', encoding="utf-8")
        (temp / "Program.cs").write_text(PROGRAM, encoding="utf-8")
        result = subprocess.run(["dotnet", "run", "--project", "Lab.csproj", "--verbosity", "quiet"], cwd=temp, text=True, capture_output=True)
        print(result.stdout)
        if result.returncode:
            print(result.stderr)
            raise SystemExit(result.returncode)
        assert all(digest(PROJECT / name) == value for name, value in original.items())
        print(json.dumps({"source_hashes": original, "original_sources_unchanged": True}))
    finally:
        resolved = temp.resolve()
        assert resolved.parent == Path(tempfile.gettempdir()).resolve()
        assert resolved.name.startswith("netintercom-domain-course-")
        shutil.rmtree(resolved)

if __name__ == "__main__":
    main()
