using System.Reflection;
using System.Text.Json.Serialization;
using Playtyper.Shared.Models;

namespace Playtyper.Shared.Services;

public enum FeatureKind { Bool, Int, StringValue, Unsupported }

public sealed record FeatureBinding(FeatureManifest.Feature Feature, PropertyInfo Property, FeatureKind Kind)
{
    public object? GetValue(FeatureFlags flags) => Property.GetValue(flags);
    public void SetValue(FeatureFlags flags, object? value) => Property.SetValue(flags, value);
}

/// <summary>
/// Kopplar ihop FeatureManifest.Features (den mänskligt skrivna katalogen,
/// använd för AI-prompten) med de faktiska properties på FeatureFlags — via
/// [JsonPropertyName], INTE property-namnet, eftersom de medvetet skrivs i
/// olika casing (Id="doneExpiryDays" i manifestet, DoneExpiryDays i C#).
///
/// Det här är den kontroll FeatureManifest.cs:s egen klassdoc-kommentar
/// efterlyser ("en missad synk [skulle bli] ett byggfel istället för ett
/// hål som upptäcks om tre månader") — fast här driver den UI:t direkt
/// istället för att bara varna. Ett Feature utan matchande property (eller
/// med en typ vi inte har en widget för än) visas inte i rutnätet, men
/// försvinner ALDRIG ur den faktiska JSON:en — den ligger orörd i
/// PackConfig.Features och nås om nödvändigt via Avancerat-fliken.
/// </summary>
public static class FeatureFlagsReflection
{
    private static readonly Dictionary<string, PropertyInfo> ByJsonName = BuildIndex();

    private static Dictionary<string, PropertyInfo> BuildIndex()
    {
        var map = new Dictionary<string, PropertyInfo>();
        foreach (var prop in typeof(FeatureFlags).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (attr != null) map[attr.Name] = prop;
        }
        return map;
    }

    public static IReadOnlyList<FeatureBinding> AllBindings() =>
        FeatureManifest.Features
            .Select(f => ByJsonName.TryGetValue(f.Id, out var prop) ? new FeatureBinding(f, prop, KindOf(prop)) : null)
            .Where(b => b is { Kind: not FeatureKind.Unsupported })
            .Select(b => b!)
            .ToList();

    private static FeatureKind KindOf(PropertyInfo prop)
    {
        if (prop.PropertyType == typeof(bool)) return FeatureKind.Bool;
        if (prop.PropertyType == typeof(int)) return FeatureKind.Int;
        if (prop.PropertyType == typeof(string)) return FeatureKind.StringValue;
        return FeatureKind.Unsupported; // nested objects (StreakConfig m.fl.) - hör hemma i Avancerat tills vidare
    }
}
