// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize;
using Rulealize.Sample.Deploy;

// A deployment pipeline: three services, three stages, and the question "what may I ship
// right now" answered by GetValidInputs. That question is the same one the board games ask
// about legal moves, and here it is the activation state of the buttons on a release screen.
//
// This sample is here for what the other four cannot show. Every one of them loads its
// entire vocabulary by scanning a folder for plugin assemblies, which is the right picture
// of a deployment and a misleading picture of a library. A project using Rulealize for its
// own business rules will have operations that are worth writing and not worth publishing:
// this one has four, and they arrive by implementing IRulealizePlugin in this project and
// handing an instance to AddPlugin.
//
// What that buys, and what a folder-scanned plugin cannot do, is the constructor. A plugin
// discovered on disk is built through its parameterless constructor and has nowhere to
// receive anything; DeployVocabulary takes the organisation's freeze calendar and ownership
// map, which are external, sizeable, versioned on their own schedule, and no business of
// any single deployment's state.
//
// What it does not buy is a licence to reach outside. Every operation is a pure function of
// its arguments and that immutable snapshot — today's date is a state field passed in as an
// argument, not a clock read — because GetValidInputs evaluates a guard once per candidate
// in a parameter's domain and needs the same answer every time.
//
//   dotnet run --project sample/Deploy                          drive it by hand
//   dotnet run --project sample/Deploy -- --auto                run it to a conclusion
//   dotnet run --project sample/Deploy -- --state friday        two days later
//   dotnet run --project sample/Deploy -- --policy lockdown     a stricter organisation
//
// The last two are the point. Neither changes a character of deploy.json, and both change
// what is legal — one through a field in the state, the other through a table the rule set
// has never seen.

const int Limit = 300;

bool auto = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
string policyName = Named(args, "--policy") ?? "acme";
string? day = Named(args, "--state");

// ── 1. Read the tables ─────────────────────────────────────────────────────────────
// Before the runtime exists, because the vocabulary is built from them.
DeployPolicy policy = DeployPolicy.Read(
    Path.Combine(AppContext.BaseDirectory, "Policy", $"{policyName}.json"));

// ── 2. Build the vocabulary ────────────────────────────────────────────────────────
// Twelve plugins found by scanning a folder, then one that was never on disk. Both routes
// end in the same table, and the rule set cannot tell which name came from where.
RuleRuntime runtime = new RuleRuntime()
    .LoadPluginsFrom(Path.Combine(AppContext.BaseDirectory, "plugin"))
    .AddPlugin(new DeployVocabulary(policy));

Console.WriteLine($"Loaded {runtime.Plugins.Length} vocabularies (policy: {policyName}):");
foreach (var manifest in runtime.Plugins.OrderBy(p => p.Namespace, StringComparer.Ordinal))
{
    // The manifest is the only thing that distinguishes them, and it does not record where
    // the plugin came from. This line reads the identifier instead, which is what `requires`
    // reads too.
    string origin = manifest.Id.StartsWith("Rulealize.Plugin.", StringComparison.Ordinal)
        ? "scanned"
        : "in process";

    Console.WriteLine($"  {manifest.Namespace,-7} {manifest.Id,-30} {manifest.Version}  {origin}");
}

