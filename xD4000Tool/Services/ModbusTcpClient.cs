using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.SemaphoreSlim;

namespace xD4000Tool.Services;

/// <summary>
/// Minimal Modbus TCP client (FC03 Read Holding Registers, FC06 Write Single Register).
///
/// Key design choice:
/// - Serialize all requests on the TCP stream using a SemaphoreSlim to avoid
///   response/request interleaving and Transaction-ID mismatches.
///   Modbus/TCP uses the MBAP Transaction Identifier for request/response pairing,
///   and the server must echo it back in the response.
/// </summary>
public sealed class ModbusTcpClient : IDisposable
{
    private TcpClient? _tcp;
    private NetworkStream? _stream;

    // Transaction ID is 16-bit and should be echoed by server in response.
    // We start at 0, but we always increment before use so first request uses 1.
    private ushort _txId;

    // Ensures only one Modbus request is in-flight at a time.
    private readonly SemaphoreSlim _ioLock = new(1, 1);

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

    public void Dispose() => Disconnect();

    /// <summary>
    /// Reads holding registers (FC03). Address is Modbus register offset (0-based).
    /// </summary>
    public async Task<ushort[]> ReadHoldingRegistersAsync(byte unitId, ushort address, ushort count, CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected");
        if (count < 1 || count > 125) throw new ArgumentOutOfRangeException(nameof(count), "Valid range 1..125 for FC03");

        await _ioLock.WaitAsync(ct);
        try
        {
            ushort tx = NextTxId();

            // MBAP (7 bytes) + PDU (5 bytes for FC03 request) = 12 bytes
            // MBAP: [TxId(2)] [Proto(2)=0] [Len(2)=6] [Unit(1)]
            // PDU : [FC(1)=3] [Addr(2)] [Count(2)]
            byte[] req = new byte[12];
            WriteU16BE(req, 0, tx);          // Transaction ID
            WriteU16BE(req, 2, 0);           // Protocol ID = 0 for Modbus/TCP
            WriteU16BE(req, 4, 6);           // Length = UnitId(1) + PDU(5)
            req[6] = unitId;                 // Unit ID
            req[7] = 0x03;                   // FC03
            WriteU16BE(req, 8, address);
            WriteU16BE(req, 10, count);

            await _stream.WriteAsync(req, 0, req.Length, ct);

            // Response header: MBAP(7) + FC(1) + ByteCount(1) = 9 bytes
            byte[] header = new byte[9];
            await ReadExactlyAsync(_stream, header, ct);

            ValidateMbap(tx, unitId, header);

            byte func = header[7];
            if ((func & 0x80) != 0)
            {
                byte ex = header[8];
                throw new InvalidOperationException($"Modbus exception {ex} (FC={func & 0x7F})");
            }

            byte byteCount = header[8];
            if (byteCount != count * 2)
            {
                // Some devices may return fewer bytes; treat as error for safety.
                throw new InvalidOperationException($"Unexpected byte count. Expected {count * 2}, got {byteCount}");
            }

            byte[] data = new byte[byteCount];
            await ReadExactlyAsync(_stream, data, ct);

            ushort[] regs = new ushort[count];
            for (int i = 0; i < count; i++)
                regs[i] = ReadU16BE(data, i * 2);

            return regs;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    /// <summary>
    /// Writes a single holding register (FC06).
    /// </summary>
    public async Task WriteSingleRegisterAsync(byte unitId, ushort address, ushort value, CancellationToken ct)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected");

        await _ioLock.WaitAsync(ct);
        try
        {
            ushort tx = NextTxId();

            // MBAP(7) + PDU(5) = 12 bytes
            // PDU: FC06 + Addr(2) + Value(2)
            byte[] req = new byte[12];
            WriteU16BE(req, 0, tx);
            WriteU16BE(req, 2, 0);
            WriteU16BE(req, 4, 6);
            req[6] = unitId;
            req[7] = 0x06;
            WriteU16BE(req, 8, address);
            WriteU16BE(req, 10, value);

            await _stream.WriteAsync(req, 0, req.Length, ct);

            // Response for FC06 is MBAP(7) + PDU(5) = 12 bytes
            byte[] resp = new byte[12];
            await ReadExactlyAsync(_stream, resp, ct);

            ValidateMbap(tx, unitId, resp);

            byte func = resp[7];
            if ((func & 0x80) != 0)
            {
                byte ex = resp[8];
                throw new InvalidOperationException($"Modbus exception {ex} (FC={func & 0x7F})");
            }

            // Optional echo validation:
            ushort echoAddr = ReadU16BE(resp, 8);
            ushort echoVal  = ReadU16BE(resp, 10);

            if (echoAddr != address || echoVal != value)
            {
                throw new InvalidOperationException($"FC06 echo mismatch. Addr {echoAddr}!= {address} or Val {echoVal}!= {value}");
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    // -------------------------
    // Helpers
    // -------------------------

    private ushort NextTxId()
    {
        // Always non-zero first request: 1,2,3... rollover allowed.
        unchecked { _txId++; }
        if (_txId == 0) unchecked { _txId++; } // avoid 0 after rollover
        return _txId;
    }

    private static void ValidateMbap(ushort expectedTx, byte expectedUnitId, byte[] mbapPlus)
    {
        // mbapPlus contains at least first 7 bytes MBAP
        ushort rxTx = ReadU16BE(mbapPlus, 0);
        ushort proto = ReadU16BE(mbapPlus, 2);

        if (proto != 0)
            throw new InvalidOperationException($"Protocol ID != 0 (got {proto})");

        // Unit ID is echoed by server per spec when relevant (e.g., gateways).
        // Some pure TCP devices ignore UnitId semantics, but they still echo it.
        byte rxUnit = mbapPlus[6];

        if (rxTx != expectedTx)
            throw new InvalidOperationException("Transaction mismatch");

        if (rxUnit != expectedUnitId)
        {
            // If your device ignores UnitId, you can relax this check.
            // For now, keep strict to prevent mixing responses from another UnitId.
            throw new InvalidOperationException($"UnitId mismatch. Expected {expectedUnitId}, got {rxUnit}");
        }
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
}
