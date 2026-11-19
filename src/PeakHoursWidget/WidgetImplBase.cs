using System;
using Microsoft.Windows.Widgets.Providers;
using Windows.Storage;

namespace PeakHoursWidget;

internal delegate WidgetImplBase WidgetCreateDelegate(string widgetId, string initialState);

internal abstract class WidgetImplBase
{
    protected readonly string Id;
    protected string State;
    protected bool IsActivated;

    protected WidgetImplBase(string widgetId, string initialState)
    {
        Id = widgetId;
        State = initialState;
    }

    public string WidgetId => Id;
    public string CustomState => State;
    public bool Activated => IsActivated;

    protected static string ReadPackageFileFromUri(string packageUri)
    {
        var uri = new Uri(packageUri);
        var fileTask = StorageFile.GetFileFromApplicationUriAsync(uri).AsTask();
        fileTask.Wait();
        var textTask = FileIO.ReadTextAsync(fileTask.Result).AsTask();
        textTask.Wait();
        return textTask.Result;
    }

    public virtual void Activate(WidgetContext widgetContext) => IsActivated = true;

    public virtual void Deactivate() => IsActivated = false;

    public virtual void OnActionInvoked(WidgetActionInvokedArgs args) { }

    public virtual void OnWidgetContextChanged(WidgetContextChangedArgs args) { }

    // Subsequent updates only need data; the template is sent once on creation.
    public virtual void Refresh() => PushUpdate(includeTemplate: false);

    public void SendInitialUpdate() => PushUpdate(includeTemplate: true);

    protected void PushUpdate(bool includeTemplate)
    {
        var options = new WidgetUpdateRequestOptions(Id)
        {
            Data = GetDataForWidget(),
            CustomState = State,
        };
        if (includeTemplate)
        {
            options.Template = GetTemplateForWidget();
        }
        WidgetManager.GetDefault().UpdateWidget(options);
    }

    public abstract string GetTemplateForWidget();

    public abstract string GetDataForWidget();
}
