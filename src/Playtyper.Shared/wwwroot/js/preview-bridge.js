// Bridges pack-draft data across the iframe boundary used by the live
// preview — see PreviewAdapter.cs's class doc comment for why there is an
// iframe (and therefore a message bridge) at all instead of a direct call.
//
// Payloads cross as JSON STRINGS, not live objects: postMessage's structured
// clone would happily carry a plain object too, but a pack draft's object
// graph (PackConfig + every activity in every language) can get large and
// deeply nested, and Blazor's JS interop has to marshal whatever crosses the
// C#↔JS boundary on BOTH ends of this trip (parent .NET → parent JS, and
// child JS → child .NET) regardless of what postMessage itself could carry.
// A string is the one shape that is trivially cheap and unambiguous to
// marshal on all three hops, and the existing PreviewSyncPayload record
// already has a System.Text.Json-friendly shape via PackConfig/Activity's
// own [JsonPropertyName] attributes, so serializing once on the C# side and
// deserializing once on the other is less code than teaching the JS layer
// to reconstruct a typed object.
window.playtyperPreviewBridge = {
    // ── Parent side (PreviewPanel.razor, main document) ────────────────────

    // Starts listening for the child's "ready" handshake. Fires once, the
    // first time the iframe's own Blazor instance has finished booting and
    // attached its own listener — see listenForData below. Until this
    // fires, sendToChild would be shouting into an iframe with nobody home,
    // which is exactly why PreviewPanel waits for it before sending the
    // first payload rather than sending unconditionally on every render.
    listenForReady: function (iframeEl, dotNetRef) {
        function handler(e) {
            if (e.source !== iframeEl.contentWindow) return;
            if (!e.data || e.data.type !== 'playtyper-preview-ready') return;
            dotNetRef.invokeMethodAsync('OnChildReady');
        }
        window.addEventListener('message', handler);
        iframeEl._playtyperReadyHandler = handler;
    },

    stopListeningForReady: function (iframeEl) {
        if (iframeEl && iframeEl._playtyperReadyHandler) {
            window.removeEventListener('message', iframeEl._playtyperReadyHandler);
            iframeEl._playtyperReadyHandler = null;
        }
    },

    sendToChild: function (iframeEl, json) {
        if (!iframeEl || !iframeEl.contentWindow) return;
        iframeEl.contentWindow.postMessage({ type: 'playtyper-preview-data', json: json }, window.location.origin);
    },

    // ── Child side (PreviewFramePage.razor, inside the iframe) ─────────────

    // Registers the data listener, then immediately announces readiness to
    // the parent. Order matters: announce AFTER the listener is attached, or
    // a parent that was already waiting could send its first payload into a
    // gap with nothing listening yet.
    listenForData: function (dotNetRef) {
        window.addEventListener('message', function (e) {
            if (e.source !== window.parent) return;
            if (!e.data || e.data.type !== 'playtyper-preview-data') return;
            dotNetRef.invokeMethodAsync('OnDataReceived', e.data.json);
        });
        window.parent.postMessage({ type: 'playtyper-preview-ready' }, window.location.origin);
    },

    // Pulls in Playtypus.Core's own stylesheets/scripts dynamically, INTO
    // THIS DOCUMENT ONLY (this function only ever runs inside the preview
    // iframe's own separate document — see PreviewFramePage.razor). Not
    // static <link>/<script> tags in index.html: index.html is shared by
    // every route this Blazor app has, including the ones the PARENT
    // document uses for Playtyper's own chrome, and loading Playtypus.Core's
    // CSS there too would recreate the exact leak this whole iframe exists
    // to avoid.
    //
    // Returns a Promise that resolves once the JS files have loaded
    // (resolves on error too, rather than hanging forever, if one file
    // 404s) — PreviewFramePage awaits this before rendering <AppShell>,
    // since AppShell's first render calls straight into window.playtypusTheme
    // and friends. Stylesheets are NOT awaited the same way: a brief flash
    // of unstyled content while a <link> loads is a normal, harmless
    // browser moment, not a broken function call.
    injectPlaytypusAssets: function () {
        var head = document.head;

        ['ui.css', 'ui-variants.css', 'detail-zones.css', 'mosaic.css', 'gallery.css',
         'feel.css', 'premium.css', 'premium-desktop.css', 'v14-additions.css', 'v18-additions.css']
            .forEach(function (f) {
                var link = document.createElement('link');
                link.rel = 'stylesheet';
                link.href = '_content/Playtypus.Core/css/' + f;
                head.appendChild(link);
            });

        // Base app font — matches Playtypus.Web/wwwroot/index.html. Pack-
        // specific typography (TypographyConfig) is layered on top of this
        // by ThemeService.ApplyTypographyAsync itself, same as production.
        var pre1 = document.createElement('link'); pre1.rel = 'preconnect'; pre1.href = 'https://fonts.googleapis.com';
        var pre2 = document.createElement('link'); pre2.rel = 'preconnect'; pre2.href = 'https://fonts.gstatic.com'; pre2.crossOrigin = 'anonymous';
        var font = document.createElement('link'); font.rel = 'stylesheet';
        font.href = 'https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&display=swap';
        head.appendChild(pre1); head.appendChild(pre2); head.appendChild(font);

        return new Promise(function (resolve) {
            var files = ['playtypus.js', 'playtypus-gallery.js', 'playtypus-map.js', 'playtypus-social.js'];
            var remaining = files.length;
            function done() { remaining--; if (remaining <= 0) resolve(); }
            files.forEach(function (f) {
                var script = document.createElement('script');
                script.src = '_content/Playtypus.Core/js/' + f;
                script.onload = done;
                script.onerror = done;
                head.appendChild(script);
            });
        });
    }
};
