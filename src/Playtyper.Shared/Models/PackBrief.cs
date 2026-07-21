namespace Playtyper.Shared.Models;

/// <summary>
/// All collected answers from the wizard interview.
/// v2: added v5 feature fields (streak, badges, auth, repeat, export, multi-select onboarding).
/// </summary>
public sealed class PackBrief
{
    // ── Identity ──────────────────────────────────────────────────────────────
    public string AppName      { get; set; } = "";
    public string PackId       { get; set; } = "";
    public string Tagline      { get; set; } = "";
    public string Emoji        { get; set; } = "✨";
    public string Description  { get; set; } = "";

    // ── Audience & context ────────────────────────────────────────────────────
    public string TargetAudience { get; set; } = "";
    public string UsageContext   { get; set; } = "";
    public string Tone           { get; set; } = "";

    // ── Branding ──────────────────────────────────────────────────────────────
    public string PrimaryColor   { get; set; } = "";
    public string SecondaryColor { get; set; } = "";
    public string AccentColor    { get; set; } = "";
    public string FontPreset     { get; set; } = "rounded";
    public int    BaseFontSize   { get; set; } = 16;

    // ── v18: app-type archetype ──────────────────────────────────────────────
    /// <summary>
    /// Id of the FeatureManifest.AppTypePresets entry chosen in
    /// CreatePackPage's app-type picker, e.g. "places-outdoor" or "recipes".
    /// Null/empty when no preset was picked (freeform/quick-mode packs, or
    /// anyone who skips the picker) — PromptGenerator falls back to its
    /// existing generic preset-matching prose in that case, unchanged from
    /// before v18. When set, PromptGenerator instead puts that one preset's
    /// suggested features/ui combo front and centre near the top of the
    /// prompt instead of leaving the AI to find it in the full reference
    /// table further down.
    /// </summary>
    public string? AppTypePresetId { get; set; }

    // ── Languages ─────────────────────────────────────────────────────────────
    public string        DefaultLanguage { get; set; } = "sv";
    public List<LangDef> Languages       { get; set; } = new();

    // ── Content ───────────────────────────────────────────────────────────────
    public List<string> Categories          { get; set; } = new();
    public List<string> Filters             { get; set; } = new();
    public int          TargetActivityCount { get; set; } = 15;
    public List<string> ActivityExamples    { get; set; } = new();
    public List<string> SituationPresets    { get; set; } = new();
    public string       ContentConstraints  { get; set; } = "";

    // ── Core features ─────────────────────────────────────────────────────────
    public string PanicButtonLabel    { get; set; } = "";
    public string PanicButtonSubtitle { get; set; } = "";
    public string PanicButtonStyle    { get; set; } = "calm";
    public bool   UseContentBlocks    { get; set; } = false;
    public bool   UseActionButtons    { get; set; } = false;
    public bool   UseHeroImages       { get; set; } = false;
    public bool   UseDoneTracking     { get; set; } = true;
    public bool   UseOnboarding       { get; set; } = false;
    public string OnboardingQuestion  { get; set; } = "";

    // ── v5 features ───────────────────────────────────────────────────────────

    /// <summary>Streak tracking. Null = disabled. Unit: daily|weekly|monthly.</summary>
    public StreakBrief? Streak { get; set; }

    /// <summary>Badge system enabled.</summary>
    public bool UseBadges { get; set; } = false;

    /// <summary>User-adjustable font size slider (important for elderly/accessibility).</summary>
    public bool UseFontSizeScale { get; set; } = false;

    /// <summary>Detect content changes since last done-marking.</summary>
    public bool UsePackVersioning { get; set; } = false;

    /// <summary>Whether activities should use repeat field (yearly/monthly/weekly/never).</summary>
    public bool UseRepeatField { get; set; } = false;

    /// <summary>Logbook feature enabled.</summary>
    public bool UseLogbook { get; set; } = false;

    /// <summary>Print view enabled.</summary>
    public bool UsePrintView { get; set; } = false;

    /// <summary>Export (CSS print) configuration. Null = disabled.</summary>
    public ExportBrief? Export { get; set; }

    /// <summary>Password protection. Null = open pack.</summary>
    public AuthBrief? Auth { get; set; }

    /// <summary>Onboarding uses multi-select step.</summary>
    public bool UseMultiSelectOnboarding { get; set; } = false;

    // ── v6 features ───────────────────────────────────────────────────────────

    /// <summary>Default layout mode: "grid" | "list".</summary>
    public string DefaultLayoutMode { get; set; } = "grid";

    /// <summary>Allow users to toggle between grid and list view.</summary>
    public bool LayoutUserToggle { get; set; } = true;

