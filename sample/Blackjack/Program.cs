// Copyright (c) 2026 Reny
// Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Text.Json;
using Rulealize;

// A hand of blackjack, played entirely through the runtime. This is the sample where the
// next state is not decided by whoever moves: ann says `hit` and the deck says which card,
// and those are two different questions asked with two different calls.
//
//   GetValidInputs  → who may do what
//   GetOutcomes     → what may then happen, and how likely each of those is
//
// That is the whole traversal, and it is the same two calls in the same order as the Reversi
// sample makes. The difference is only that the inner loop here turns thirteen times instead
// of once — read that sample first if this is your first one.
//
// `--auto` deals a hand with the house both choosing and sampling. `--odds` adds, before
// every decision, the exact chance each option busts the hand on the next card. Nothing in
// this file knows that an ace is worth eleven.

bool automatic = args.Contains("--auto", StringComparer.OrdinalIgnoreCase);
bool odds = args.Contains("--odds", StringComparer.OrdinalIgnoreCase);
Random shoe = new(Seed(args));

// ── 1. Build the vocabulary ────────────────────────────────────────────────────────
string pluginFolder = Path.Combine(AppContext.BaseDirectory, "plugin");
RuleRuntime runtime = new RuleRuntime().LoadPluginsFrom(pluginFolder);

Console.WriteLine($"Loaded {runtime.Plugins.Length} plugins:");
foreach (var manifest in runtime.Plugins.OrderBy(p => p.Namespace, StringComparer.Ordinal))
{
    string draws = runtime.Operations.Any(o => o.Plugin.Id == manifest.Id && o.Kind == OperationKind.Draw)
        ? "  draws"
        : string.Empty;
    Console.WriteLine($"  {manifest.Namespace,-7} {manifest.Id} {manifest.Version}{draws}");
}

