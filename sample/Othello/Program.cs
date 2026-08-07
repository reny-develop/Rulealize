// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Rulealize;

// A game of Othello, played entirely through the runtime. Nothing in this file knows the
// rules: it loads plugins, compiles a document, asks what is legal, and applies what the
// user picked. Swapping othello.json for another rule set would change the game without
// changing a line here — except the board rendering, which does assume a board.

bool automatic = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);

// ── 1. Build the vocabulary ────────────────────────────────────────────────────────
// A runtime starts with no operations at all. Everything a rule set is allowed to say
// comes from a plugin, and plugins are found by scanning a folder for assemblies.
string pluginFolder = Path.Combine(AppContext.BaseDirectory, "plugins");
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
RuleContext othello = runtime.CreateContext(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuleSets", "othello.json")));

Console.WriteLine($"\nRule set: {othello.RuleSet}   inputs: {string.Join(", ", othello.Inputs)}");

// ── 3. Play ───────────────────────────────────────────────────────────────────────
// A context holds no position. The state travels in and out as a document, so a game can
// be suspended, stored, and resumed by keeping nothing but this string.
string state = othello.InitialState;
int ply = 0;

while (true)
{
    // GetValidInputs enumerates the product of each input's parameter domains and sifts it
    // with that input's guard. The limit bounds how many guards are evaluated; Othello has
    // sixty-five candidates, so it is never close.
    ValidInputSet moves = othello.GetValidInputs(state, validationLimit: 128);

    // Whether a position is final is a separate question from what is legal in it.
    // GetValidInputs does not consult the terminal section — a rule set whose guards stay
    // satisfiable after the game ends would still list moves, and the runtime does not
    // second-guess it. Asking here is the host's job.
    TerminalStatus status = othello.GetTerminalStatus(state);

    Render(state, moves, status);

    if (status.IsTerminal)
    {
        Console.WriteLine($"\nGame over after {ply} plies. Result: {status.Result}.");
        break;
    }

    if (moves.Count == 0)
    {
        Console.WriteLine("\nNo legal input, and the rule set does not consider this final. Stopping.");
        break;
    }

    ValidInput? chosen = automatic ? moves[0] : Ask(moves);
    if (chosen is null)
    {
        Console.WriteLine("\nStopped.");
        break;
    }

    Console.WriteLine($"\n{chosen.Actor} plays {chosen}");

    // A move that came out of GetValidInputs goes straight back in. That round trip is why
    // arguments are written in their own JSON form and why an opaque value such as a
    // coordinate has to have a text form.
    TransitionResult result = othello.ApplyToState(
        chosen.ToInputDocument(othello.RuleSet),
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

void Render(string stateDocument, ValidInputSet moves, TerminalStatus status)
{
    using JsonDocument document = JsonDocument.Parse(stateDocument);
    JsonElement data = document.RootElement.GetProperty("data");
    JsonElement board = data.GetProperty("board");

    HashSet<string> playable = status.IsTerminal
        ? []
        : [.. moves.Where(m => m.Arguments.ContainsKey("at")).Select(m => m.Arguments["at"])];

    Console.WriteLine();
    Console.WriteLine("    a b c d e f g h");
    for (int rank = 8; rank >= 1; rank--)
    {
        Console.Write($" {rank}  ");
        for (char file = 'a'; file <= 'h'; file++)
        {
            string square = $"{file}{rank}";

            // A square with no stone is left out of the document entirely — the board is
            // serialized sparsely, which is the grid plugin's decision, not the core's.
            string glyph = board.TryGetProperty(square, out JsonElement cell)
                ? cell.GetString() == "black" ? "@" : "O"
                : playable.Contains(square) ? "." : "-";

            Console.Write($"{glyph} ");
        }

        Console.WriteLine($" {rank}");
    }

    Console.WriteLine("    a b c d e f g h");

    int black = Count(board, "black");
    int white = Count(board, "white");
    Console.WriteLine($"\n @ black {black}    O white {white}    turn: {data.GetProperty("turn").GetString()}"
        + $"    passes: {data.GetProperty("passes").GetInt32()}");

    Console.WriteLine(status.IsTerminal
        ? " final position"
        : moves.Count == 0
            ? " no legal input"
            : $" legal: {string.Join(", ", moves.Select(m => m.ToString()))}");
}

static int Count(JsonElement board, string colour) =>
    board.EnumerateObject().Count(square => square.Value.GetString() == colour);

ValidInput? Ask(ValidInputSet moves)
{
    while (true)
    {
        Console.Write("\n move (square, 'pass', or 'q' to quit) > ");
        string? line = Console.ReadLine()?.Trim();

        if (line is null || line.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (line.Equals("pass", StringComparison.OrdinalIgnoreCase))
        {
            ValidInput? pass = moves.FirstOrDefault(m => m.Input == "pass");
            if (pass is not null)
            {
                return pass;
            }

            Console.WriteLine(" passing is not legal here.");
            continue;
        }

        ValidInput? move = moves.FirstOrDefault(
            m => m.Arguments.TryGetValue("at", out string? at)
                && at.Equals(line, StringComparison.OrdinalIgnoreCase));

        if (move is not null)
        {
            return move;
        }

        Console.WriteLine(" not a legal move here.");
    }
}
