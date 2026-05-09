namespace xD4000Tool.Models;

public static class Xd4000CoreRegs
{
    public const ushort ETA = 3201;
    public const ushort RFR = 3202;
    public const ushort ULN = 3207;
    public const ushort VBUS = 3243;
    public const ushort CRC = 8441;
    public const ushort CCC = 8442;
    public const ushort CMD = 8501;
    public const ushort LFR = 8502;
    public const ushort FR1 = 8413;
    public const ushort CNL_PC_TOOL = 15;

    public const ushort LFT = 7121;
    public static readonly ushort[] FaultHistory = new ushort[] { 7200,7201,7202,7203,7204,7205,7206,7207,7208,7209 };
}
