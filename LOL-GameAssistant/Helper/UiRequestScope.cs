namespace LOL_GameAssistant.Helper;

/// <summary>UI 线程使用的请求范围。新请求取消旧请求，关闭取消全部；任务自己释放令牌源。</summary>
internal sealed class UiRequestScope : IDisposable
{
    private Request? _current;
    private bool _disposed;
    private readonly CancellationTokenSource _lifetime = new();
    internal UiRequestScope() => Token = _lifetime.Token;
    internal CancellationToken Token { get; }
    internal Request Begin()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _current?.Cancel();
        return _current = new Request(this);
    }
    internal void Cancel() { _current?.Cancel(); _current = null; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        Cancel();
        _lifetime.Dispose();
    }
    internal sealed class Request : IDisposable
    {
        private readonly UiRequestScope _owner;
        private readonly CancellationTokenSource _source;
        internal Request(UiRequestScope owner) { _owner = owner; _source = CancellationTokenSource.CreateLinkedTokenSource(owner.Token); Token = _source.Token; }
        internal CancellationToken Token { get; }
        internal bool IsCurrent => !_owner._disposed && ReferenceEquals(_owner._current, this) && !Token.IsCancellationRequested;
        internal void Cancel() => _source.Cancel();
        public void Dispose()
        {
            if (ReferenceEquals(_owner._current, this)) _owner._current = null;
            _source.Dispose();
        }
    }
}
