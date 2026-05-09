using System.Collections.Generic;

namespace xD4000Tool.Models;

public sealed class SpecModel
{
    public string Version { get; set; } = "";
    public List<ParameterDefinition> Parameters { get; set; } = new();
    public Dictionary<string, List<EnumItem>> Enumerations { get; set; } = new();
    public Dictionary<string, List<BitItem>> RegisterBits { get; set; } = new();
}

public sealed class EnumItem
{
    public int Value { get; set; }
    public string Display { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class BitItem
{
    public int Bit { get; set; }
    public string Text { get; set; } = "";
}
