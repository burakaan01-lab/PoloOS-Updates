namespace PoloOS.Diagnostics.Core;

public sealed class KwpCommon
{
    private readonly FtdiD2xxTransport _transport;
    private readonly DiagLog _log;
    public KwpCommon(FtdiD2xxTransport transport, DiagLog log) { _transport = transport; _log = log; }
    public WakeResult WakeUp(byte controllerAddress, bool evenParity, int bitMilliseconds)
    {
        _transport.FiveBaudInit(controllerAddress, evenParity, bitMilliseconds);
        var sync = _transport.ReadByte(2000);
        if (sync != 0x55) throw new InvalidOperationException($"Unexpected sync byte {sync:X2}, expected 55.");
        var keyLsb = _transport.ReadByte(1000);
        var keyMsb = _transport.ReadByte(1000);
        var protocol = ((keyMsb & 0x7F) << 7) + (keyLsb & 0x7F);
        PreciseDelay.Milliseconds(25);
        WriteByte((byte)~keyMsb);
        if (protocol >= 2000) { var adjusted = FtdiD2xxTransport.AdjustParity(controllerAddress, evenParity); ReadComplement(adjusted); }
        _log.Write($"WAKE_OK addr={controllerAddress:X2} KW={protocol} key={keyLsb:X2} {keyMsb:X2} parity={(evenParity ? "EVEN" : "ODD")} bitMs={bitMilliseconds}");
        return new WakeResult(protocol, keyLsb, keyMsb, evenParity, bitMilliseconds);
    }
    public byte ReadByte(int? timeoutMs = null) => _transport.ReadByte(timeoutMs);
    public void WriteByte(byte value) => _ = _transport.WriteByteAndDiscardEcho(value);
    public void ReadComplement(byte value)
    {
        var expected = (byte)~value;
        var actual = ReadByte(1800);
        if (actual != expected) throw new InvalidOperationException($"Complement mismatch. Expected {expected:X2}, got {actual:X2}.");
    }
}
