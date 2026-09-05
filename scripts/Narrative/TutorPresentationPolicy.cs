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
    /// <summary>
    /// Story and timed action explanations may replace a cue. Optional tactical
    /// feedback uses the shared player's non-interrupting cooldown.
    /// </summary>
    public static TutorSpeechMode ResolveSpeechMode(string lineId)
    {
        if (string.IsNullOrWhiteSpace(lineId)
            || lineId.StartsWith("limit_lock_", StringComparison.OrdinalIgnoreCase))
        {
            return TutorSpeechMode.Silent;
        }

        if (lineId == "limit_reveal_tutor_choice")
        {
            return TutorSpeechMode.Essential;
        }

        return StartsWithAny(lineId,
            "choice_", "bash_state_", "bash_select_", "bash_r1_tutor_",
            "bash_r2_tutor_", "bash_terminal_", "limit_state_",
            "limit_select_", "limit_reveal_", "limit_terminal_", "restore_")
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
