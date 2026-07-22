using System.Text.Json.Serialization;

namespace CodebaseIndexer.Domain.Models;

/// <summary>Closed vocabulary of source languages aligned with <c>LanguageRegistry</c> ids.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceLanguage
{
    /// <summary>Unknown or empty language (summary fallback).</summary>
    [JsonStringEnumMemberName("unknown")]
    Unknown,

    /// <summary>Python.</summary>
    [JsonStringEnumMemberName("python")]
    Python,

    /// <summary>JavaScript.</summary>
    [JsonStringEnumMemberName("javascript")]
    JavaScript,

    /// <summary>TypeScript.</summary>
    [JsonStringEnumMemberName("typescript")]
    TypeScript,

    /// <summary>Go.</summary>
    [JsonStringEnumMemberName("go")]
    Go,

    /// <summary>Rust.</summary>
    [JsonStringEnumMemberName("rust")]
    Rust,

    /// <summary>Java.</summary>
    [JsonStringEnumMemberName("java")]
    Java,

    /// <summary>C.</summary>
    [JsonStringEnumMemberName("c")]
    C,

    /// <summary>C++.</summary>
    [JsonStringEnumMemberName("cpp")]
    Cpp,

    /// <summary>C#.</summary>
    [JsonStringEnumMemberName("csharp")]
    CSharp,

    /// <summary>SQL.</summary>
    [JsonStringEnumMemberName("sql")]
    Sql,

    /// <summary>XML / project XML.</summary>
    [JsonStringEnumMemberName("xml")]
    Xml,

    /// <summary>YAML.</summary>
    [JsonStringEnumMemberName("yaml")]
    Yaml,

    /// <summary>JSON.</summary>
    [JsonStringEnumMemberName("json")]
    Json,

    /// <summary>Properties / INI / env-style config.</summary>
    [JsonStringEnumMemberName("properties")]
    Properties,

    /// <summary>TOML.</summary>
    [JsonStringEnumMemberName("toml")]
    Toml,

    /// <summary>HCL / Terraform.</summary>
    [JsonStringEnumMemberName("hcl")]
    Hcl,

    /// <summary>Dockerfile.</summary>
    [JsonStringEnumMemberName("dockerfile")]
    Dockerfile,

    /// <summary>Groovy / Gradle.</summary>
    [JsonStringEnumMemberName("groovy")]
    Groovy,

    /// <summary>Protocol Buffers.</summary>
    [JsonStringEnumMemberName("protobuf")]
    Protobuf,

    /// <summary>Kotlin.</summary>
    [JsonStringEnumMemberName("kotlin")]
    Kotlin,

    /// <summary>Scala.</summary>
    [JsonStringEnumMemberName("scala")]
    Scala,

    /// <summary>Ruby.</summary>
    [JsonStringEnumMemberName("ruby")]
    Ruby,

    /// <summary>PHP.</summary>
    [JsonStringEnumMemberName("php")]
    Php,

    /// <summary>Swift.</summary>
    [JsonStringEnumMemberName("swift")]
    Swift,

    /// <summary>Dart.</summary>
    [JsonStringEnumMemberName("dart")]
    Dart,

    /// <summary>Bash / shell.</summary>
    [JsonStringEnumMemberName("bash")]
    Bash,

    /// <summary>PowerShell.</summary>
    [JsonStringEnumMemberName("powershell")]
    PowerShell,

    /// <summary>Markdown.</summary>
    [JsonStringEnumMemberName("markdown")]
    Markdown,
}
