namespace PoloOS.Diagnostics.Core;

public sealed class Kw1281ReadOnlySession : IDisposable
{
    private readonly int _deviceIndex;
    private readonly ModuleSpec _module;
    private readonly DiagLog _log;
    private FtdiD2xxTransport? _transport;
    private Kw1281Dialog? _dialog;
    public string Identification { get; private set; } = string.Empty;
    public bool IsConnected => _transport is not null && _dialog is not null;
    public Kw1281ReadOnlySession(int deviceIndex, ModuleSpec module, DiagLog log) { _deviceIndex=deviceIndex; _module=module; _log=log; }
    public void Open()
    {
        Close();
        var candidates = _module.Address == 0x01 || _module.Address is 0x17 or 0x25 ? new[] {195,200} : new[] {200,195};
        Exception? last=null;
        foreach (var bitMs in candidates)
        for (var retry=1; retry<=3; retry++)
        {
            try
            {
                var transport=new FtdiD2xxTransport(_deviceIndex,_log); transport.Open(_module.Baud);
                var common=new KwpCommon(transport,_log);
                var wake=common.WakeUp(_module.Address,false,bitMs);
                if (wake.KeywordVersion!=1281) { transport.Dispose(); throw new InvalidOperationException($"Module {_module.Address:X2} returned KW{wake.KeywordVersion}; KW1281 required."); }
                var dialog=new Kw1281Dialog(common,_log); var startup=dialog.Connect();
                Identification=string.Join(" | ",startup.Where(b=>b.Title==0xF6&&!string.IsNullOrWhiteSpace(b.Ascii)).Select(b=>b.Ascii).Distinct());
                _transport=transport; _dialog=dialog; _log.Write($"KW1281_SESSION_OPEN addr={_module.Address:X2} bitMs={bitMs} ident={Identification}"); return;
            }
            catch(Exception ex) { last=ex; _log.Write($"KW1281_SESSION_RETRY addr={_module.Address:X2} bitMs={bitMs} retry={retry}/3 reason={ex.Message}"); Close(); Thread.Sleep(650); }
        }
        throw new InvalidOperationException($"KW1281 session {_module.Address:X2} could not open.",last);
    }
    public Kw1281GroupSample? ReadGroup(byte group) => _dialog is null ? throw new InvalidOperationException("KW1281 session is not open.") : _dialog.ReadGroup(group);
    public void Close() { try { _transport?.Dispose(); } catch {} _dialog=null; _transport=null; Identification=string.Empty; }
    public void Dispose()=>Close();
}
