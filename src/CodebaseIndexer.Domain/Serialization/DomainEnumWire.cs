using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json.Serialization;
using CodebaseIndexer.Domain.Models;
using DomainMatchType = CodebaseIndexer.Domain.Models.MatchType;

namespace CodebaseIndexer.Domain.Serialization;

/// <summary>
/// Centralized wire-string mapping for domain closed-set enums (MCP JSON, Qdrant payload, Neo4j props).
/// Prefers <see cref="JsonStringEnumMemberNameAttribute"/> values over PascalCase <c>ToString()</c>.
/// </summary>
public static class DomainEnumWire
{
    private static readonly FrozenDictionary<SymbolType, string> SymbolTypeToWire =
        BuildToWireMap<SymbolType>();

    private static readonly FrozenDictionary<string, SymbolType> WireToSymbolType =
        BuildFromWireMap<SymbolType>();

    private static readonly FrozenDictionary<SourceLanguage, string> SourceLanguageToWire =
        BuildToWireMap<SourceLanguage>();

    private static readonly FrozenDictionary<string, SourceLanguage> WireToSourceLanguage =
        BuildFromWireMap<SourceLanguage>();

    private static readonly FrozenDictionary<NamedVector, string> NamedVectorToWire =
        BuildToWireMap<NamedVector>();

    private static readonly FrozenDictionary<string, NamedVector> WireToNamedVector =
        BuildFromWireMap<NamedVector>();

    private static readonly FrozenDictionary<ReferenceType, string> ReferenceTypeToWire =
        BuildToWireMap<ReferenceType>();

    private static readonly FrozenDictionary<string, ReferenceType> WireToReferenceType =
        BuildFromWireMap<ReferenceType>();

    private static readonly FrozenDictionary<DomainMatchType, string> MatchTypeToWire =
        BuildToWireMap<DomainMatchType>();

    private static readonly FrozenDictionary<string, DomainMatchType> WireToMatchType =
        BuildFromWireMap<DomainMatchType>();

    /// <summary>Canonical wire form for <see cref="SymbolType"/>.</summary>
    public static string ToWire(SymbolType value) => SymbolTypeToWire[value];

    /// <summary>Canonical wire form for <see cref="SourceLanguage"/>.</summary>
    public static string ToWire(SourceLanguage value) => SourceLanguageToWire[value];

    /// <summary>Canonical wire form for <see cref="NamedVector"/> (Phase 2).</summary>
    public static string ToWire(NamedVector value) => NamedVectorToWire[value];

    /// <summary>Canonical wire form for <see cref="ReferenceType"/> (Phase 3).</summary>
    public static string ToWire(ReferenceType value) => ReferenceTypeToWire[value];

    /// <summary>Canonical wire form for <see cref="DomainMatchType"/> (Phase 3).</summary>
    public static string ToWire(DomainMatchType value) => MatchTypeToWire[value];

    /// <summary>Try parse a wire string to <see cref="SymbolType"/>.</summary>
    public static bool TryParse(string? wire, out SymbolType value)
    {
        if (!string.IsNullOrEmpty(wire) && WireToSymbolType.TryGetValue(wire, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Try parse a wire string to <see cref="SourceLanguage"/>.</summary>
    public static bool TryParse(string? wire, out SourceLanguage value)
    {
        if (!string.IsNullOrEmpty(wire) && WireToSourceLanguage.TryGetValue(wire, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Try parse a wire string to <see cref="NamedVector"/>.</summary>
    public static bool TryParse(string? wire, out NamedVector value)
    {
        if (!string.IsNullOrEmpty(wire) && WireToNamedVector.TryGetValue(wire, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Try parse a wire string to <see cref="ReferenceType"/>.</summary>
    public static bool TryParse(string? wire, out ReferenceType value)
    {
        if (!string.IsNullOrEmpty(wire) && WireToReferenceType.TryGetValue(wire, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Try parse a wire string to <see cref="DomainMatchType"/>.</summary>
    public static bool TryParse(string? wire, out DomainMatchType value)
    {
        if (!string.IsNullOrEmpty(wire) && WireToMatchType.TryGetValue(wire, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Parse symbol kind or fall back to <see cref="SymbolType.Other"/>.</summary>
    public static SymbolType ParseOrOther(string? wire) =>
        TryParse(wire, out SymbolType value) ? value : SymbolType.Other;

    /// <summary>Parse language or fall back to <see cref="SourceLanguage.Unknown"/>.</summary>
    public static SourceLanguage ParseOrUnknown(string? wire) =>
        TryParse(wire, out SourceLanguage value) ? value : SourceLanguage.Unknown;

    private static FrozenDictionary<TEnum, string> BuildToWireMap<TEnum>()
        where TEnum : struct, Enum
    {
        var map = new Dictionary<TEnum, string>();
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attr = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
            var wire = attr?.Name ?? ToSnakeCase(field.Name);
            map[(TEnum)field.GetValue(null)!] = wire;
        }

        return map.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, TEnum> BuildFromWireMap<TEnum>()
        where TEnum : struct, Enum
    {
        var map = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var attr = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
            var wire = attr?.Name ?? ToSnakeCase(field.Name);
            map[wire] = (TEnum)field.GetValue(null)!;
        }

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        Span<char> buffer = stackalloc char[name.Length * 2];
        var written = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                buffer[written++] = '_';
            }

            buffer[written++] = char.ToLowerInvariant(c);
        }

        return new string(buffer[..written]);
    }
}
