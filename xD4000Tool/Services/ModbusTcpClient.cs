using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace xD4000Tool.Services;

public sealed class ModbusTcpClient : IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;
    private ushort _txId;

    public bool IsConnected => _tcp?.Connected == true;

    public async Task ConnectAsync(string ip, int port, CancellationToken ct)
    {
        Disconnect();
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(ip, port, ct);
        _stream = _tcp.GetStream();
        _stream.ReadTimeout = 4000;
        _stream.WriteTimeout = 4000;
        _txId = 0;
    }

    public void Disconnect()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Close(); } catch { }
        _stream = null;
        _tcp = null;
    }

    public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort address, ushort count, CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected");
        if (count < 1 || count > 125) throw new ArgumentOutOfRangeException(nameof(count));

        ushort tx = unchecked(++_txId);
        byte[] req = new byte[12];
        WriteU16BE(req, 0, tx);
        WriteU16BE(req, 2, 0);
        WriteU16BE(req, 4, 6);
        req[6] = unitId;
        req[7] = 0x03;
        WriteU16BE(req, 8, address);
        WriteU16BE(req, 10, count);

        await _stream.WriteAsync(req, 0, req.Length, ct);

        byte[] header = new byte[9];
        await ReadExactlyAsync(_stream, header, ct);

        if (ReadU16BE(header, 0) != tx) throw new InvalidOperationException("Transaction mismatch");
        byte func = header[7];
        if ((func & 0x80) != 0) throw new InvalidOperationException($"Modbus exception {header[8]}");

        int byteCount = header[8];
        byte[] data = new byte[byteCount];
        await ReadExactlyAsync(_stream, data, ct);

        ushort[] regs = new ushort[count];
        for (int i = 0; i < count; i++) regs[i] = ReadU16BE(data, i * 2);
        return regs;
    }

    public async Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value, CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected");
        ushort tx = unchecked(++_txId);
        byte[] req = new byte[12];
        WriteU16BE(req, 0, tx);
        WriteU16BE(req, 2, 0);
        WriteU16BE(req, 4, 6);
        req[6] = unitId;
        req[7] = 0x06;
        WriteU16BE(req, 8, address);
        WriteU16BE(req, 10, value);
        await _stream.WriteAsync(req, 0, req.Length, ct);

        byte[] resp = new byte[12];
        await ReadExactlyAsync(_stream, resp, ct);
        if (resp[7] == (0x80 | 0x06)) throw new InvalidOperationException($"Modbus exception {resp[8]}");
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, ct);
            if (read == 0) throw new InvalidOperationException("Connection closed");
            offset += read;
        }
    }

    private static void WriteU16BE(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value >> 8);
        buf[offset + 1] = (byte)(value & 0xFF);
    }

    private static ushort ReadU16BE(byte[] buf, int offset)
    {
        return (ushort)((buf[offset] << 8) | buf[offset + 1]);
    }

    public void Dispose() => Disconnect();
}
