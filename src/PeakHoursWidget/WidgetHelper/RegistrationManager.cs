using System;
using System.Threading;
using Microsoft.Windows.Widgets.Providers;

namespace PeakHoursWidget.WidgetHelper;

public sealed class RegistrationManager<TWidgetProvider> : IDisposable
    where TWidgetProvider : IWidgetProvider, new()
{
    private bool _disposed;
    private readonly ManualResetEvent _disposedEvent = new(false);
    private readonly IDisposable _registeredProvider;

    private RegistrationManager(IDisposable provider) => _registeredProvider = provider;

    public static RegistrationManager<TWidgetProvider> RegisterProvider()
    {
        var registration = RegisterClass(typeof(TWidgetProvider).GUID, new WidgetProviderFactory<TWidgetProvider>());
        return new RegistrationManager<TWidgetProvider>(registration);
    }

    private static IDisposable RegisterClass(Guid clsid, Com.IClassFactory factory)
    {
        uint registrationHandle;
        Com.ClassObject.Register(clsid, factory, out registrationHandle);
        return new ClassLifetimeUnregister(registrationHandle);
    }

    public ManualResetEvent GetDisposedEvent() => _disposedEvent;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _registeredProvider.Dispose();
        _disposed = true;
        _disposedEvent.Set();
        GC.SuppressFinalize(this);
    }

    ~RegistrationManager() => Dispose();

    private sealed class ClassLifetimeUnregister : IDisposable
    {
        private readonly uint _registrationHandle;

        public ClassLifetimeUnregister(uint registrationHandle) => _registrationHandle = registrationHandle;

        public void Dispose() => Com.ClassObject.Revoke(_registrationHandle);
    }
}