// ── 3. Compile the rule set ────────────────────────────────────────────────────────
// `requires` names Acme.Deploy.Rules alongside the ten standard vocabularies it draws on.
// Dropping the AddPlugin call above makes this line fail, naming it — a private vocabulary
// is still a declared one.
RuleContext pipeline = runtime.CreateContext(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuleSet", "deploy.json")));

Console.WriteLine($"\nRule set: {pipeline.RuleSet}   inputs: {string.Join(", ", pipeline.Inputs)}");

string state = day is null ? pipeline.InitialState : ReadDay(day);
Console.WriteLine(day is null ? "Day: the one in the rule set's state.initial" : $"Day: {day}");

if (auto)
{
    return Run(state);
}

while (true)
{
    ValidInputSet legal = pipeline.GetValidInputs(state, Limit);
    Render(state, legal, pipeline.GetTerminalStatus(state));

    string? command = Ask();
    if (command is null)
    {
        Console.WriteLine("\nStopped.");
        return 0;
    }

    if (command.Equals("auto", StringComparison.OrdinalIgnoreCase))
    {
        return Run(state);
    }

    if (Document(command) is not string request)
    {
        Console.WriteLine(" say 'deploy <service> <version>', 'promote <service> <stage>',"
            + " or 'approve <service> <stage> <who>'.");
        continue;
    }

    try
    {
        state = pipeline.ApplyToState(request, state).State;
    }
    catch (IllegalInputException refused)
    {
        Console.WriteLine($" refused: {refused.Message}");
    }
    catch (RuleDocumentException malformed)
    {
        Console.WriteLine($" not a command this rule set has: {malformed.Message}");
    }
}

// ── Driving it ─────────────────────────────────────────────────────────────────────
// Not a search: every legal input here moves the pipeline forward or is harmless, so the
// train comes in by preferring promotions to deployments to signatures and never backing
// out. What it can do is stop — a freeze makes `blocked` reachable with everything staged
// and signed off, which is the case worth seeing.

int Run(string from)
{
    string current = from;

    for (int step = 0; step < 100; step++)
    {
        TerminalStatus status = pipeline.GetTerminalStatus(current);
        if (status.IsTerminal)
        {
            Render(current, pipeline.GetValidInputs(current, Limit), status);
            return status.Result == "shipped" ? 0 : 1;
        }

        ValidInputSet legal = pipeline.GetValidInputs(current, Limit);
        ValidInput? next = Prefer(legal, "promote") ?? Prefer(legal, "deploy") ?? Prefer(legal, "approve");
        if (next is null)
        {
            break;
        }

        Console.WriteLine($"  {Describe(next)}");
        current = pipeline.ApplyToState(next.ToInputDocument(pipeline.RuleSet), current).State;
    }

    Render(current, pipeline.GetValidInputs(current, Limit), pipeline.GetTerminalStatus(current));
    return 1;
}

static ValidInput? Prefer(ValidInputSet legal, string input) =>
    legal.FirstOrDefault(candidate => candidate.Input == input);

/// <summary>Writes a legal input the way the command line accepts it.</summary>
static string Describe(ValidInput play) => play.Input switch
{
    "deploy" => $"deploy {play.Arguments["service"]} {play.Arguments["version"]}",
    "promote" => $"promote {play.Arguments["service"]} {play.Arguments["to"]}",
    _ => $"approve {play.Arguments["service"]} {play.Arguments["stage"]} {play.Arguments["by"]}",
};

// ── Documents ──────────────────────────────────────────────────────────────────────

/// <summary>Turns a typed line into an input document.</summary>
/// <remarks>
/// Not validated here, deliberately. A version that is not an upgrade, an approver who does
/// not own the service, a promotion into a frozen stage — all of it is the rule set's
/// business, and half of it is a vocabulary this function could not consult if it wanted to.
/// </remarks>
string? Document(string command)
{
    string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    (string Input, string Args)? request = words switch
    {
        ["deploy", string service, string version] => ("deploy", $$"""
            "service": "{{service}}", "version": "{{version}}"
            """),
        ["promote", string service, string stage] => ("promote", $$"""
            "service": "{{service}}", "to": "{{stage}}"
            """),
        ["approve", string service, string stage, string who] => ("approve", $$"""
            "service": "{{service}}", "stage": "{{stage}}", "by": "{{who}}"
            """),
        _ => null,
    };

    return request is null
        ? null
        : $$"""
            { "$schema": "rulealize/input/v1", "ruleSet": "{{pipeline.RuleSet}}",
              "input": "{{request.Value.Input}}", "args": { {{request.Value.Args}} } }
            """;
}

static string ReadDay(string name)
{
    string path = File.Exists(name)
        ? name
        : Path.Combine(AppContext.BaseDirectory, "State", Path.HasExtension(name) ? name : $"{name}.json");

    return File.ReadAllText(path);
}

static string? Named(string[] arguments, string flag)
{
    int at = Array.FindIndex(arguments, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
    return at >= 0 && at + 1 < arguments.Length ? arguments[at + 1] : null;
}

// ── Presentation ───────────────────────────────────────────────────────────────────
// Everything below is about showing a pipeline to a person. The runtime is done.

void Render(string stateDocument, ValidInputSet legal, TerminalStatus status)
{
    // The same options the runtime reads documents with. A state document a person
    // maintains is allowed comments, and State/friday.json uses them — a host that parses
    // one more strictly than the runtime does will accept a file and then fail to display
    // it.
    JsonDocumentOptions options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    using JsonDocument document = JsonDocument.Parse(stateDocument, options);
    JsonElement data = document.RootElement.GetProperty("data");

    string[] stages = [.. data.GetProperty("stages").EnumerateArray()
        .OrderBy(static stage => stage.GetProperty("rank").GetInt32())
        .Select(static stage => stage.GetProperty("id").GetString()!)];

    string today = data.GetProperty("today").GetString()!;
    string target = data.GetProperty("target").GetString()!;

    Console.WriteLine($"\n {today}   target {target}");
    Console.WriteLine($" {"service",-9}{string.Join(string.Empty, stages.Select(static s => $"{s,-14}"))}what may ship");

    foreach (JsonElement service in data.GetProperty("services").EnumerateArray())
    {
        string id = service.GetString()!;

        // The last column is the whole reason a release screen would hold a rule set. It is
        // not a list this program worked out; it is the answer to "what is legal", and an
        // empty one is a service nothing can be done to right now.
        string[] moves =
        [
            .. legal.Where(play => play.Arguments.TryGetValue("service", out string? on) && on == id)
                .Select(static play => play.Input switch
                {
                    "deploy" => $"deploy {play.Arguments["version"]}",
                    "promote" => $"→{play.Arguments["to"]}",
                    _ => $"sign {play.Arguments["stage"]}/{play.Arguments["by"]}",
                })
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

        Console.WriteLine($" {id,-9}"
            + string.Join(string.Empty, stages.Select(stage => $"{VersionOn(data, id, stage) ?? "-",-14}"))
            + (moves.Length == 0 ? "nothing" : string.Join(", ", moves)));
    }

    // Why a freeze bit, when it did. The rule set answers only whether something is legal;
    // the reason lives in the same table the vocabulary reads, and the host is free to ask
    // it directly because the host is what owns it.
    if (policy.WhyFrozen(DeployPolicy.Day(today)) is string reason)
    {
        Console.WriteLine($"\n frozen: {reason}");
    }

    JsonElement[] log = [.. data.GetProperty("log").EnumerateArray()];
    if (log.Length > 0)
    {
        Console.WriteLine($" last {log.Length}: " + string.Join(", ", log.Select(static entry =>
            $"{entry.GetProperty("action").GetString()} {entry.GetProperty("service").GetString()}"
                + $" {entry.GetProperty("version").GetString()}@{entry.GetProperty("stage").GetString()}"
                + (entry.GetProperty("by").GetString() is { Length: > 0 } who ? $" by {who}" : string.Empty))));
    }

    Console.WriteLine(status.IsTerminal
        ? $"\n {status.Result}"
        : $"\n {legal.Count} legal of {legal.Evaluated} candidates evaluated");
}

static string? VersionOn(JsonElement data, string service, string stage) =>
    data.GetProperty("deployed").EnumerateArray()
        .Where(entry => entry.GetProperty("service").GetString() == service
            && entry.GetProperty("stage").GetString() == stage)
        .Select(static entry => entry.GetProperty("version").GetString())
        .FirstOrDefault();

static string? Ask()
{
    Console.Write("\n command ('promote search prod', 'approve billing dev ann', 'auto', 'q') > ");
    string? line = Console.ReadLine()?.Trim();

    return line is null || line.Equals("q", StringComparison.OrdinalIgnoreCase) ? null : line;
}
