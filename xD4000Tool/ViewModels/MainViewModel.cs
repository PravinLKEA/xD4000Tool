using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using xD4000Tool.Models;
using xD4000Tool.Services;

namespace xD4000Tool.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly SpecModel _spec;
    private readonly ModbusTcpClient _client = new();
    private CancellationTokenSource? _pollCts;
    private System.Timers.Timer? _trendTimer;

    public MainViewModel()
    {
        _spec = SpecLoader.Load();
        Title = $"xD4000Tool v{_spec.Version}";
        BuildTree();
        ApplyFilter();

        ConnectCommand = new RelayCommand(async _ => await ConnectAsync());
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => Connected);
        ReadGroupCommand = new RelayCommand(async _ => await ReadCurrentGroupAsync(), _ => Connected);
        WriteSelectedCommand = new RelayCommand(async _ => await WriteSelectedAsync(), _ => Connected);

        RunCommand = new RelayCommand(async _ => await PulseCmdBitAsync(3), _ => Connected);
        StopCommand = new RelayCommand(async _ => await PulseCmdBitAsync(2), _ => Connected);
        ResetCommand = new RelayCommand(async _ => await PulseCmdBitAsync(7), _ => Connected);
        WriteSetpointCommand = new RelayCommand(async _ => await WriteLfrAsync(), _ => Connected);
        PreparePcRefCommand = new RelayCommand(async _ => await PreparePcRefAsync(), _ => Connected);

        RefreshDiagnosticsCommand = new RelayCommand(async _ => await RefreshDiagnosticsAsync(), _ => Connected);
        StartTrendCommand = new RelayCommand(_ => StartTrend(), _ => Connected);
        StopTrendCommand = new RelayCommand(_ => StopTrend(), _ => Connected);

        BackupCommand = new RelayCommand(async _ => await BackupCurrentGroupAsync(), _ => Connected);
        SaveProjectCommand = new RelayCommand(_ => SaveProject());
        LoadProjectCommand = new RelayCommand(_ => LoadProject());
        CompareCommand = new RelayCommand(async _ => await CompareWithDriveAsync(), _ => Connected && ProjectSnapshot.Count > 0);
        RestoreDiffCommand = new RelayCommand(async _ => await RestoreDiffAsync(), _ => Connected && DiffRows.Count > 0);
    }

    public string Title { get; }

    // Header status
    private string _status = "";
    public string Status { get => _status; private set { _status = value; OnPropertyChanged(); } }

    // Connection
    private string _ip = "192.168.0.10";
    public string Ip { get => _ip; set { _ip = value; OnPropertyChanged(); } }

    private int _port = 502;
    public int Port { get => _port; set { _port = value; OnPropertyChanged(); } }

    private byte _unitId = 1;
    public byte UnitId { get => _unitId; set { _unitId = value; OnPropertyChanged(); } }

    private bool _connected;
    public bool Connected { get => _connected; private set { _connected = value; OnPropertyChanged(); } }

    private bool _enableControl;
    public bool EnableControl { get => _enableControl; set { _enableControl = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanWriteSetpoint)); } }

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }

    // Tree + Search
    public ObservableCollection<TreeNode> Tree { get; } = new();

    private TreeNode? _selectedNode;
    public TreeNode? SelectedNode { get => _selectedNode; set { _selectedNode = value; OnPropertyChanged(); ApplyFilter(); } }

    private string _search = "";
    public string Search { get => _search; set { _search = value; OnPropertyChanged(); ApplyFilter(); } }

    public ObservableCollection<ParameterRowViewModel> ParameterRows { get; } = new();

    private ParameterRowViewModel? _selectedRow;
    public ParameterRowViewModel? SelectedRow { get => _selectedRow; set { _selectedRow = value; OnPropertyChanged(); } }

    public ICommand ReadGroupCommand { get; }
    public ICommand WriteSelectedCommand { get; }

    // Operate
    private double _lfrHz = 10;
    public double LfrHz { get => _lfrHz; set { _lfrHz = value; OnPropertyChanged(); } }

    private ushort _etaRaw;
    public ushort ETA_Raw { get => _etaRaw; private set { _etaRaw = value; OnPropertyChanged(); OnPropertyChanged(nameof(EtaText)); OnPropertyChanged(nameof(IsRunning)); } }
    public string EtaText => $"ETA: {ETA_Raw} (raw)";
    public bool IsRunning => (ETA_Raw & (1 << 2)) != 0;

    private short _rfrRaw;
    public short RFR_Raw { get => _rfrRaw; private set { _rfrRaw = value; OnPropertyChanged(); OnPropertyChanged(nameof(RfrHz)); } }
    public double RfrHz => RFR_Raw / 10.0;

    private ushort _ulnRaw;
    public ushort ULN_Raw { get => _ulnRaw; private set { _ulnRaw = value; OnPropertyChanged(); OnPropertyChanged(nameof(UlnV)); } }
    public double UlnV => ULN_Raw / 10.0;

    private ushort _vbusRaw;
    public ushort VBUS_Raw { get => _vbusRaw; private set { _vbusRaw = value; OnPropertyChanged(); OnPropertyChanged(nameof(VbusV)); } }
    public double VbusV => VBUS_Raw / 10.0;

    private ushort _crcRaw;
    public ushort CRC_Raw { get => _crcRaw; private set { _crcRaw = value; OnPropertyChanged(); OnPropertyChanged(nameof(CrcDecoded)); OnPropertyChanged(nameof(CanWriteSetpoint)); } }

    private ushort _cccRaw;
    public ushort CCC_Raw { get => _cccRaw; private set { _cccRaw = value; OnPropertyChanged(); OnPropertyChanged(nameof(CccDecoded)); } }

    public string CrcDecoded => DecodeBits("CRC", CRC_Raw);
    public string CccDecoded => DecodeBits("CCC", CCC_Raw);

    public bool CanWriteSetpoint
    {
        get
        {
            bool modbus = (CRC_Raw & (1 << 3)) != 0;
            bool eth = (CRC_Raw & (1 << 11)) != 0;
            return EnableControl && (modbus || eth);
        }
    }

    public ICommand RunCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand WriteSetpointCommand { get; }
    public ICommand PreparePcRefCommand { get; }

    // Diagnostics
    public ObservableCollection<FaultItem> FaultHistory { get; } = new();

    private ushort _lastFault;
    public ushort LastFault { get => _lastFault; private set { _lastFault = value; OnPropertyChanged(); } }

    public ICommand RefreshDiagnosticsCommand { get; }

    // Trend
    public ObservableCollection<TrendSeries> TrendSeries { get; } = new();
    public ICommand StartTrendCommand { get; }
    public ICommand StopTrendCommand { get; }

    // Project
    public Dictionary<int, ushort> ProjectSnapshot { get; private set; } = new();
    public ObservableCollection<DiffRow> DiffRows { get; } = new();

    public ICommand BackupCommand { get; }
    public ICommand SaveProjectCommand { get; }
    public ICommand LoadProjectCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand RestoreDiffCommand { get; }

    private void BuildTree()
    {
        Tree.Clear();
        var groups = _spec.Parameters.GroupBy(p => (p.Category ?? "").Trim()).OrderBy(g => g.Key);
        foreach (var cat in groups)
        {
            var catNode = new TreeNode(cat.Key);
            var menus = cat.GroupBy(p => (p.Menu ?? "").Trim()).OrderBy(g => g.Key);
            foreach (var menu in menus)
                catNode.Children.Add(new TreeNode(menu.Key) { Tag = menu.ToList() });
            Tree.Add(catNode);
        }
    }

    private void ApplyFilter()
    {
        ParameterRows.Clear();
        IEnumerable<ParameterDefinition> list;
        if (SelectedNode?.Tag is List<ParameterDefinition> defs) list = defs; else list = _spec.Parameters;

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var s = Search.Trim();
            list = list.Where(p => (p.Code?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false) ||
                                   (p.Name?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var p in list.OrderBy(p => p.Address))
            ParameterRows.Add(new ParameterRowViewModel(p));
    }

    private async Task ConnectAsync()
    {
        try
        {
            Status = "Connecting...";
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _client.ConnectAsync(Ip, Port, cts.Token);
            Connected = true;
            Status = "Connected";
            StartPolling();
            await RefreshDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            Connected = false;
            Status = "Connect failed: " + ex.Message;
            MessageBox.Show(ex.Message, "Connect error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Disconnect()
    {
        StopTrend();
        StopPolling();
        _client.Disconnect();
        Connected = false;
        Status = "Disconnected";
    }

    private void StartPolling()
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_pollCts!.IsCancellationRequested)
            {
                try
                {
                    ETA_Raw = (await _client.ReadHoldingRegistersAsync(UnitId, Xd4000CoreRegs.ETA, 1, _pollCts.Token))[0];
                    RFR_Raw = unchecked((short)(await _client.ReadHoldingRegistersAsync(UnitId, Xd4000CoreRegs.RFR, 1, _pollCts.Token))[0]);
                    ULN_Raw = (await _client.ReadHoldingRegistersAsync(UnitId, Xd4000CoreRegs.ULN, 1, _pollCts.Token))[0];
                    VBUS_Raw = (await _client.ReadHoldingRegistersAsync(UnitId, Xd4000CoreRegs.VBUS, 1, _pollCts.Token))[0];
                    CRC_Raw = (await _client.ReadHoldingRegistersAsync(UnitId, Xd4000CoreRegs.CRC, 1, _pollCts.Token))[0];
                    CCC_Raw = (await _client.ReadHoldingRegistersAsync(UnitId, Xd4000CoreRegs.CCC, 1, _pollCts.Token))[0];
                }
                catch { }

                await Task.Delay(500);
            }
        });
    }

    private void StopPolling()
    {
        try { _pollCts?.Cancel(); } catch { }
        _pollCts = null;
    }

    private async Task ReadCurrentGroupAsync()
    {
        Status = "Reading group...";
        foreach (var row in ParameterRows)
        {
            try
            {
                var val = (await _client.ReadHoldingRegistersAsync(UnitId, (ushort)row.Address, 1, CancellationToken.None))[0];
                row.Raw = val;
            }
            catch { }
        }
        Status = "Group read complete";
    }

    private async Task WriteSelectedAsync()
    {
        if (SelectedRow == null) return;
        if (!SelectedRow.IsWritable)
        {
            MessageBox.Show("Selected parameter is not writable.", "Write", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedRow.Edit))
        {
            MessageBox.Show("Enter value in the Edit column.", "Write", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!double.TryParse(SelectedRow.Edit, out var eng))
        {
            MessageBox.Show("Invalid number.", "Write", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        double scale = SelectedRow.Scale;
        int rawInt = (int)Math.Round(eng / (scale == 0 ? 1 : scale));
        ushort raw = unchecked((ushort)rawInt);
        await _client.WriteSingleRegisterAsync(UnitId, (ushort)SelectedRow.Address, raw, CancellationToken.None);
        SelectedRow.Raw = raw;
        Status = $"Wrote {SelectedRow.Code}";
    }

    private async Task PulseCmdBitAsync(int bit)
    {
        if (!EnableControl)
        {
            MessageBox.Show("Enable Control first.", "Control", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ushort cmd = (await _client.ReadHoldingRegistersAsync(UnitId, Xd4000CoreRegs.CMD, 1, CancellationToken.None))[0];
        cmd = (ushort)(cmd | (1 << bit));
        await _client.WriteSingleRegisterAsync(UnitId, Xd4000CoreRegs.CMD, cmd, CancellationToken.None);
    }

    private async Task WriteLfrAsync()
    {
        if (!CanWriteSetpoint)
        {
            MessageBox.Show("Setpoint write disabled. Check CRC and Enable Control.", "Setpoint", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        short raw = (short)Math.Round(LfrHz * 10.0);
        await _client.WriteSingleRegisterAsync(UnitId, Xd4000CoreRegs.LFR, unchecked((ushort)raw), CancellationToken.None);
    }

    private async Task PreparePcRefAsync()
    {
        if (IsRunning)
        {
            MessageBox.Show("Drive is RUNNING. Stop the drive to apply FR1 change (policy).", "Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await _client.WriteSingleRegisterAsync(UnitId, Xd4000CoreRegs.FR1, Xd4000CoreRegs.CNL_PC_TOOL, CancellationToken.None);
        MessageBox.Show("FR1 set to PC tool. If active reference does not change, adjust RFC/RCB manually.", "Done", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RefreshDiagnosticsAsync()
    {
        FaultHistory.Clear();
        LastFault = (await _client.ReadHoldingRegistersAsync(UnitId, Xd4000CoreRegs.LFT, 1, CancellationToken.None))[0];
        for (int i = 0; i < Xd4000CoreRegs.FaultHistory.Length; i++)
        {
            var addr = Xd4000CoreRegs.FaultHistory[i];
            var code = (await _client.ReadHoldingRegistersAsync(UnitId, addr, 1, CancellationToken.None))[0];
            FaultHistory.Add(new FaultItem { Index = i, Register = addr, Code = code });
        }
    }

    private void StartTrend()
    {
        if (_trendTimer != null) return;
        if (TrendSeries.Count == 0)
        {
            TrendSeries.Add(new TrendSeries("RFR(Hz)", () => RfrHz));
            TrendSeries.Add(new TrendSeries("VBUS(V)", () => VbusV));
        }
        _trendTimer = new System.Timers.Timer(200);
        _trendTimer.Elapsed += (_, __) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var s in TrendSeries) s.AddPoint();
            });
        };
        _trendTimer.Start();
    }

    private void StopTrend()
    {
        if (_trendTimer == null) return;
        _trendTimer.Stop();
        _trendTimer.Dispose();
        _trendTimer = null;
    }

    private async Task BackupCurrentGroupAsync()
    {
        var snap = new Dictionary<int, ushort>();
        foreach (var row in ParameterRows)
        {
            var val = (await _client.ReadHoldingRegistersAsync(UnitId, (ushort)row.Address, 1, CancellationToken.None))[0];
            snap[row.Address] = val;
        }
        ProjectSnapshot = snap;
        Status = "Backup done";
    }

    private void SaveProject()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "xD4000 Project (*.json)|*.json", FileName = "xd4000_project.json" };
        if (dlg.ShowDialog() != true) return;
        var payload = new ProjectFile { Version = _spec.Version, Ip = Ip, Port = Port, UnitId = UnitId, Snapshot = ProjectSnapshot };
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void LoadProject()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "xD4000 Project (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        var payload = JsonSerializer.Deserialize<ProjectFile>(File.ReadAllText(dlg.FileName));
        if (payload == null) return;
        Ip = payload.Ip;
        Port = payload.Port;
        UnitId = payload.UnitId;
        ProjectSnapshot = payload.Snapshot ?? new();
        Status = "Project loaded";
    }

    private async Task CompareWithDriveAsync()
    {
        DiffRows.Clear();
        foreach (var kv in ProjectSnapshot)
        {
            var addr = (ushort)kv.Key;
            var driveVal = (await _client.ReadHoldingRegistersAsync(UnitId, addr, 1, CancellationToken.None))[0];
            if (driveVal != kv.Value)
            {
                var def = _spec.Parameters.FirstOrDefault(p => p.Address == kv.Key);
                DiffRows.Add(new DiffRow { Address = kv.Key, Code = def?.Code ?? "", Name = def?.Name ?? "", ProjectValue = kv.Value, DriveValue = driveVal });
            }
        }
        Status = $"Compare complete. Diffs: {DiffRows.Count}";
    }

    private async Task RestoreDiffAsync()
    {
        if (IsRunning)
        {
            MessageBox.Show("Restore is blocked while RUNNING (policy).", "Blocked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var res = MessageBox.Show($"Write {DiffRows.Count} differences to drive?", "Restore", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;
        foreach (var d in DiffRows)
        {
            await _client.WriteSingleRegisterAsync(UnitId, (ushort)d.Address, d.ProjectValue, CancellationToken.None);
        }
        await CompareWithDriveAsync();
    }

    private string DecodeBits(string reg, ushort value)
    {
        if (!_spec.RegisterBits.TryGetValue(reg, out var bits) || bits.Count == 0) return value.ToString();
        var active = bits.Where(b => (value & (1 << b.Bit)) != 0).Select(b => $"b{b.Bit}:{b.Text}");
        return string.Join(" | ", active);
    }
}

public sealed class TreeNode
{
    public TreeNode(string name) { Name = name; }
    public string Name { get; }
    public ObservableCollection<TreeNode> Children { get; } = new();
    public object? Tag { get; set; }
}

public sealed class FaultItem
{
    public int Index { get; set; }
    public int Register { get; set; }
    public ushort Code { get; set; }
}

public sealed class TrendSeries : ViewModelBase
{
    private readonly Func<double> _get;
    private const int MaxPoints = 200;

    public TrendSeries(string name, Func<double> get) { Name = name; _get = get; }

    public string Name { get; }
    public ObservableCollection<double> Points { get; } = new();

    public void AddPoint()
    {
        Points.Add(_get());
        while (Points.Count > MaxPoints) Points.RemoveAt(0);
        OnPropertyChanged(nameof(Points));
    }
}

public sealed class ProjectFile
{
    public string Version { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; }
    public byte UnitId { get; set; }
    public Dictionary<int, ushort>? Snapshot { get; set; }
}

public sealed class DiffRow
{
    public int Address { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public ushort ProjectValue { get; set; }
    public ushort DriveValue { get; set; }
}
