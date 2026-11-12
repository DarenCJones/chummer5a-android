// SPDX-License-Identifier: GPL-3.0-or-later
using Android.OS;
using Android.Print;
using Android.Webkit;

namespace Chummer.AndroidApp;

internal sealed class SummaryPageClient(Action ready) : WebViewClient
{
    private bool started;
    public override void OnPageFinished(WebView? view, string? url)
    {
        if (started) return;
        started = true; ready();
    }
}

internal sealed class SummaryPrintAdapter(PrintDocumentAdapter inner, Action finished) : PrintDocumentAdapter
{
    public override void OnStart() => inner.OnStart();
    public override void OnLayout(PrintAttributes? oldAttributes, PrintAttributes? newAttributes,
        CancellationSignal? cancellationSignal, LayoutResultCallback? callback, Bundle? extras) =>
        inner.OnLayout(oldAttributes, newAttributes, cancellationSignal, callback, extras);
    public override void OnWrite(PageRange[]? pages, ParcelFileDescriptor? destination,
        CancellationSignal? cancellationSignal, WriteResultCallback? callback) =>
        inner.OnWrite(pages, destination, cancellationSignal, callback);
    public override void OnFinish()
    {
        try { inner.OnFinish(); } finally { finished(); }
    }
}
