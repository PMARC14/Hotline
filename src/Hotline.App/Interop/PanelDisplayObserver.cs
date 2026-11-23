using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Hotline.App.Interop;

/// <summary>Coalesces display topology and XAML raster-scale notifications on the window dispatcher.</summary>
internal sealed class PanelDisplayObserver : IDisposable
{
    private readonly FrameworkElement _root;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _changed;
    private readonly DisplayAreaWatcher _watcher = DisplayArea.CreateWatcher();
    private XamlRoot? _xamlRoot;
    private bool _queued;
    private bool _disposed;

    public PanelDisplayObserver(FrameworkElement root, DispatcherQueue dispatcher, Action changed)
    {
        (_root, _dispatcher, _changed) = (root, dispatcher, changed);
        _root.Loaded += Loaded;
        _watcher.Added += DisplayChanged;
        _watcher.Removed += DisplayChanged;
        _watcher.Updated += DisplayChanged;
        AttachRoot();
        _watcher.Start();
    }

    private void Loaded(object sender, RoutedEventArgs e) { AttachRoot(); Queue(); }
    private void AttachRoot()
    {
        if (_xamlRoot == _root.XamlRoot) return;
        if (_xamlRoot is not null) _xamlRoot.Changed -= RootChanged;
        _xamlRoot = _root.XamlRoot;
        if (_xamlRoot is not null) _xamlRoot.Changed += RootChanged;
    }
    private void DisplayChanged(DisplayAreaWatcher sender, object args) => Queue();
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Queue();
    private void Queue()
    {
        if (_disposed || _queued) return;
        _queued = true;
        if (!_dispatcher.TryEnqueue(() => { _queued = false; if (!_disposed) _changed(); })) _queued = false;
    }
    public void Dispose()
    {
        _disposed = true;
        _watcher.Stop();
        _watcher.Added -= DisplayChanged;
        _watcher.Removed -= DisplayChanged;
        _watcher.Updated -= DisplayChanged;
        _root.Loaded -= Loaded;
        if (_xamlRoot is not null) _xamlRoot.Changed -= RootChanged;
    }
}
