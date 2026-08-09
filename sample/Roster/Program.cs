// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Text.Json;
using Rulealize;

// A week's shift roster, filled entirely through the runtime. This sample is here because
// the other three are board games, and three board games is not evidence that the DSL is
// general — it is three points from one corner of the space.
//
// Nothing about this one is a game. There is no turn, so ValidInput.Actor is null; there is
// no opponent and no winner, and the outcome is whether the constraints came out satisfied
// ("complete") or painted into a corner ("stuck"). The grid plugin is loaded, because the
// host loads all twelve, but roster.json never says "grid." once.
//
// The question a caller asks of it is GetValidInputs, read as "who may still be put on
// what". That is the same question a scheduling screen asks and the same one a solver
// branches on, and the runtime answers it without knowing that any of this is about people.
//
//   dotnet run --project sample/Roster                                fill it by hand
//   dotnet run --project sample/Roster -- --solve                     let it search
//   dotnet run --project sample/Roster -- --state other-week --solve  a different week
//
// The last one is the point of the rule set. Different people, a different number of them,
// three days instead of five — and not a character of roster.json changes, because none of
// it was ever in roster.json. The rule set is the domain; a week is a state document.

const int Limit = 200;

bool solving = args.Contains("--solve", StringComparer.OrdinalIgnoreCase);
string? week = Named(args, "--state");

// ── 1. Build the vocabulary ────────────────────────────────────────────────────────
// A runtime starts with no operations at all. Everything a rule set is allowed to say
// comes from a plugin, and plugins are found by scanning a folder for assemblies.
string pluginFolder = Path.Combine(AppContext.BaseDirectory, "plugin");
RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(pluginFolder);

Console.WriteLine($"Loaded {runtime.Plugins.Length} plugins:");
foreach (var manifest in runtime.Plugins.OrderBy(p => p.Namespace, StringComparer.Ordinal))
{
    string shorthand = manifest.ReservedPrefix is char prefix ? $"  shorthand '{prefix}'" : string.Empty;
    Console.WriteLine($"  {manifest.Namespace,-7} {manifest.Id} {manifest.Version}{shorthand}");
}