// ── 2. Compile the rule set ───────────────────────────────────────────────────────
RuleContext blackjack = runtime.CreateContext(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RuleSet", "blackjack.json")));

Console.WriteLine($"\nRule set: {blackjack.RuleSet}   inputs: {string.Join(", ", blackjack.Inputs)}");

// ── 3. Play ───────────────────────────────────────────────────────────────────────
string state = blackjack.InitialState;
int step = 0;

while (true)
{
    ValidInputSet moves = blackjack.GetValidInputs(state, validationLimit: 128);
    TerminalStatus status = blackjack.GetTerminalStatus(state);

    Render(state, moves, status);

    if (status.IsTerminal)
    {
        Console.WriteLine($"\nHand over after {step} steps. Table: {status.Result}.");
        break;
    }

    if (moves.Count == 0)
    {
        Console.WriteLine("\nNo legal input, and the rule set does not consider this final. Stopping.");
        break;
    }

    // ── The choice ────────────────────────────────────────────────────────────────
    // Somebody decides which input. When there is only one — a card being dealt, the
    // dealer being made to draw, the table being settled — nobody decides anything and
    // the rules have already said so by leaving one candidate.
    if (odds && moves.Count > 1)
    {
        Console.WriteLine();
        foreach (ValidInput option in moves)
        {
            Console.WriteLine($"    {option.Input,-8} busts on the next card {Percent(BustChance(state, option))}");
        }
    }

    ValidInput? chosen = moves.Count == 1
        ? moves[0]
        : automatic ? Policy(state, moves) : Ask(moves);

    if (chosen is null)
    {
        Console.WriteLine("\nStopped.");
        break;
    }

    // ── What may happen ───────────────────────────────────────────────────────────
    // The move goes back in and comes out as everything it could lead to, with a
    // probability on each. `stand` and `settle` draw nothing and produce exactly one of
    // these, so this loop needs no special case for them and neither does the caller.
    OutcomeSet outcomes = blackjack.GetOutcomes(
        chosen.ToInputDocument(blackjack.RuleSet),
        state,
        outcomeLimit: 64);

    Outcome happened = Sample(outcomes, shoe);

    string what = happened.Draws.IsEmpty
        ? string.Empty
        : $" → {string.Join(" ", happened.Draws)}   ({Percent(happened.Probability)} of {outcomes.Count})";

    // No actor is not a gap in the output. `dealSeat` and `settle` name nobody because
    // nobody decides them, and the rule set saying so is what a host reads to know that.
    string who = chosen.Actor is string actor ? $"{actor}: " : string.Empty;
    Console.WriteLine($"\n{who}{chosen.Input}{what}");

    // No further call. An outcome carries where it leads, because enumerating the
    // alternatives and applying one is the same work and asking twice would do it twice.
    state = happened.Result.State;
    step++;

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

// ── Where the randomness lives ────────────────────────────────────────────────────
// Here, and nowhere below the runtime. The rule set says what could come off the deck and
// how likely each of those is; picking one of them is five lines in the host, and it is the
// host that knows whether this is a game being played, a simulation being run, or a hand
// being replayed out of a log.
static Outcome Sample(OutcomeSet outcomes, Random random)
{
    // Scaled by Coverage rather than by one, so that a set the limit cut short is still
    // sampled in proportion. It is never short here — sixty-four is comfortably more than
    // thirteen — but a rule set with a deeper draw tree would need this.
    double roll = random.NextDouble() * outcomes.Coverage;
    foreach (Outcome outcome in outcomes)
    {
        roll -= outcome.Probability;
        if (roll <= 0)
        {
            return outcome;
        }
    }

    return outcomes[^1];
}

// ── Deciding with the probabilities ───────────────────────────────────────────────
// The chance an option ends the acting seat's hand on the next card. Exact, not sampled,
// and it needs no search at all: the outcomes of one move are the whole distribution.
//
// This is also the base case of an expectimax. Give the recursion a value for a finished
// hand and let it sum probability × value over the outcomes of each move, and what comes
// back is the option a player should take — the same shape, and the same two calls.
double BustChance(string position, ValidInput move)
{
    string? seat = move.Actor;
    OutcomeSet outcomes = blackjack.GetOutcomes(move.ToInputDocument(blackjack.RuleSet), position, 64);

    return outcomes
        .Where(outcome => seat is not null && Busted(Hand(outcome.Result.State, seat)))
        .Sum(outcome => outcome.Probability);
}

ValidInput Policy(string position, ValidInputSet moves)
{
    // Hit below seventeen, otherwise stand. Not basic strategy, and not meant to be — what
    // it is here to show is that a host deciding badly and a host deciding well make the
    // same two calls.
    ValidInput? stand = moves.FirstOrDefault(m => m.Input == "stand");
    if (stand is null || moves.All(m => m.Input != "hit"))
    {
        return moves[0];
    }

    return Best(Hand(position, stand.Actor!)) < 17 ? moves.First(m => m.Input == "hit") : stand;
}

static ValidInput? Ask(ValidInputSet moves)
{
    Console.Write($"\n{moves[0].Actor}: {string.Join(" / ", moves.Select(m => m.Input))}? ");
    string? answer = Console.ReadLine();
    return answer is null ? null : moves.FirstOrDefault(m => m.Input == answer.Trim()) ?? moves[0];
}

// ── Presentation ──────────────────────────────────────────────────────────────────
// Everything below is about showing a table to a person. The runtime is done.
//
// The totals here are worked out in C# purely to print them. The rule set has its own and
// does not share it — a hand is a list of ranks in the state document, and what it adds up
// to is a definition nothing outside the document can call.

void Render(string stateDocument, ValidInputSet moves, TerminalStatus status)
{
    using JsonDocument document = JsonDocument.Parse(stateDocument);
    JsonElement data = document.RootElement.GetProperty("data");

    Console.WriteLine();
    foreach (JsonElement seat in data.GetProperty("seats").EnumerateArray())
    {
        string[] cards = Ranks(seat.GetProperty("cards"));
        string id = seat.GetProperty("id").GetString()!;
        string marker = moves.Any(m => m.Actor == id) ? "»" : " ";
        string result = seat.GetProperty("result").GetString() is string r ? $"   {r}" : string.Empty;
        string total = cards.Length == 0 ? string.Empty : Describe(cards);

        int bet = seat.GetProperty("bet").GetInt32();
        Console.WriteLine(
            $" {marker} {id,-6} {string.Join(" ", cards),-14} {total,-10} bet {bet}{result}");
    }

    string[] up = Ranks(data.GetProperty("dealer"));
    string showing = up.Length == 0 ? string.Empty : Describe(up);
    Console.WriteLine($"   {"dealer",-6} {string.Join(" ", up),-14} {showing}");

    if (!status.IsTerminal)
    {
        Console.WriteLine($"\n legal: {string.Join(", ", moves.Select(m => m.Input).Distinct(StringComparer.Ordinal))}");
    }
}

static string Describe(string[] cards)
{
    if (Busted(cards))
    {
        return $"{Hard(cards)} bust";
    }

    int best = Best(cards);
    return best == 21 && cards.Length == 2 ? "21 natural" : best.ToString(CultureInfo.InvariantCulture);
}

static string Percent(double value) => $"{value * 100:F1}%";

static string[] Ranks(JsonElement cards) => [.. cards.EnumerateArray().Select(c => c.GetString()!)];

static string[] Hand(string stateDocument, string seat)
{
    using JsonDocument document = JsonDocument.Parse(stateDocument);
    JsonElement seats = document.RootElement.GetProperty("data").GetProperty("seats");

    return seats.EnumerateArray().Where(s => s.GetProperty("id").GetString() == seat)
        .Select(s => Ranks(s.GetProperty("cards")))
        .FirstOrDefault() ?? [];
}

static int Hard(string[] cards) => cards.Sum(c => c switch
{
    "A" => 1,
    "T" or "J" or "Q" or "K" => 10,
    _ => int.Parse(c, CultureInfo.InvariantCulture)
});

static int Best(string[] cards)
{
    int hard = Hard(cards);
    return cards.Contains("A") && hard + 10 <= 21 ? hard + 10 : hard;
}

static bool Busted(string[] cards) => Hard(cards) > 21;

static int Seed(string[] arguments)
{
    int index = Array.FindIndex(arguments, a => a.Equals("--seed", StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < arguments.Length
        && int.TryParse(arguments[index + 1], CultureInfo.InvariantCulture, out int seed)
        ? seed
        : Environment.TickCount;
}
