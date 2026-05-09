using xD4000Tool.Models;
using xD4000Tool.Services;

namespace xD4000Tool.ViewModels;

public sealed class ParameterRowViewModel : ViewModelBase
{
    public ParameterDefinition Def { get; }

    public ParameterRowViewModel(ParameterDefinition def) => Def = def;

    public int Address => Def.Address;
    public string Code => Def.Code;
    public string Name => Def.Name;
    public string Access => Def.Access;
    public string Type => Def.Type;
    public string Units => Def.Units;

    public double Scale => Scaling.GetScale(Def.Units);
    public string UnitLabel => Scaling.GetUnitLabel(Def.Units);

    private ushort? _raw;
    public ushort? Raw
    {
        get => _raw;
        set { _raw = value; OnPropertyChanged(); OnPropertyChanged(nameof(Value)); }
    }

    private string? _edit;
    public string? Edit
    {
        get => _edit;
        set { _edit = value; OnPropertyChanged(); }
    }

    public string Value
    {
        get
        {
            if (Raw is null) return "";
            if (Type.Contains("Signed16"))
            {
                short s = unchecked((short)Raw.Value);
                return (s * Scale).ToString("0.###") + (UnitLabel.Length > 0 ? $" {UnitLabel}" : "");
            }
            return (Raw.Value * Scale).ToString("0.###") + (UnitLabel.Length > 0 ? $" {UnitLabel}" : "");
        }
    }

    public bool IsWritable => Access.Contains("W");
}
