// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using System.Text.Json;
using Rulealize;

// A game of chess, played entirely through the runtime. Nothing in this file knows the
// rules: it loads plugins, compiles a document, asks what is legal, and applies what the
// user picked. Castling, promotion, capture in passing and the whole question of whether a
// king is left in check are in chess.json, not here.
//
// What this sample shows that Reversi does not is the shape of a move. A chess move is one
// parameter holding a tuple written "from|to|tag", because the destination depends on the
// origin and separate parameters would make every position a 64 x 64 candidate space. So a
// host has to read that text form back — Match below is the whole of it, and it is the
// price of the domain having been narrowed from 4,096 candidates to about twenty.
//
//   dotnet run --project sample/Chess                 play it
//   dotnet run --project sample/Chess -- --auto       let it play itself
//   dotnet run --project sample/Chess -- --perft 3    count the legal move tree

const int Limit = 1000;
const int AutoPlyLimit = 300;

bool automatic = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
int? perftDepth = PerftDepth(args);

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
RuleContext chess = runtime.CreateContext(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuleSet", "chess.json")));

Console.WriteLine($"\nRule set: {chess.RuleSet}   inputs: {string.Join(", ", chess.Inputs)}");

if (perftDepth is int depth)
{
    return Perft(depth);
}

// ── 3. Play ───────────────────────────────────────────────────────────────────────
// A context holds no position. The state travels in and out as a document, so a game can
// be suspended, stored, and resumed by keeping nothing but this string.
string state = chess.InitialState;
int ply = 0;

while (true)
{
    // GetValidInputs sifts the candidates with the input's guard. Here the candidates are
    // already the pseudo-legal moves — the domain computed them — so the guard only has to
    // ask whether the move leaves its own king attacked. Evaluated is how many times it was
    // asked, and in chess that number is the interesting one: twenty in the opening rather
    // than the 4,096 a from/to pair would have produced.
    ValidInputSet moves = chess.GetValidInputs(state, Limit);

    // Whether a position is final is a separate question from what is legal in it.
    // GetValidInputs does not consult the terminal section, so checkmate and stalemate are
    // asked about here rather than inferred from an empty move list.
    TerminalStatus status = chess.GetTerminalStatus(state);

    Render(state, moves, status);

    if (status.IsTerminal)
    {
        Console.WriteLine($"\nGame over after {ply} plies. Result: {status.Result}.");
        break;
    }

    if (automatic && ply >= AutoPlyLimit)
    {
        Console.WriteLine($"\nStopped after {AutoPlyLimit} plies without a finish.");
        break;
    }

    ValidInput? chosen = automatic ? moves[0] : Ask(moves);
    if (chosen is null)
    {
        Console.WriteLine("\nStopped.");
        break;
    }

    Console.WriteLine($"\n{chosen.Actor} plays {Describe(chosen)}");

    // A move that came out of GetValidInputs goes straight back in. That round trip is why
    // arguments are written in their own JSON form and why an opaque value such as a
    // coordinate has to have a text form.
    //
    // Applying is checked against the domain as well as the guard: a rule set that puts the
    // movement rules in the domain — as this one does — would otherwise have no say over a
    // move that arrived in a document rather than out of GetValidInputs.
    TransitionResult result = chess.ApplyToState(
        chosen.ToInputDocument(chess.RuleSet),
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

// ── Counting the move tree ────────────────────────────────────────────────────────
// perft is the standard way to say that a chess implementation is right: the number of
// leaves of the legal move tree is published, so agreeing with it is not self-consistency.
// It is also the honest measure of what interpreting the DSL costs.

int Perft(int target)
{
    long[] published = [20, 400, 8_902, 197_281];

    Console.WriteLine($"\nperft({target}) from the initial position.");
    if (target >= 4)
    {
        Console.WriteLine(" (depth 4 is about two hundred thousand positions and takes a while)");
    }

    Stopwatch clock = Stopwatch.StartNew();
    long leaves = Count(chess.InitialState, target);
    clock.Stop();

    string expected = target >= 1 && target <= published.Length
        ? $"   expected {published[target - 1]:N0}   {(leaves == published[target - 1] ? "agrees" : "DISAGREES")}"
        : string.Empty;

    Console.WriteLine($"  {leaves:N0} leaves in {clock.Elapsed.TotalSeconds:F1}s{expected}");
    return 0;

    long Count(string position, int remaining)
    {
        ValidInputSet moves = chess.GetValidInputs(position, Limit);
        if (remaining <= 1)
        {
            return moves.Count;
        }

        long total = 0;
        foreach (ValidInput move in moves)
        {
            total += Count(chess.ApplyToState(move.ToInputDocument(chess.RuleSet), position).State, remaining - 1);
        }

        return total;
    }
}

static int? PerftDepth(string[] arguments)
{
    int flag = Array.FindIndex(arguments, a => a.Equals("--perft", StringComparison.OrdinalIgnoreCase));
    if (flag < 0)
    {
        return null;
    }

    return flag + 1 < arguments.Length && int.TryParse(arguments[flag + 1], out int depth) ? depth : 3;
}

// ── Presentation ──────────────────────────────────────────────────────────────────
// Everything below is about showing a board to a person. The runtime is done.

void Render(string stateDocument, ValidInputSet moves, TerminalStatus status)
{
    using JsonDocument document = JsonDocument.Parse(stateDocument);
    JsonElement data = document.RootElement.GetProperty("data");
    JsonElement board = data.GetProperty("board");

    Console.WriteLine();
    Console.WriteLine("    a b c d e f g h");
    for (int rank = 8; rank >= 1; rank--)
    {
        Console.Write($" {rank}  ");
        for (char file = 'a'; file <= 'h'; file++)
        {
            // A square with no piece is left out of the document entirely — the board is
            // serialized sparsely, which is the grid plugin's decision, not the core's.
            string glyph = board.TryGetProperty($"{file}{rank}", out JsonElement cell)
                ? cell.GetString() ?? "."
                : ".";

            Console.Write($"{glyph} ");
        }

        Console.WriteLine($" {rank}");
    }

    Console.WriteLine("    a b c d e f g h");

    string rights = string.Concat(
        Right(data, "wk", 'K'), Right(data, "wq", 'Q'), Right(data, "bk", 'k'), Right(data, "bq", 'q'));

    string passing = data.GetProperty("ep").ValueKind == JsonValueKind.Null
        ? "-"
        : data.GetProperty("ep").GetString()!;

    Console.WriteLine($"\n turn: {data.GetProperty("turn").GetString()}    castling: {(rights.Length == 0 ? "-" : rights)}"
        + $"    en passant: {passing}    idle: {data.GetProperty("idle").GetInt32()}");

    Console.WriteLine(status.IsTerminal
        ? $" final position — {status.Result}"
        : $" {moves.Count} legal, {moves.Evaluated} guards evaluated"
            + $"{(moves.Truncated ? $" (stopped at the limit of {Limit})" : string.Empty)}");
}

static string Right(JsonElement data, string field, char letter) =>
    data.GetProperty(field).GetBoolean() ? letter.ToString() : string.Empty;

/// <summary>Reads a move token back into its three parts.</summary>
static (string From, string To, string Tag) Parts(ValidInput move)
{
    string[] parts = move.Arguments["m"].Split('|');
    return (parts[0], parts[1], parts[2]);
}

static string Describe(ValidInput move)
{
    (string from, string to, string tag) = Parts(move);
    return tag switch
    {
        "-" => $"{from}{to}",
        "2" => $"{from}{to} (double step)",
        "ep" => $"{from}{to} (in passing)",
        "0-0" => "0-0",
        "0-0-0" => "0-0-0",
        _ => $"{from}{to} promoting to {tag}",
    };
}

ValidInput? Ask(ValidInputSet moves)
{
    while (true)
    {
        Console.Write("\n move (e2e4, e7e8q, 0-0, 'moves', or 'q' to quit) > ");
        string? line = Console.ReadLine()?.Trim();

        if (line is null || line.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (line.Equals("moves", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($" {string.Join(", ", moves.Select(Describe).Order(StringComparer.Ordinal))}");
            continue;
        }

        IReadOnlyList<ValidInput> matched = Match(moves, line);

        if (matched.Count == 1)
        {
            return matched[0];
        }

        Console.WriteLine(matched.Count == 0
            ? " not a legal move here."
            : $" which one? {string.Join(", ", matched.Select(Describe))}");
    }
}

/// <summary>Finds the moves a typed square pair could mean.</summary>
/// <remarks>
/// The move is one parameter carrying "from|to|tag", so a host that wants to accept "e2e4"
/// has to take the token apart. Nothing here decides anything about chess: every candidate
/// came out of GetValidInputs already, and this only picks among them.
/// </remarks>
static IReadOnlyList<ValidInput> Match(ValidInputSet moves, string typed)
{
    string text = typed.Replace(" ", string.Empty).ToLowerInvariant();

    if (text is "0-0" or "o-o")
    {
        return [.. moves.Where(move => Parts(move).Tag == "0-0")];
    }

    if (text is "0-0-0" or "o-o-o")
    {
        return [.. moves.Where(move => Parts(move).Tag == "0-0-0")];
    }

    // The token itself, for anyone who would rather say exactly which move they mean.
    if (text.Contains('|'))
    {
        return [.. moves.Where(move => move.Arguments["m"] == text)];
    }

    if (text.Length is 4 or 5)
    {
        string from = text[..2];
        string to = text[2..4];
        string? tag = text.Length == 5 ? text[4..] : null;

        return [.. moves.Where(move => Says(move, from, to, tag))];
    }

    return [];
}

static bool Says(ValidInput move, string from, string to, string? tag)
{
    (string movesFrom, string movesTo, string movesTag) = Parts(move);
    return movesFrom == from && movesTo == to && (tag is null || movesTag == tag);
}
