using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Windows.Widgets.Providers;

namespace PeakHoursWidget;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid("E8F3A6B2-5C14-4D7E-9A31-6B0F2C8D4E77")]
public sealed class WidgetProvider : IWidgetProvider
{
    // Must match the <com:Class Id> in Package.appxmanifest.
    public const string ClassId = "E8F3A6B2-5C14-4D7E-9A31-6B0F2C8D4E77";

    private static readonly object Sync = new();
    private static readonly Dictionary<string, WidgetImplBase> WidgetInstances = new();
    private static readonly Dictionary<string, WidgetCreateDelegate> WidgetImpls = new()
    {
        [PeakHoursWidgetImpl.DefinitionId] = (widgetId, state) => new PeakHoursWidgetImpl(widgetId, state),
    };

    private static Timer? _refreshTimer;
    private static bool _recovered;

    public WidgetProvider()
    {
        Diag.Log("WidgetProvider constructed.");
        RecoverRunningWidgets();
        _refreshTimer ??= new Timer(
            _ => RefreshAll(),
            null,
            TimeSpan.FromSeconds(15),
            TimeSpan.FromMinutes(10));
    }

    private static void RecoverRunningWidgets()
    {
        if (_recovered)
        {
            return;
        }

        try
        {
            var manager = WidgetManager.GetDefault();
            var infos = manager.GetWidgetInfos();
            if (infos == null)
            {
                return;
            }

            foreach (var info in infos)
            {
                var context = info.WidgetContext;
                lock (Sync)
                {
                    if (WidgetInstances.ContainsKey(context.Id))
                    {
                        continue;
                    }

                    if (WidgetImpls.TryGetValue(context.DefinitionId, out var factory))
                    {
                        WidgetInstances[context.Id] = factory(context.Id, info.CustomState ?? string.Empty);
                    }
                    else
                    {
                        manager.DeleteWidget(context.Id);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"Recovery failed: {ex.Message}");
        }
        finally
        {
            _recovered = true;
        }
    }

    private static void RefreshAll()
    {
        try
        {
            List<WidgetImplBase> snapshot;
            lock (Sync)
            {
                snapshot = new List<WidgetImplBase>(WidgetInstances.Values);
            }

            foreach (var widget in snapshot)
            {
                if (widget.Activated)
                {
                    widget.Refresh();
                }
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"Refresh failed: {ex.Message}");
        }
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        Diag.Log($"CreateWidget id={widgetContext.Id} definition={widgetContext.DefinitionId}");

        try
        {
            if (!WidgetImpls.TryGetValue(widgetContext.DefinitionId, out var factory))
            {
                throw new InvalidOperationException($"Unknown widget definition: {widgetContext.DefinitionId}");
            }

            var instance = factory(widgetContext.Id, string.Empty);
            lock (Sync)
            {
                WidgetInstances[widgetContext.Id] = instance;
            }
            instance.SendInitialUpdate();
            Diag.Log($"CreateWidget done for {widgetContext.Id}");
        }
        catch (Exception ex)
        {
            Diag.Log($"CreateWidget FAILED: {ex}");
            throw;
        }
    }

    public void DeleteWidget(string widgetId, string _)
    {
        Diag.Log($"DeleteWidget id={widgetId}");
        lock (Sync)
        {
            WidgetInstances.Remove(widgetId);
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        Diag.Log($"OnActionInvoked verb={actionInvokedArgs.Verb}");
        WidgetImplBase? instance;
        lock (Sync)
        {
            WidgetInstances.TryGetValue(actionInvokedArgs.WidgetContext.Id, out instance);
        }
        instance?.OnActionInvoked(actionInvokedArgs);
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        // The card is designed to adapt to all supported sizes; nothing to do here.
    }

    public void Activate(WidgetContext widgetContext)
    {
        Diag.Log($"Activate id={widgetContext.Id}");
        lock (Sync)
        {
            if (WidgetInstances.TryGetValue(widgetContext.Id, out var instance))
            {
                instance.Activate(widgetContext);
                instance.Refresh();
            }
        }
    }

    public void Deactivate(string widgetId)
    {
        lock (Sync)
        {
            if (WidgetInstances.TryGetValue(widgetId, out var instance))
            {
                instance.Deactivate();
            }
        }
    }
}
