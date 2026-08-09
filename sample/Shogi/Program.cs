// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize;

// A game of shogi, played entirely through the runtime. Nothing in this file knows the
// rules: it loads plugins, compiles a document, asks what is legal, and applies what the
// user picked. Promotion, drops, the two pawn restrictions and the rule that a pawn may not
// be dropped to give mate are all in shogi.json.
//
// What this sample shows that Chess does not is a rule set with two inputs of different
// shapes, and a host that has to keep them apart:
//
//   move  one parameter "m" carrying (from, to, promote) as one token, because the
//         destination depends on the origin
//   drop  two parameters, "piece" and "to", whose domains genuinely do not depend on one
//         another and so are left separate
//
// ValidInput.Arguments has only the keys its own input declares, so anything asking about
// "m" has to filter on Input first. That is a small edge and this file meets it twice.
//
//   dotnet run --project sample/Shogi                play it
//   dotnet run --project sample/Shogi -- --auto      let it play itself
//
// Coordinates are the grid plugin's a1..i9 with black's back rank at 1, which is the
// standard position mirrored left to right — the same game, written in the notation the
// vocabulary has.

const int Limit = 4000;
const int AutoPlyLimit = 200;

bool automatic = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);

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
// Everything decidable from the document is decided here. If this call returns, the
// document is coherent: no unknown operations, no unbound names, no cyclic definitions,
// no state path that the schema does not have.
RuleContext shogi = runtime.CreateContext(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuleSet", "shogi.json")));

Console.WriteLine($"\nRule set: {shogi.RuleSet}   inputs: {string.Join(", ", shogi.Inputs)}");

// ── 3. Play ───────────────────────────────────────────────────────────────────────
// A context holds no position. The state travels in and out as a document, so a game can
// be suspended, stored, and resumed by keeping nothing but this string.
string state = shogi.InitialState;
int ply = 0;

while (true)
{
    // Moves and drops come back in one set, already sifted by their guards. The drops are
    // the reason the limit matters here: seven kinds crossed with the empty squares is a
    // few hundred candidates before the guard sees any of them.
    ValidInputSet plays = shogi.GetValidInputs(state, Limit);

    // Whether a position is final is a separate question from what is legal in it. Shogi
    // has no stalemate and no draw by material, so this is mate — but it is the rule set
    // that says so, and asking is the host's job.
    TerminalStatus status = shogi.GetTerminalStatus(state);

    Render(state, plays, status);

    if (status.IsTerminal)
    {
        Console.WriteLine($"\nGame over after {ply} plies. Result: {status.Result}.");
        break;
    }

    if (plays.Count == 0)
    {
        Console.WriteLine("\nNo legal play, and the rule set does not consider this final. Stopping.");
        break;
    }

    if (automatic && ply >= AutoPlyLimit)
    {
        Console.WriteLine($"\nStopped after {AutoPlyLimit} plies without a finish.");
        break;
    }

    ValidInput? chosen = automatic ? plays[0] : Ask(plays);
    if (chosen is null)
    {
        Console.WriteLine("\nStopped.");
        break;
    }

    Console.WriteLine($"\n{chosen.Actor} plays {Describe(chosen)}");

    // A play that came out of GetValidInputs goes straight back in. Applying is checked
    // against the domain as well as the guard, which is what stops a document from
    // describing a move this rule set never generated.
    TransitionResult result = shogi.ApplyToState(
        chosen.ToInputDocument(shogi.RuleSet),
        state);

    state = result.State;
    ply++;

    if (automatic)
    {
        continue;
    }

    Console.WriteLine("(press enter)");
    if (Console.ReadLine() is null)
    {
        break;
    }
}

return 0;

// ── Presentation ──────────────────────────────────────────────────────────────────
// Everything below is about showing a board to a person. The runtime is done.

void Render(string stateDocument, ValidInputSet plays, TerminalStatus status)
{
    using JsonDocument document = JsonDocument.Parse(stateDocument);
    JsonElement data = document.RootElement.GetProperty("data");
    JsonElement board = data.GetProperty("board");
    JsonElement hand = data.GetProperty("hand");

    Console.WriteLine();
    Console.WriteLine($" white in hand: {Held(hand, "white")}");
    Console.WriteLine("     a   b   c   d   e   f   g   h   i");
    for (int rank = 9; rank >= 1; rank--)
    {
        Console.Write($" {rank} ");
        for (char file = 'a'; file <= 'i'; file++)
        {
            // A square with no piece is left out of the document entirely — the board is
            // serialized sparsely, which is the grid plugin's decision, not the core's.
            string glyph = board.TryGetProperty($"{file}{rank}", out JsonElement cell)
                ? cell.GetString() ?? "."
                : ".";

            Console.Write($" {glyph,-2} ");
        }

        Console.WriteLine($" {rank}");
    }

    Console.WriteLine("     a   b   c   d   e   f   g   h   i");
    Console.WriteLine($" black in hand: {Held(hand, "black")}");

    Console.WriteLine($"\n turn: {data.GetProperty("turn").GetString()}"
        + "    (upper case is black, moving up the board; a leading + is promoted)");

    Console.WriteLine(status.IsTerminal
        ? $" final position — {status.Result} wins"
        : $" {plays.Count} legal, {plays.Evaluated} guards evaluated"
            + $"{(plays.Truncated ? $" (stopped at the limit of {Limit})" : string.Empty)}");
}

/// <summary>Renders one side's hand, which is a record of counters rather than a list.</summary>
static string Held(JsonElement hand, string colour)
{
    JsonElement side = hand.GetProperty(colour);

    string[] holdings =
    [
        .. new[] { "P", "L", "N", "S", "G", "B", "R" }
            .Where(kind => side.GetProperty(kind).GetInt32() > 0)
            .Select(kind => side.GetProperty(kind).GetInt32() is 1
                ? kind
                : $"{kind}x{side.GetProperty(kind).GetInt32()}"),
    ];

    return holdings.Length == 0 ? "-" : string.Join(" ", holdings);
}

static string Describe(ValidInput play)
{
    if (play.Input == "drop")
    {
        return $"{play.Arguments["piece"]}*{play.Arguments["to"]}";
    }

    string[] parts = play.Arguments["m"].Split('|');
    return parts[2] == "+" ? $"{parts[0]}{parts[1]}+" : $"{parts[0]}{parts[1]}";
}

ValidInput? Ask(ValidInputSet plays)
{
    while (true)
    {
        Console.Write("\n play (e3e4, e3e4+, P*e5, 'moves', or 'q' to quit) > ");
        string? line = Console.ReadLine()?.Trim();

        if (line is null || line.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (line.Equals("moves", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($" {string.Join(", ", plays.Select(Describe).Order(StringComparer.Ordinal))}");
            continue;
        }

        IReadOnlyList<ValidInput> matched = Match(plays, line);

        if (matched.Count == 1)
        {
            return matched[0];
        }

        Console.WriteLine(matched.Count == 0
            ? " not a legal play here."
            : $" which one? {string.Join(", ", matched.Select(Describe))}");
    }
}

/// <summary>Finds the plays a typed line could mean.</summary>
/// <remarks>
/// Two inputs of different shapes meet here. A drop is matched on its two arguments; a move
/// is one token that has to be taken apart. Neither branch decides anything about shogi —
/// every candidate came out of GetValidInputs already.
/// </remarks>
static IReadOnlyList<ValidInput> Match(ValidInputSet plays, string typed)
{
    string text = typed.Replace(" ", string.Empty);

    // A drop, written the way shogi notation writes one.
    if (text.Length == 4 && text[1] == '*')
    {
        string piece = char.ToUpperInvariant(text[0]).ToString();
        string square = text[2..].ToLowerInvariant();

        return
        [
            .. plays.Where(play => play.Input == "drop"
                && play.Arguments["piece"] == piece
                && play.Arguments["to"] == square),
        ];
    }

    text = text.ToLowerInvariant();

    // The move token itself, for anyone who would rather say exactly which move they mean.
    if (text.Contains('|'))
    {
        return [.. plays.Where(play => play.Input == "move" && play.Arguments["m"] == text)];
    }

    if (text.Length is 4 or 5)
    {
        string from = text[..2];
        string to = text[2..4];
        string? promote = text.Length == 5 ? text[4..] : null;

        return [.. plays.Where(play => play.Input == "move" && Says(play, from, to, promote))];
    }

    return [];
}

static bool Says(ValidInput move, string from, string to, string? promote)
{
    string[] parts = move.Arguments["m"].Split('|');
    return parts[0] == from && parts[1] == to && (promote is null || parts[2] == promote);
}
