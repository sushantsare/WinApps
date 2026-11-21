using System.Text.Json.Serialization;
using AppStore.Core.Models;
using AppStore.Core.Winget;

namespace AppStore.Core;

/// <summary>Source-generated JSON (works with Native AOT and trimming).</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<HistoryEntry>))]
[JsonSerializable(typeof(HistoryEntry))]
[JsonSerializable(typeof(AppListFile))]
[JsonSerializable(typeof(List<OperationRequest>))]
[JsonSerializable(typeof(BatchEvent))]
internal sealed partial class AppJson : JsonSerializerContext
{
}

/// <summary>Objects printed by the PowerShell WinGet module (PascalCase, as PowerShell writes them).</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<PsPackage>))]
internal sealed partial class PsJson : JsonSerializerContext
{
}