// ── 2. Compile the rule set ───────────────────────────────────────────────────────
// Everything decidable from the document is decided here. What is not decidable here is
// who the people are: that arrives with the state.
RuleContext roster = runtime.CreateContext(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuleSet", "roster.json")));

Console.WriteLine($"\nRule set: {roster.RuleSet}   inputs: {string.Join(", ", roster.Inputs)}");

// ── 3. Fill it in ─────────────────────────────────────────────────────────────────
// The initial state is one week; --state names another. Both are instances of the same
// rule set, and the search below cannot tell which one it is working on.
string state = week is null ? roster.InitialState : ReadWeek(week);

Console.WriteLine(week is null
    ? "Week: the one in the rule set's state.initial"
    : $"Week: {week}");

if (solving)
{
    return Search(state);
}

while (true)
{
    ValidInputSet placements = roster.GetValidInputs(state, Limit);

    // Whether the roster is finished is a separate question from what may still be done to
    // it. GetValidInputs does not consult the terminal section, which is why a release is
    // still offered from a stuck roster — and that is what makes this searchable rather
    // than a one-way filling.
    TerminalStatus status = roster.GetTerminalStatus(state);

    Render(state, placements, status);

    string? command = Ask();
    if (command is null)
    {
        Console.WriteLine("\nStopped.");
        break;
    }

    if (command.Equals("solve", StringComparison.OrdinalIgnoreCase))
    {
        Search(state);
        break;
    }

    if (Document(command) is not string request)
    {
        Console.WriteLine(" say 'who shift', or 'release shift'.");
        continue;
    }

    try
    {
        // Whatever the user typed goes in as an input document, not as a ValidInput picked
        // out of the set above. Both routes are checked the same way: the arguments are
        // matched against the parameter domains, then the guard runs, and either refusal
        // arrives as IllegalInputException — a caller cannot tell which rule turned it
        // away, which is the point of the domain being part of the rules.
        TransitionResult result = roster.ApplyToState(request, state);
        state = result.State;
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

return 0;

// ── Searching ─────────────────────────────────────────────────────────────────────
// What a rule set like this is for. The searcher knows nothing about seniority or rest
// days: it asks what may be assigned, tries one, and backtracks when the answer comes back
// empty. Everything that makes it terminate — a finite domain, a guard that tightens as the
// roster fills — is in the document.

int Search(string from)
{
    int shifts = ShiftCount(from);
    int explored = 0;

    Stopwatch clock = Stopwatch.StartNew();
    string? solved = Fill(from, 0);
    clock.Stop();

    if (solved is null)
    {
        Console.WriteLine($"\nNo complete roster from here. {explored:N0} states explored in {clock.ElapsedMilliseconds} ms.");
        return 1;
    }

    Console.WriteLine($"\nComplete. {explored:N0} states explored in {clock.ElapsedMilliseconds} ms.");
    Render(solved, roster.GetValidInputs(solved, Limit), roster.GetTerminalStatus(solved));
    return 0;

    // Only assignments are followed. A release is a legal input and would make the space
    // cyclic, which is a real property of this rule set rather than an oversight: the DSL
    // has no notion of progress, so a searcher decides for itself what counts as forward.
    string? Fill(string current, int depth)
    {
        explored++;

        if (roster.GetTerminalStatus(current) is { IsTerminal: true, Result: "complete" })
        {
            return current;
        }

        if (depth >= shifts)
        {
            return null;
        }

        foreach (ValidInput placement in Assignments(current))
        {
            string next = roster.ApplyToState(placement.ToInputDocument(roster.RuleSet), current).State;

            if (Fill(next, depth + 1) is string solvedHere)
            {
                return solvedHere;
            }
        }

        return null;
    }
}

IEnumerable<ValidInput> Assignments(string current) =>
    roster.GetValidInputs(current, Limit).Where(static play => play.Input == "assign");

// ── Documents ─────────────────────────────────────────────────────────────────────

/// <summary>Turns a typed line into an input document.</summary>
/// <remarks>
/// Deliberately not validated here. An unknown name, a shift someone may not work, a shift
/// already covered — all of it is the rule set's business, and this sample would be lying
/// about where the rules live if it checked first.
/// </remarks>
string? Document(string command)
{
    string[] words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    (string Input, string Args)? request = words switch
    {
        ["release", string shift] => ("release", $$"""
            "shift": "{{shift}}"
            """),
        ["assign", string who, string shift] => ("assign", $$"""
            "who": "{{who}}", "shift": "{{shift}}"
            """),

        // The verb is optional for the common case, because a roster screen is mostly
        // assignments and "ann mon-am" is how a person would say it.
        [string who, string shift] => ("assign", $$"""
            "who": "{{who}}", "shift": "{{shift}}"
            """),
        _ => null,
    };

    return request is null
        ? null
        : $$"""
            { "$schema": "rulealize/input/v1", "ruleSet": "{{roster.RuleSet}}",
              "input": "{{request.Value.Input}}", "args": { {{request.Value.Args}} } }
            """;
}

static string ReadWeek(string name)
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

// ── Presentation ──────────────────────────────────────────────────────────────────
// Everything below is about showing a roster to a person. The runtime is done.

void Render(string stateDocument, ValidInputSet placements, TerminalStatus status)
{
    using JsonDocument document = JsonDocument.Parse(stateDocument);
    JsonElement data = document.RootElement.GetProperty("data");

    Dictionary<string, string> covered = Covered(data);

    Console.WriteLine("\n shift    wants     covered by   still possible");
    foreach (JsonElement shift in data.GetProperty("shifts").EnumerateArray())
    {
        string id = shift.GetProperty("id").GetString()!;
        bool taken = covered.TryGetValue(id, out string? who);

        // The last column is the whole reason a scheduling screen would hold a rule set: it
        // is not a list this program worked out, it is the answer to "what is legal". An
        // empty one is a hole nobody left may fill, and that is what "stuck" is made of.
        string[] candidates =
        [
            .. placements
                .Where(play => play.Input == "assign" && play.Arguments["shift"] == id)
                .Select(static play => play.Arguments["who"])
                .Order(StringComparer.Ordinal),
        ];

        Console.WriteLine($" {id,-8} {(shift.GetProperty("senior").GetBoolean() ? "senior" : "anyone"),-9} "
            + $"{(taken ? who : "-"),-12} "
            + $"{(taken ? string.Empty : candidates.Length == 0 ? "nobody" : string.Join(", ", candidates))}");
    }

    Console.WriteLine("\n staff    grade     worked");
    foreach (JsonElement person in data.GetProperty("staff").EnumerateArray())
    {
        string name = person.GetProperty("name").GetString()!;
        int worked = covered.Count(entry => entry.Value == name);

        Console.WriteLine($" {name,-8} {(person.GetProperty("senior").GetBoolean() ? "senior" : "junior"),-9} "
            + $"{worked}/{person.GetProperty("capacity").GetInt32()}");
    }

    // The trail is a list in the state with a maxLength of five, appended and trimmed in one
    // expression by the rule set. Nothing here trims it.
    JsonElement[] log = [.. data.GetProperty("log").EnumerateArray()];
    if (log.Length > 0)
    {
        Console.WriteLine($"\n last {log.Length}: " + string.Join(", ", log.Select(static entry =>
            $"{entry.GetProperty("action").GetString()} {entry.GetProperty("who").GetString()}"
                + $" {entry.GetProperty("shift").GetString()}")));
    }

    Console.WriteLine(status.IsTerminal
        ? $"\n {status.Result}"
        : $"\n {placements.Count(static play => play.Input == "assign")} assignments and "
            + $"{placements.Count(static play => play.Input == "release")} releases available, "
            + $"{placements.Evaluated} guards evaluated");
}

static Dictionary<string, string> Covered(JsonElement data) =>
    data.GetProperty("assigned").EnumerateArray().ToDictionary(
        static entry => entry.GetProperty("shift").GetString()!,
        static entry => entry.GetProperty("who").GetString()!,
        StringComparer.Ordinal);

/// <summary>How many shifts this week has, which is how deep a complete roster can be.</summary>
static int ShiftCount(string stateDocument)
{
    using JsonDocument document = JsonDocument.Parse(stateDocument);
    return document.RootElement.GetProperty("data").GetProperty("shifts").GetArrayLength();
}

static string? Ask()
{
    Console.Write("\n command ('ann mon-am', 'release mon-am', 'solve', or 'q' to quit) > ");
    string? line = Console.ReadLine()?.Trim();

    return line is null || line.Equals("q", StringComparison.OrdinalIgnoreCase) ? null : line;
}
