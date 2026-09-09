// Services/AgentArchitecture.cs
// Multi-agent foundation for Janani.
//
// Each specialized agent focuses on ONE domain — meal suggestions,
// companion chat, birth planning, etc. They share a common AgentContext
// (user state: week, language, mood, name) without coupling to each other.
// The registry orchestrates all agents and is the single DI entry point
// for pages — pages never instantiate agents directly.
//
// This maps directly to the multi-agent pattern: specialized agents,
// shared context, pub/sub via the registry's event model.

namespace Janani.Services;

// ── Supported languages ──────────────────────────────────────────────────────
public enum AppLanguage
{
    English, Hindi, Telugu, Japanese, Norwegian, Arabic,
    Bengali, Marathi, Tamil, Gujarati, Urdu, Kannada, Odia, Malayalam, Punjabi
}

public static class LanguageNames
{
    public static string ToPromptInstruction(this AppLanguage lang) => lang switch
    {
        AppLanguage.Hindi     => "Please respond entirely in Hindi (हिंदी).",
        AppLanguage.Telugu    => "Please respond entirely in Telugu (తెలుగు).",
        AppLanguage.Japanese  => "Please respond entirely in Japanese (日本語).",
        AppLanguage.Norwegian => "Please respond entirely in Norwegian (Norsk).",
        AppLanguage.Arabic    => "Please respond entirely in Arabic (العربية).",
        AppLanguage.Bengali   => "Please respond entirely in Bengali (বাংলা).",
        AppLanguage.Marathi   => "Please respond entirely in Marathi (मराठी).",
        AppLanguage.Tamil     => "Please respond entirely in Tamil (தமிழ்).",
        AppLanguage.Gujarati  => "Please respond entirely in Gujarati (ગુજરાતી).",
        AppLanguage.Urdu      => "Please respond entirely in Urdu (اردو).",
        AppLanguage.Kannada   => "Please respond entirely in Kannada (ಕನ್ನಡ).",
        AppLanguage.Odia      => "Please respond entirely in Odia (ଓଡ଼ିଆ).",
        AppLanguage.Malayalam => "Please respond entirely in Malayalam (മലയാളം).",
        AppLanguage.Punjabi   => "Please respond entirely in Punjabi (ਪੰਜਾਬੀ).",
        _                     => "Please respond in English."
    };

    public static string DisplayName(this AppLanguage lang) => lang switch
    {
        AppLanguage.Hindi     => "हिंदी",
        AppLanguage.Telugu    => "తెలుగు",
        AppLanguage.Japanese  => "日本語",
        AppLanguage.Norwegian => "Norsk",
        AppLanguage.Arabic    => "العربية",
        AppLanguage.Bengali   => "বাংলা",
        AppLanguage.Marathi   => "मराठी",
        AppLanguage.Tamil     => "தமிழ்",
        AppLanguage.Gujarati  => "ગુજરાતી",
        AppLanguage.Urdu      => "اردو",
        AppLanguage.Kannada   => "ಕನ್ನಡ",
        AppLanguage.Odia      => "ଓଡ଼ିଆ",
        AppLanguage.Malayalam => "മലയാളം",
        AppLanguage.Punjabi   => "ਪੰਜਾਬੀ",
        _                     => "English"
    };
}

// ── Shared agent context — passed to every agent on every call ───────────────
public record AgentContext(
    string UserName,
    int PregnancyWeek,
    AppLanguage Language,
    bool WorkingWomanMode,
    string? LastMood = null,      // from the most recent mood check-in
    bool IsBereaved = false,      // from UserProfile.CareContext — see Companion.razor
    string? BirthOutcome = null,  // the raw BirthOutcome enum name, e.g. "PartialLossMultiple" —
                                   // IsBereaved alone can't distinguish a full loss from a
                                   // surviving-twin case, and the two need very different replies
    string? BabyName = null       // from UserProfile.BabyName, for the bereavement prompt to use gently
);

// ── Common interface all agents implement ────────────────────────────────────
public interface IBloomAgent
{
    string AgentName { get; }
    string AgentRole { get; }
}

// ── Shared prompt-building helper — every agent wraps its own core prompt
// with the same user-context/language/tone boilerplate, so the tone stays
// consistent no matter which agent answers. Internal (not file-scoped) so
// every agent file in this project can share the one implementation.
internal static class AgentHelper
{
    public static string Build(string corePrompt, AgentContext ctx) =>
        $"""
        {corePrompt}

        USER CONTEXT:
        - Name: {ctx.UserName}
        - Pregnancy week: {ctx.PregnancyWeek}
        - Working woman mode: {ctx.WorkingWomanMode}
        {(ctx.LastMood != null ? $"- How she's feeling today: {ctx.LastMood}" : "")}

        LANGUAGE: {ctx.Language.ToPromptInstruction()}

        TONE (non-negotiable): Warm, gentle, never prescriptive. Never use "should",
        "must", or "you need to". Always meet her where she is. She is doing something
        extraordinary — honor that in every word.
        """;
}

// ── Registry — single DI entry point for pages ──────────────────────────────
public class BloomAgentRegistry
{
    public MealAgent                    Meals         { get; }
    public PartnerAgent                 Partner       { get; }
    public BirthPlanAgent               BirthPlan     { get; }
    public CompanionService             Companion     { get; }
    public DayNurtureService            DayNurture    { get; }
    public ElderNurtureService          ElderNurture  { get; }
    public HealthMonitorService         HealthMonitor { get; }
    public PregnancyVitalsMonitorService PregnancyVitalsMonitor { get; }
    public PostpartumRecoveryMonitorService PostpartumRecoveryMonitor { get; }
    public CaregiverCoordinationService CaregiverCoordination { get; }
    public InfantCareService            InfantCare    { get; }
    public MedicineLookupService        MedicineLookup { get; }

    public BloomAgentRegistry(
        MealAgent meals,
        PartnerAgent partner,
        BirthPlanAgent birthPlan,
        CompanionService companion,
        DayNurtureService dayNurture,
        ElderNurtureService elderNurture,
        HealthMonitorService healthMonitor,
        PregnancyVitalsMonitorService pregnancyVitalsMonitor,
        PostpartumRecoveryMonitorService postpartumRecoveryMonitor,
        CaregiverCoordinationService caregiverCoordination,
        InfantCareService infantCare,
        MedicineLookupService medicineLookup)
    {
        Meals                  = meals;
        Partner                = partner;
        BirthPlan              = birthPlan;
        Companion              = companion;
        DayNurture             = dayNurture;
        ElderNurture           = elderNurture;
        HealthMonitor          = healthMonitor;
        PregnancyVitalsMonitor = pregnancyVitalsMonitor;
        PostpartumRecoveryMonitor = postpartumRecoveryMonitor;
        CaregiverCoordination  = caregiverCoordination;
        InfantCare             = infantCare;
        MedicineLookup         = medicineLookup;
    }

    public IReadOnlyList<IBloomAgent> GetAllAgents() =>
    [
        Meals,
        Partner,
        BirthPlan,
        Companion,
        DayNurture,
        ElderNurture,
        HealthMonitor,
        PregnancyVitalsMonitor,
        PostpartumRecoveryMonitor,
        CaregiverCoordination,
        InfantCare,
        MedicineLookup
    ];
}
