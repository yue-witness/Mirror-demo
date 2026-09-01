using System;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed record BashRuleConfiguration(
    int MinimumTake,
    int MaximumTake,
    int[] Round1InitialUnits,
    int[] Round2InitialUnits);

public sealed record LimitBashRuleConfiguration(
    int MinimumTake,
    int MaximumTake,
    int MinimumInitialUnits,
    int MaximumInitialUnits,
    int RequiredPlayerWins,
    int RequiredConsecutiveDraws);

public sealed class RuleConfiguration
{
    public required BashRuleConfiguration Bash { get; init; }

    public required LimitBashRuleConfiguration LimitBash { get; init; }

    public static RuleConfiguration Load(string bashPath, string limitBashPath)
    {
        return Load(File.ReadAllText, bashPath, limitBashPath);
    }

    public static RuleConfiguration Load(
        Func<string, string> readAllText,
        string bashPath,
        string limitBashPath)
    {
        ArgumentNullException.ThrowIfNull(readAllText);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        BashRuleConfiguration? bash = JsonSerializer.Deserialize<BashRuleConfiguration>(
            readAllText(bashPath),
            options);
        LimitBashRuleConfiguration? limit =
            JsonSerializer.Deserialize<LimitBashRuleConfiguration>(
                readAllText(limitBashPath),
                options);

        if (bash is null || limit is null)
        {
            throw new InvalidDataException("Rule configuration could not be read.");
        }

        Validate(bash, limit);
        return new RuleConfiguration
        {
            Bash = bash,
            LimitBash = limit
        };
    }

    private static void Validate(
        BashRuleConfiguration bash,
        LimitBashRuleConfiguration limit)
    {
        if (bash.MinimumTake != BashGame.MinimumTake
            || bash.MaximumTake != BashGame.MaximumTake
            || bash.Round1InitialUnits.Length == 0
            || bash.Round2InitialUnits.Length == 0
            || bash.Round1InitialUnits.Any(units => units < 1)
            || bash.Round2InitialUnits.Any(units => units < 1))
        {
            throw new InvalidDataException("Bash rule configuration is outside code bounds.");
        }

        if (limit.MinimumTake != LimitBashGame.MinimumTake
            || limit.MaximumTake != LimitBashGame.MaximumTake
            || limit.MinimumInitialUnits != LimitBashGame.MinimumInitialUnits
            || limit.MaximumInitialUnits != LimitBashGame.MaximumInitialUnits
            || limit.RequiredPlayerWins != 2
            || limit.RequiredConsecutiveDraws != 2)
        {
            throw new InvalidDataException(
                "Limit Bash rule configuration is outside the formal demo bounds.");
        }
    }
}
