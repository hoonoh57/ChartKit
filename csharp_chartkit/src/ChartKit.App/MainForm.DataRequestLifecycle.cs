using ChartKit.CSharp.Contracts;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private readonly LatestRequestCoordinator _dataRequestCoordinator = new();
    private int _activeDataCommandCount;
    private long _dataStreamGeneration;
    private bool _dataRequestLifecycleInstalled;

    private void InstallDataRequestLifecycle()
    {
        if (_dataRequestLifecycleInstalled) return;
        _dataRequestLifecycleInstalled = true;

        Shown -= OnShown;
        Shown += OnDataControlsShown;
        FormClosing += OnDataControlsFormClosing;
    }

    private async void OnDataControlsShown(object? sender, EventArgs e)
    {
        await ExecuteDataCommandAsync(
            () => ReloadActiveSymbolsAsync(
                _workspace.Timeframe,
                reloadAll: true));
        if (!IsDisposed) _chart.Focus();
    }

    private void OnDataControlsFormClosing(
        object? sender,
        FormClosingEventArgs e)
    {
        Interlocked.Increment(ref _dataStreamGeneration);
        _streamStop?.Cancel();
        _dataRequestCoordinator.Dispose();
    }

    private LatestRequestCoordinator.RequestLease BeginDataRequest(
        bool cancelStream)
    {
        LatestRequestCoordinator.RequestLease request =
            _dataRequestCoordinator.Begin(_stop.Token);
        if (cancelStream || request.ReplacedCurrent)
        {
            Interlocked.Increment(ref _dataStreamGeneration);
            _streamStop?.Cancel();
        }
        return request;
    }

    private long BeginDataStreamGeneration() =>
        Interlocked.Increment(ref _dataStreamGeneration);

    private bool IsCurrentDataStream(long generation) =>
        generation == Volatile.Read(ref _dataStreamGeneration) &&
        !_stop.IsCancellationRequested;

    private async Task RefreshMetadataForSymbolAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        if (_metadataCache.TryGetValue(symbol, out InstrumentMetadata? cached))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(_selectedSymbol, symbol, StringComparison.Ordinal))
                _selectedMetadata = cached;
            return;
        }

        InstrumentMetadata metadata;
        if (_source is IInstrumentMetadataSource metadataSource)
        {
            try
            {
                metadata = await metadataSource.GetInstrumentMetadataAsync(
                    symbol,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                metadata = CreateFallbackMetadata(symbol);
            }
        }
        else
        {
            metadata = CreateFallbackMetadata(symbol);
        }

        cancellationToken.ThrowIfCancellationRequested();
        _metadataCache[symbol] = metadata;
        if (string.Equals(_selectedSymbol, symbol, StringComparison.Ordinal))
            _selectedMetadata = metadata;
    }
}
