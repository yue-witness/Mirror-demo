using System;

public enum TutorEmotion
{
    Neutral = 0,
    Encouraging = 1,
    Stern = 2
}

public enum TutorSpeechMode
{
    Silent,
    Standard,
    Essential
}

/// <summary>
/// Keeps portrait emotion and voice density decisions consistent across the
/// dialogue-only screen and the gameplay HUD.
/// </summary>
public static class TutorPresentationPolicy
{
    private static readonly string[] SilentSpeechPrefixes =
    {
        "choice_revision_",
        "choice_hesitation_",
        "bash_state_",
        "bash_select_",
        "bash_r1_tutor_",
        "bash_r2_tutor_",
        "limit_state_",
        "limit_select_",
        "limit_lock_",
        "limit_reveal_",
        "limit_terminal_"
    };

    private static readonly string[] SilentSpeechIds =
    {
        "bash_terminal_last",
        "bash_terminal_safe",
        "bash_terminal_trace",
        "bash_terminal_one",
        "bash_terminal_control"
    };

    /// <summary>
    /// Key story, rules, briefings and outcomes are voiced. Repeated tactical
    /// prompts are text-only, preventing selection chatter from interrupting
    /// the player's decision loop.
    /// </summary>
    public static TutorSpeechMode ResolveSpeechMode(string lineId)
    {
        if (string.IsNullOrWhiteSpace(lineId)
            || MatchesAny(lineId, SilentSpeechIds)
            || StartsWithAny(lineId, SilentSpeechPrefixes))
        {
            return TutorSpeechMode.Silent;
        }

        return lineId.StartsWith("restore_", StringComparison.OrdinalIgnoreCase)
            ? TutorSpeechMode.Standard
            : TutorSpeechMode.Essential;
    }

    public static TutorEmotion ResolveEmotion(
        string lineId,
        string text,
        bool signalAnomaly = false)
    {
        if (signalAnomaly
            || ContainsAny(lineId, "loss", "retry", "failure", "anomaly")
            || ContainsAny(
                text,
                "alert",
                "anomaly",
                "error",
                "fail",
                "lost",
                "threat",
                "unstable",
                "warning",
                "decommission",
                "do not disappoint",
                "unsuccessful"))
        {
            return TutorEmotion.Stern;
        }

        if (ContainsAny(lineId, "_win", "summary_complete", "summary_reasoning")
            || ContainsAny(
                text,
                "clever",
                "excellent",
                "good",
                "impressive",
                "success",
                "well done",
                "you win",
                "you passed",
                "satisfied",
                "recovered control",
                "evaluation sequence complete"))
        {
            return TutorEmotion.Encouraging;
        }

        if (ContainsAny(
            lineId,
            "terminal_",
            "choice_revision_",
            "choice_hesitation_",
            "limit_lock_",
            "limit_restart_",
            "summary_meta_"))
        {
            return TutorEmotion.Stern;
        }

        return TutorEmotion.Neutral;
    }

    private static bool MatchesAny(string value, params string[] candidates)
    {
        foreach (string candidate in candidates)
        {
            if (value.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
    {
        foreach (string prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string value, params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            if (value.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