    /// <summary>
    /// Erbjud mosaik som ett tredje, växlingsbart vyläge (utöver grid/list)
    /// via pack.config.json:s `ui.availableLayouts`. Hierarkin styrs per
    /// aktivitet via `layoutHint` ("featured" | "compact" | utelämnad).
    /// Separat mekanism från DefaultLayoutMode/LayoutUserToggle — se
    /// AI-referensdokumentet ("Vyer och layout") för skillnaden.
    /// </summary>
    public bool OfferMosaic { get; set; } = false;

    /// <summary>Show quick-action buttons (timer/link) directly on cards.</summary>
    public bool UseCardActions { get; set; } = false;

    /// <summary>Allow users to create their own activities inside the app.</summary>
    public bool AllowUserContent { get; set; } = false;

    /// <summary>Per-activity free-text note field (always visible, not tied to logbook).</summary>
    public bool UseActivityNotes { get; set; } = false;

    /// <summary>Progression lock: activities unlock sequentially via modules.</summary>
    public bool UseProgressionLock { get; set; } = false;

    /// <summary>Smart (computed) filter expressions.</summary>
    public bool UseSmartFilters { get; set; } = false;

    /// <summary>Reminder config. Null = disabled.</summary>
    public ReminderBrief? Reminder { get; set; }

    /// <summary>Data sync mode: "none" | "export-only". Future: "github" | "dropbox" | "onedrive".</summary>
    public string DataSync { get; set; } = "none";

    // ── Version ───────────────────────────────────────────────────────────────
    public string Version { get; set; } = "2.0";

    // ── v4: Snabbläge / fritext-eskap ────────────────────────────────────────

    /// <summary>
    /// Fritext skriven av användaren — antingen hela innehållet i "Snabb"-läge,
    /// eller ihopslagna utdrag från enskilda "fritt"-eskap i "Guidad"-läge.
    /// Tom sträng om inte använt. Injiceras i AI-prompten och tolkas av Claude
    /// istället för (eller utöver) de strukturerade fälten ovan.
    /// </summary>
    public string FreeformNotes { get; set; } = "";

    /// <summary>Sant om PackBrief byggdes via "Snabb"-läget i intervjun.</summary>
    public bool IsQuickMode { get; set; } = false;

    // ── Källmaterial ──────────────────────────────────────────────────────────

    /// <summary>
    /// Källmaterial insamlat under intervjun (webbsidor, inklistrad text
    /// m.m.). Label = t.ex. sidtitel eller URL, Content = extraherad text.
    /// Injiceras av PromptGenerator som PRIMÄR referens för aktiviteter,
    /// kategorier, terminologi och ton. Tom lista = inget källmaterial
    /// insamlat, avsnittet utelämnas då helt ur prompten.
    /// </summary>
    public List<(string Label, string Content)> SourceMaterials { get; set; } = new();

    /// <summary>
    /// Bildkandidater hittade i källmaterialets webbsidor. Skickas explicit
    /// till Claude så riktiga bild-URL:er kan återanvändas för
    /// heroImage/thumbnail/images/gallery-content-block istället för att
    /// AI:n hittar på filnamn. Tom lista = inga kandidater, avsnittet
    /// utelämnas då helt ur prompten.
    /// </summary>
    public List<ImageCandidate> ImageCandidates { get; set; } = new();
}

/// <summary>En bild hittad i källmaterialets webbsidor, redo att föreslås till Claude.</summary>
public sealed class ImageCandidate
{
    public string Url { get; set; } = "";
    public string? Alt { get; set; }

    /// <summary>Vilket källmaterial (se PackBrief.SourceMaterials.Label) bilden kom från.</summary>
    public string SourceLabel { get; set; } = "";
}

public sealed class LangDef
{
    public string Code  { get; set; } = "";
    public string Label { get; set; } = "";
    public string Flag  { get; set; } = "";
}

public sealed class StreakBrief
{
    public string Unit             { get; set; } = "weekly";
    public int    GracePeriodHours { get; set; } = 4;
    public bool   ShowCounter      { get; set; } = true;
}

public sealed class ExportBrief
{
    public bool   ActivityCard { get; set; } = true;
    public bool   Logbook      { get; set; } = true;
    public string PhotoLayout  { get; set; } = "grid";
}

public sealed class AuthBrief
{
    public string PasswordHash  { get; set; } = "";
    public int    SessionHours  { get; set; } = 8;
    public string? HintKey      { get; set; }
}

public sealed class ReminderBrief
{
    public string Time      { get; set; } = "08:00";
    public string Frequency { get; set; } = "daily";   // daily | weekly
}
