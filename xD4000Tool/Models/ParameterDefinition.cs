namespace xD4000Tool.Models;

public sealed class ParameterDefinition
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Address { get; set; }
    public string Category { get; set; } = "";
    public string Menu { get; set; } = "";
    public string Access { get; set; } = "";
    public string Type { get; set; } = "";
    public string Units { get; set; } = "";
    public string Factory { get; set; } = "";
    public string Range { get; set; } = "";
}
