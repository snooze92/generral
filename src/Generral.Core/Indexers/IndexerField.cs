// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Http/ClientSchema/Field.cs
using System.Text.Json.Nodes;

namespace Generral.Core.Indexers;

/// <summary>Sonarr's provider <c>Field</c>, in its JSON shape. Only <c>name</c> and <c>value</c> are read back.</summary>
public sealed record IndexerField
{
    public int Order { get; init; }

    public required string Name { get; init; }

    public string? Label { get; init; }

    public string? Unit { get; init; }

    public string? HelpText { get; init; }

    public JsonNode? Value { get; init; }

    /// <summary><c>textbox</c>, <c>number</c>, <c>checkbox</c> or <c>select</c>.</summary>
    public string Type { get; init; } = "textbox";

    public bool Advanced { get; init; }

    public IReadOnlyList<IndexerSelectOption>? SelectOptions { get; init; }

    /// <summary><c>normal</c> or <c>apiKey</c> (the value is masked when read).</summary>
    public string Privacy { get; init; } = "normal";

    public bool IsFloat { get; init; }
}

public sealed record IndexerSelectOption(int Value, string Name, int Order, string? Hint = null);
