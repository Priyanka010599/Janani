// Services/Concern.cs
// Shared by the deterministic threshold checkers (PregnancyVitalsThresholdChecker,
// PostpartumRecoveryChecker, EpdsScreeningChecker) — previously each declared
// an identical private nested record; pulled out here so they share one type
// instead of copy-pasting it.

using Janani.Models;

namespace Janani.Services;

public record Concern(string Description, AlertSeverity Severity);
