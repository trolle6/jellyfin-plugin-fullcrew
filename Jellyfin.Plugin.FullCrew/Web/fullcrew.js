(function () {
    'use strict';

    /* ================================================================== */
    /* Architecture                                                       */
    /*                                                                    */
    /* One injected script (Jellyfin plugin constraint). Features live as */
    /* named sections that share Core primitives:                         */
    /*   Core.el / Core.prop / Core.getJson / Core.PageTitle / Core.Ui    */
    /*   Core.CustomPage — mount/teardown for hash-routed overlays        */
    /*   Core.navigateToItem / Core.bindPageLoad / Core.findMount         */
    /* Features: Crew accordion · Bumper/Trailer · Stats · Studio         */
    /* Route chrome (peek/apply) runs before the duplicate-script guard.  */
    /* ================================================================== */

    var PLUGIN_GUID = 'a8f3c2e1-9b4d-4f6a-8e2c-1d5b7a9c0e3f';
    var PLUGIN_VERSION = '1.5.4.0';
    var CRITICAL_STYLE_ID = 'fullCrewCritical';
    var STYLE_ID = 'fullCrewStyles';
    var ROUTE_PENDING_CLASS = 'fullCrewRoutePending';
    var STATS_BODY_CLASS = 'fullCrewStatsActive';
    var STUDIO_BODY_CLASS = 'fullCrewStudioActive';

    /**
     * Parse hash ASAP (no DOM required). Used by early chrome + routers.
     * Matches #/fullcrew/... and #!/fullcrew/...
     */
    function peekFullCrewRoute(hash) {
        var raw = (hash || (typeof location !== 'undefined' ? location.hash : '') || '').split('?')[0];
        if (raw.indexOf('#!/') === 0) {
            raw = '#' + raw.slice(2);
        }
        if (raw.indexOf('#/fullcrew/') !== 0) {
            return null;
        }
        if (raw === '#/fullcrew/stats' || raw.indexOf('#/fullcrew/stats/') === 0) {
            return { kind: 'stats', title: 'Stats', bodyClass: STATS_BODY_CLASS };
        }
        if (raw.indexOf('#/fullcrew/studio/') === 0) {
            var enc = raw.slice('#/fullcrew/studio/'.length);
            var name = enc;
            try {
                name = decodeURIComponent(enc) || enc;
            } catch (e) { /* keep enc */ }
            return { kind: 'studio', title: name || 'Studio', bodyClass: STUDIO_BODY_CLASS };
        }
        return { kind: 'other', title: 'Full Crew', bodyClass: STATS_BODY_CLASS };
    }

    /**
     * Synchronous document.title lock for Full Crew hashes.
     * MutationObserver alone is async → one paint of "Page not found" in the tab.
     * Shared via window so inline early-boot + deferred fullcrew.js share one hook.
     */
    var TitleLock = (function () {
        var existing = window.__fullCrewTitleLock;
        if (existing && existing.__fullCrewTitleLockV2) {
            return existing;
        }

        var desired = (existing && typeof existing.desired === 'function')
            ? existing.desired()
            : (window.__fcTitleDesired != null ? window.__fcTitleDesired : null);
        var hooked = !!(window.__fcTitleHooked || (existing && existing.isHooked && existing.isHooked()));
        var nativeGet = window.__fcTitleNativeGet || null;
        var nativeSet = window.__fcTitleNativeSet || null;
        var rafId = 0;

        function resolveNative() {
            if (typeof nativeGet === 'function' && typeof nativeSet === 'function') {
                return true;
            }
            var desc = Object.getOwnPropertyDescriptor(Document.prototype, 'title') ||
                Object.getOwnPropertyDescriptor(HTMLDocument.prototype, 'title');
            if (!desc || typeof desc.get !== 'function' || typeof desc.set !== 'function') {
                return false;
            }
            nativeGet = desc.get;
            nativeSet = desc.set;
            window.__fcTitleNativeGet = nativeGet;
            window.__fcTitleNativeSet = nativeSet;
            return true;
        }

        function writeNative(value) {
            var text = value == null ? '' : String(value);
            if (resolveNative()) {
                nativeSet.call(document, text);
                return;
            }
            try {
                var el = document.querySelector('head > title') || document.querySelector('title');
                if (el) {
                    el.textContent = text;
                }
            } catch (e) { /* ignore */ }
        }

        function ensureHook() {
            if (hooked) {
                return true;
            }
            if (!resolveNative()) {
                return false;
            }
            try {
                Object.defineProperty(document, 'title', {
                    configurable: true,
                    enumerable: true,
                    get: function () {
                        return nativeGet.call(document);
                    },
                    set: function (value) {
                        if (desired != null && peekFullCrewRoute()) {
                            var next = value == null ? '' : String(value);
                            if (next !== desired) {
                                // Swallow Jellyfin "Page not found" (and any other overwrite).
                                writeNative(desired);
                                return;
                            }
                        }
                        writeNative(value);
                    }
                });
                hooked = true;
                window.__fcTitleHooked = true;
                return true;
            } catch (e) {
                return false;
            }
        }

        function stopRaf() {
            if (rafId) {
                try {
                    window.cancelAnimationFrame(rafId);
                } catch (e) { /* ignore */ }
                rafId = 0;
            }
        }

        function startRaf() {
            if (rafId || typeof window.requestAnimationFrame !== 'function') {
                return;
            }
            function tick() {
                rafId = 0;
                if (desired == null) {
                    return;
                }
                if (!peekFullCrewRoute()) {
                    // Route left without release — drop lock so Home can own the title.
                    desired = null;
                    window.__fcTitleDesired = null;
                    return;
                }
                try {
                    var cur = resolveNative() ? nativeGet.call(document) : (document.title || '');
                    if (cur !== desired) {
                        writeNative(desired);
                    }
                } catch (e) { /* ignore */ }
                rafId = window.requestAnimationFrame(tick);
            }
            rafId = window.requestAnimationFrame(tick);
        }

        function hold(title) {
            desired = title || 'Full Crew';
            window.__fcTitleDesired = desired;
            ensureHook();
            writeNative(desired);
            startRaf();
        }

        function release() {
            desired = null;
            window.__fcTitleDesired = null;
            stopRaf();
        }

        var api = {
            __fullCrewTitleLockV2: true,
            hold: hold,
            release: release,
            desired: function () {
                return desired;
            },
            isHooked: function () {
                return hooked;
            }
        };
        window.__fullCrewTitleLock = api;
        return api;
    })();

    function criticalCssText() {
        return (
            'html.' + ROUTE_PENDING_CLASS + ',html.' + STATS_BODY_CLASS + ',html.' + STUDIO_BODY_CLASS +
            '{background:var(--background-color,#101010)!important}' +
            'html.' + ROUTE_PENDING_CLASS + ' body,body.' + STATS_BODY_CLASS + ',body.' + STUDIO_BODY_CLASS +
            '{background:var(--background-color,#101010)!important}' +
            /* Hide Jellyfin's not-found / empty page until our overlay mounts */
            'html.' + ROUTE_PENDING_CLASS + ' .mainAnimatedPages > .page,' +
            'html.' + ROUTE_PENDING_CLASS + ' .mainAnimatedPages > .mainAnimatedPage,' +
            'body.' + STATS_BODY_CLASS + ' .mainAnimatedPages > .page,' +
            'body.' + STATS_BODY_CLASS + ' .mainAnimatedPages > .mainAnimatedPage,' +
            'body.' + STUDIO_BODY_CLASS + ' .mainAnimatedPages > .page,' +
            'body.' + STUDIO_BODY_CLASS + ' .mainAnimatedPages > .mainAnimatedPage,' +
            'html.' + ROUTE_PENDING_CLASS + ' .mainAnimatedPages .emptyMessage,' +
            'html.' + ROUTE_PENDING_CLASS + ' .mainAnimatedPages .noItemsMessage,' +
            'body.' + STATS_BODY_CLASS + ' .mainAnimatedPages .emptyMessage,' +
            'body.' + STUDIO_BODY_CLASS + ' .mainAnimatedPages .emptyMessage' +
            '{visibility:hidden!important;pointer-events:none!important;opacity:0!important}' +
            /* Avoid "Page not found" flashing in the skin header while pending */
            'html.' + ROUTE_PENDING_CLASS + ' .skinHeader .pageTitle,' +
            'html.' + ROUTE_PENDING_CLASS + ' .skinHeader .headerTitle,' +
            'html.' + ROUTE_PENDING_CLASS + ' .headerTop .pageTitle,' +
            'html.' + STATS_BODY_CLASS + '.' + ROUTE_PENDING_CLASS + ' .skinHeader .pageTitle,' +
            'html.' + STUDIO_BODY_CLASS + '.' + ROUTE_PENDING_CLASS + ' .skinHeader .pageTitle' +
            '{visibility:hidden!important}' +
            /* Full-viewport host under header (avoid ~30% Jellyfin bleed from 70vh) */
            'body.' + STATS_BODY_CLASS + ' .mainAnimatedPages,' +
            'body.' + STUDIO_BODY_CLASS + ' .mainAnimatedPages,' +
            'html.' + STATS_BODY_CLASS + ' .mainAnimatedPages,' +
            'html.' + STUDIO_BODY_CLASS + ' .mainAnimatedPages,' +
            'html.' + ROUTE_PENDING_CLASS + ' .mainAnimatedPages' +
            '{position:relative!important;' +
            'min-height:calc(100vh - var(--header-height,3.5rem))!important;' +
            'min-height:calc(100dvh - var(--header-height,3.5rem))!important;' +
            'background:var(--background-color,#101010)!important}'
        );
    }

    function ensureCriticalStyles() {
        var existing = document.getElementById(CRITICAL_STYLE_ID);
        if (existing) {
            return existing;
        }
        var style = document.createElement('style');
        style.id = CRITICAL_STYLE_ID;
        style.textContent = criticalCssText();
        var parent = document.head || document.documentElement;
        if (parent) {
            parent.insertBefore(style, parent.firstChild);
        }
        return style;
    }

    function ensureStylesheetLink() {
        if (document.getElementById(STYLE_ID)) {
            return;
        }
        var link = document.createElement('link');
        link.id = STYLE_ID;
        link.rel = 'stylesheet';
        link.href = '/FullCrew/fullcrew.css?v=' + PLUGIN_VERSION;
        (document.head || document.documentElement).appendChild(link);
    }

    /**
     * Apply/remove route body+html classes and optional early title.
     * Safe to call before DOMContentLoaded (body may be null).
     */
    function applyRouteChrome(route, opts) {
        opts = opts || {};
        var root = document.documentElement;
        if (!route) {
            root.classList.remove(ROUTE_PENDING_CLASS, STATS_BODY_CLASS, STUDIO_BODY_CLASS);
            if (document.body) {
                document.body.classList.remove(ROUTE_PENDING_CLASS, STATS_BODY_CLASS, STUDIO_BODY_CLASS);
            }
            return;
        }

        ensureCriticalStyles();
        ensureStylesheetLink();

        root.classList.remove(STATS_BODY_CLASS, STUDIO_BODY_CLASS);
        root.classList.add(route.bodyClass);
        if (opts.pending) {
            root.classList.add(ROUTE_PENDING_CLASS);
        } else {
            root.classList.remove(ROUTE_PENDING_CLASS);
        }

        function onBody(body) {
            if (!body) {
                return;
            }
            body.classList.remove(STATS_BODY_CLASS, STUDIO_BODY_CLASS);
            body.classList.add(route.bodyClass);
            if (opts.pending) {
                body.classList.add(ROUTE_PENDING_CLASS);
            } else {
                body.classList.remove(ROUTE_PENDING_CLASS);
            }
        }

        if (document.body) {
            onBody(document.body);
        } else if (opts.pending) {
            document.addEventListener('DOMContentLoaded', function () {
                var still = peekFullCrewRoute();
                if (still) {
                    onBody(document.body);
                }
            });
        }

        if (opts.setTitle && route.title) {
            TitleLock.hold(route.title);
        }
    }

    function clearRoutePending() {
        var root = document.documentElement;
        root.classList.remove(ROUTE_PENDING_CLASS);
        if (document.body) {
            document.body.classList.remove(ROUTE_PENDING_CLASS);
        }
    }

    // Before-paint intervention when this file runs (defer or async).
    // Index HTML may also inject an identical early boot; both are idempotent.
    (function earlyBoot() {
        var route = peekFullCrewRoute();
        if (!route) {
            return;
        }
        applyRouteChrome(route, { pending: true, setTitle: true });
    })();

    // Guard against duplicate <script src="/FullCrew/fullcrew.js"> tags after upgrades.
    if (window.__fullCrewMain) {
        return;
    }
    window.__fullCrewMain = true;

    var Core = (function () {
        function ensureStyles() {
            ensureCriticalStyles();
            ensureStylesheetLink();
        }

        function el(tag, className, text) {
            var node = document.createElement(tag);
            if (className) {
                node.className = className;
            }
            if (text != null) {
                node.textContent = text;
            }
            return node;
        }

        function prop(obj, pascal, camel) {
            if (!obj) {
                return undefined;
            }
            if (pascal && Object.prototype.hasOwnProperty.call(obj, pascal) && obj[pascal] !== undefined) {
                return obj[pascal];
            }
            if (camel && Object.prototype.hasOwnProperty.call(obj, camel) && obj[camel] !== undefined) {
                return obj[camel];
            }
            if (pascal && obj[pascal] !== undefined) {
                return obj[pascal];
            }
            if (camel && obj[camel] !== undefined) {
                return obj[camel];
            }
            return undefined;
        }

        function apiClient() {
            return window.ApiClient || window.apiClient || null;
        }

        function getJson(path) {
            var client = apiClient();
            var relative = String(path || '').replace(/^\//, '');

            if (client && typeof client.ajax === 'function') {
                return Promise.resolve(
                    client.ajax({
                        url: client.getUrl(relative),
                        type: 'GET',
                        dataType: 'json'
                    })
                );
            }
            if (client && typeof client.getJSON === 'function') {
                return Promise.resolve(client.getJSON(client.getUrl(relative)));
            }
            return fetch('/' + relative, { credentials: 'same-origin' }).then(function (res) {
                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }
                return res.json();
            });
        }

        function normalizeHash(hash) {
            var raw = (hash || '').split('?')[0];
            if (raw.indexOf('#!/') === 0) {
                return '#' + raw.slice(2);
            }
            return raw;
        }

        function parseQuery(query) {
            var params = {};
            if (!query) {
                return params;
            }
            query.split('&').forEach(function (pair) {
                var i = pair.indexOf('=');
                if (i < 0) {
                    return;
                }
                try {
                    params[decodeURIComponent(pair.slice(0, i))] = decodeURIComponent(pair.slice(i + 1));
                } catch (e) {
                    /* ignore */
                }
            });
            return params;
        }

        function detailsHashForItem(itemId) {
            return '#/details?id=' + encodeURIComponent(itemId);
        }

        /**
         * Navigate to a Jellyfin entity page (Person, Genre, Studio, BoxSet, …).
         * Prefer Emby.Page / appRouter; fall back to hash navigation.
         */
        function navigateToItem(itemId) {
            if (!itemId) {
                return false;
            }

            try {
                if (window.Emby && window.Emby.Page && typeof window.Emby.Page.showItem === 'function') {
                    window.Emby.Page.showItem(String(itemId));
                    return true;
                }
            } catch (e) { /* fall through */ }

            try {
                if (window.appRouter && typeof window.appRouter.showItem === 'function') {
                    var client = apiClient();
                    var userId = client && client.getCurrentUserId && client.getCurrentUserId();
                    if (client && userId && typeof client.getItem === 'function') {
                        Promise.resolve(client.getItem(userId, String(itemId)))
                            .then(function (item) {
                                if (item) {
                                    window.appRouter.showItem(item);
                                } else {
                                    window.location.hash = detailsHashForItem(itemId);
                                }
                            })
                            .catch(function () {
                                window.location.hash = detailsHashForItem(itemId);
                            });
                        return true;
                    }

                    window.appRouter.showItem(String(itemId));
                    return true;
                }
            } catch (e1) { /* fall through */ }

            try {
                if (window.Dashboard && typeof window.Dashboard.navigate === 'function') {
                    window.Dashboard.navigate('details?id=' + encodeURIComponent(itemId));
                    return true;
                }
            } catch (e2) { /* fall through */ }

            window.location.hash = detailsHashForItem(itemId);
            return true;
        }

        // Overridable if a feature needs a specialized router (kept for API stability).
        var navigateImpl = navigateToItem;

        function primaryImageUrl(itemId, maxHeight) {
            if (!itemId) {
                return null;
            }
            var h = maxHeight || 360;
            var client = apiClient();
            try {
                if (client && typeof client.getImageUrl === 'function') {
                    return client.getImageUrl(itemId, { type: 'Primary', maxHeight: h });
                }
            } catch (e) { /* ignore */ }
            var base = client && typeof client.getUrl === 'function'
                ? client.getUrl('Items/' + itemId + '/Images/Primary')
                : '/Items/' + encodeURIComponent(itemId) + '/Images/Primary';
            return base + (base.indexOf('?') >= 0 ? '&' : '?') + 'maxHeight=' + h + '&quality=90';
        }

        /**
         * First-class document + skinHeader pageTitle ownership.
         * Only while on #/fullcrew/* — never rewrite Home brand/"Jellyfin" chrome.
         * claim()/release() set intent; TitleLock intercepts document.title writes
         * synchronously (MutationObserver alone still allows one tab-title frame of
         * "Page not found"). Header text is re-applied via MutationObserver.
         */
        var PageTitle = (function () {
            var owned = false;
            var label = '';
            var applying = false;
            var titleObserver = null;
            var PREV_DOC = 'data-fullcrew-prev-title';
            var OWNED = 'data-fullcrew-owned-title';
            var PREV_HDR = 'data-fullcrew-prev-header';
            /* Narrow: real page titles only — never bare .headerTitle / h1 / sectionTitle
             * (those match brand chips and home section headers → "Stats"/"Jellyfin" stickers). */
            var HEADER_SEL =
                '.skinHeader .headerLeft .pageTitle, .skinHeader .pageTitle, .headerTop .pageTitle';

            function headerNodes() {
                return document.querySelectorAll(HEADER_SEL);
            }

            function ownedNodesEverywhere() {
                return document.querySelectorAll('[' + OWNED + '="1"]');
            }

            function isOurTab(node) {
                return !!(node && (node.id === 'fullCrewStatsTab' || (node.closest && node.closest('#fullCrewStatsTab'))));
            }

            function onFullCrewRoute() {
                return !!peekFullCrewRoute();
            }

            /** Single visible pageTitle — owning several leaves overlapping stickers. */
            function primaryHeaderNode() {
                var nodes = headerNodes();
                for (var i = 0; i < nodes.length; i++) {
                    var node = nodes[i];
                    if (!node || isOurTab(node)) {
                        continue;
                    }
                    if (node.offsetParent === null && node.getClientRects && !node.getClientRects().length) {
                        continue;
                    }
                    return node;
                }
                return nodes.length ? nodes[0] : null;
            }

            function scrubNode(node) {
                if (!node) {
                    return;
                }
                if (node.getAttribute(OWNED) !== '1' && node.getAttribute(PREV_HDR) == null) {
                    return;
                }
                var prevHdr = node.getAttribute(PREV_HDR);
                // Always restore prior text (including '') so we never leave "Stats" behind.
                if (prevHdr != null) {
                    node.textContent = prevHdr;
                }
                node.removeAttribute(PREV_HDR);
                node.removeAttribute(OWNED);
            }

            function dropOwnership() {
                owned = false;
                label = '';
                TitleLock.release();
            }

            function apply() {
                if (!owned || applying) {
                    return;
                }
                // Never keep title ownership off Full Crew routes (Home/Favourites/etc.).
                if (!onFullCrewRoute()) {
                    dropOwnership();
                    restore();
                    return;
                }
                applying = true;
                try {
                    if (!document.documentElement.getAttribute(PREV_DOC)) {
                        var cur = document.title || '';
                        // Don't stash "Page not found" / our own label as the restore target.
                        if (!/page not found/i.test(cur) && cur !== label) {
                            document.documentElement.setAttribute(PREV_DOC, cur);
                        } else if (!document.documentElement.getAttribute(PREV_DOC)) {
                            document.documentElement.setAttribute(PREV_DOC, 'Jellyfin');
                        }
                    }
                    TitleLock.hold(label);

                    var primary = primaryHeaderNode();
                    // Drop ownership on any stale/extra nodes so Home never shows dual stickers.
                    Array.prototype.forEach.call(ownedNodesEverywhere(), function (node) {
                        if (node !== primary) {
                            scrubNode(node);
                        }
                    });

                    if (!primary) {
                        applying = false;
                        return;
                    }

                    var text = (primary.textContent || '').replace(/\s+/g, ' ').trim();
                    if (!primary.getAttribute(PREV_HDR)) {
                        if (text && !/page not found/i.test(text) && text !== label) {
                            primary.setAttribute(PREV_HDR, text);
                        } else {
                            primary.setAttribute(PREV_HDR, '');
                        }
                    }
                    primary.setAttribute(OWNED, '1');
                    if (primary.textContent !== label) {
                        primary.textContent = label;
                    }
                } catch (e) { /* ignore */ }
                applying = false;
            }

            function restore() {
                stopObserver();
                try {
                    var prev = document.documentElement.getAttribute(PREV_DOC);
                    if (prev != null) {
                        // TitleLock must already be released so this write sticks.
                        document.title = prev;
                        document.documentElement.removeAttribute(PREV_DOC);
                    }
                } catch (e) { /* ignore */ }
                Array.prototype.forEach.call(ownedNodesEverywhere(), scrubNode);
                // Also clear any nodes matching the header selector that still carry attrs.
                Array.prototype.forEach.call(headerNodes(), function (node) {
                    if (node && (node.getAttribute(OWNED) === '1' || node.getAttribute(PREV_HDR) != null)) {
                        scrubNode(node);
                    }
                });
            }

            function stopObserver() {
                if (titleObserver) {
                    titleObserver.disconnect();
                    titleObserver = null;
                }
            }

            function startObserver() {
                if (titleObserver || typeof MutationObserver === 'undefined') {
                    return;
                }
                titleObserver = new MutationObserver(function () {
                    if (!owned || applying) {
                        return;
                    }
                    if (!onFullCrewRoute()) {
                        dropOwnership();
                        restore();
                        return;
                    }
                    var primary = primaryHeaderNode();
                    var titleWrong = (document.title || '') !== label ||
                        /page not found/i.test(document.title || '');
                    if (titleWrong || !primary || primary.getAttribute(OWNED) !== '1' ||
                        primary.textContent !== label) {
                        apply();
                    }
                });
                titleObserver.observe(document.documentElement, {
                    subtree: true,
                    childList: true,
                    characterData: true
                });
                // Also watch <title> directly when present (some skins replace the node).
                try {
                    var titleEl = document.querySelector('head > title') || document.querySelector('title');
                    if (titleEl) {
                        titleObserver.observe(titleEl, {
                            characterData: true,
                            childList: true,
                            subtree: true
                        });
                    }
                } catch (e) { /* ignore */ }
            }

            return {
                claim: function (titleText) {
                    if (!onFullCrewRoute()) {
                        this.release();
                        return;
                    }
                    owned = true;
                    label = titleText || 'Full Crew';
                    TitleLock.hold(label);
                    apply();
                    startObserver();
                },
                release: function () {
                    dropOwnership();
                    restore();
                },
                tick: function () {
                    if (!owned) {
                        return;
                    }
                    if (!onFullCrewRoute()) {
                        this.release();
                        return;
                    }
                    apply();
                },
                isOwned: function () {
                    return owned;
                }
            };
        })();

        var Ui = {
            status: function (message, isError, className) {
                return el(
                    'div',
                    (className || 'fullCrewStatsStatus') + (isError ? ' fullCrewStatsStatus--error' : ''),
                    message
                );
            },
            metaRows: function (rows) {
                var dl = el('dl', 'fullCrewMetaRows fullCrewStudioMetaRows');
                (rows || []).forEach(function (row) {
                    if (!row || !row.value) {
                        return;
                    }
                    dl.appendChild(el('dt', 'fullCrewStudioMetaLabel', row.label));
                    dl.appendChild(el('dd', 'fullCrewStudioMetaValue', row.value));
                });
                return dl.childNodes.length ? dl : null;
            },
            badgeLink: function (labelText, href) {
                var a = el('a', 'fullCrewStudioBadge', labelText);
                a.href = href;
                a.target = '_blank';
                a.rel = 'noopener noreferrer';
                return a;
            },
            itemLink: function (text, itemId, className) {
                var a = el('a', className || 'fullCrewStatsItemLink', text);
                a.href = detailsHashForItem(itemId);
                a.addEventListener('click', function (ev) {
                    if (ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) {
                        return;
                    }
                    if (navigateImpl(itemId)) {
                        ev.preventDefault();
                    }
                });
                return a;
            },
            externalLink: function (text, href, className) {
                var a = el('a', className || 'fullCrewStatsItemLink', text);
                a.href = href;
                a.target = '_blank';
                a.rel = 'noopener noreferrer';
                return a;
            },
            posterCard: function (opts) {
                var id = opts.id;
                var card = el('a', 'fullCrewStudioCard');
                card.href = detailsHashForItem(id);
                card.addEventListener('click', function (ev) {
                    if (ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) {
                        return;
                    }
                    if (navigateImpl(id)) {
                        ev.preventDefault();
                    }
                });
                var poster = el('div', 'fullCrewStudioPoster');
                var imgUrl = primaryImageUrl(id);
                if (imgUrl && opts.imageTag) {
                    var img = el('img', 'fullCrewStudioPosterImg');
                    img.src = imgUrl;
                    img.alt = opts.name || '';
                    img.loading = 'lazy';
                    img.onerror = function () {
                        var fallback = el('div', 'fullCrewStudioPosterFallback', (opts.type || 'Title').charAt(0));
                        if (img.parentNode) {
                            img.parentNode.replaceChild(fallback, img);
                        }
                    };
                    poster.appendChild(img);
                } else {
                    poster.appendChild(el('div', 'fullCrewStudioPosterFallback', (opts.type || 'Title').charAt(0)));
                }
                card.appendChild(poster);
                var caption = el('div', 'fullCrewStudioCardCaption');
                caption.appendChild(el('div', 'fullCrewStudioCardName', opts.name || 'Untitled'));
                var line = [];
                if (opts.year) { line.push(String(opts.year)); }
                if (opts.rating) { line.push('★ ' + Number(opts.rating).toFixed(1)); }
                if (line.length) {
                    caption.appendChild(el('div', 'fullCrewStudioCardMeta', line.join(' · ')));
                }
                card.appendChild(caption);
                return card;
            },
            posterRow: function (heading, cards) {
                var section = el('section', 'fullCrewStudioSection fullCrewStudioSection--library');
                section.appendChild(el('h2', 'fullCrewStudioSectionTitle', heading));
                var scroller = el('div', 'fullCrewStudioRow');
                (cards || []).forEach(function (card) { scroller.appendChild(card); });
                section.appendChild(scroller);
                return section;
            }
        };

        function CustomPage(spec) {
            this.spec = spec;
        }

        CustomPage.findMount = function () {
            return (
                document.querySelector('.mainAnimatedPages') ||
                document.querySelector('#mainContent') ||
                document.querySelector('.mainDrawer-scrollContainer') ||
                document.body
            );
        };

        CustomPage.prototype.findMount = function () {
            return CustomPage.findMount();
        };

        CustomPage.prototype.get = function () {
            return document.getElementById(this.spec.id);
        };

        CustomPage.prototype.tearDown = function (opts) {
            opts = opts || {};
            if (this.spec.bodyClass) {
                // Never clear ROUTE_PENDING here — callers keep pending up across
                // shell swaps so Jellyfin 404 chrome cannot flash between remounts.
                document.documentElement.classList.remove(this.spec.bodyClass);
                document.body.classList.remove(this.spec.bodyClass);
            }
            if (opts.releaseTitle !== false && this.spec.ownsTitle) {
                PageTitle.release();
            }
            var page = this.get();
            if (page && page.parentNode) {
                page.parentNode.removeChild(page);
            }
        };

        CustomPage.prototype.isCurrent = function (routeKey) {
            var page = this.get();
            return !!(page && page.getAttribute('data-route') === routeKey && page.getAttribute('data-loaded') === '1');
        };

        CustomPage.prototype.mountShell = function (routeKey, buildShell) {
            this.tearDown({ releaseTitle: false });
            var page = buildShell();
            page.setAttribute('data-route', routeKey);
            if (this.spec.bodyClass) {
                document.documentElement.classList.add(this.spec.bodyClass);
                document.body.classList.add(this.spec.bodyClass);
                document.documentElement.classList.remove(ROUTE_PENDING_CLASS);
                document.body.classList.remove(ROUTE_PENDING_CLASS);
            }
            this.findMount().appendChild(page);
            return page;
        };

        /**
         * Shared promise→DOM bind: skip if page unmounted; optional stillValid gate.
         * opts: { stillValid, onSuccess, onError, warn, errorSelector, errorMessage, errorClass }
         */
        function bindPageLoad(page, promise, opts) {
            opts = opts || {};
            return promise
                .then(function (data) {
                    if (!document.body.contains(page)) {
                        return;
                    }
                    if (opts.stillValid && !opts.stillValid()) {
                        return;
                    }
                    if (typeof opts.onSuccess === 'function') {
                        opts.onSuccess(data);
                    }
                    page.setAttribute('data-loaded', '1');
                    page.removeAttribute('data-loading');
                })
                .catch(function (err) {
                    if (!document.body.contains(page)) {
                        return;
                    }
                    if (opts.warn) {
                        console.warn(opts.warn, err);
                    }
                    if (typeof opts.onError === 'function') {
                        opts.onError(err);
                    } else if (opts.errorMessage) {
                        var body = page.querySelector(opts.errorSelector || '.fullCrewStatsBody, .fullCrewStudioBody');
                        if (body) {
                            body.innerHTML = '';
                            body.appendChild(
                                Ui.status(opts.errorMessage, true, opts.errorClass || 'fullCrewStatsStatus')
                            );
                        }
                    }
                    page.removeAttribute('data-loading');
                });
        }

        return {
            ensureStyles: ensureStyles,
            el: el,
            prop: prop,
            apiClient: apiClient,
            getJson: getJson,
            normalizeHash: normalizeHash,
            parseQuery: parseQuery,
            detailsHashForItem: detailsHashForItem,
            navigateToItem: function (itemId) {
                return navigateImpl(itemId);
            },
            setNavigateToItem: function (fn) {
                if (typeof fn === 'function') {
                    navigateImpl = fn;
                }
            },
            primaryImageUrl: primaryImageUrl,
            PageTitle: PageTitle,
            Ui: Ui,
            CustomPage: CustomPage,
            findMount: CustomPage.findMount,
            bindPageLoad: bindPageLoad,
            version: PLUGIN_VERSION
        };
    })();

    /* ================================================================== */
    /* Feature: Cast & Crew accordion                                     */
    /* ================================================================== */

    var SECTION_ID = 'fullCrewSection';
    /** itemId → true after credits failed/empty for this navigation (no remount thrash). */
    var creditsGaveUp = Object.create(null);
    /** Track detail item from the hash so we clear the circuit breaker on navigate. */
    var creditsNavItemId = null;
    var creditsFetchEpoch = 0;

    function syncCreditsNavState() {
        var id = getItemIdFromLocation();
        if (id !== creditsNavItemId) {
            creditsNavItemId = id;
            creditsGaveUp = Object.create(null);
            creditsFetchEpoch += 1;
        }
    }

    function noteCreditsGaveUp(itemId) {
        if (itemId) {
            creditsGaveUp[itemId] = true;
        }
    }

    function ensureStyles() {
        Core.ensureStyles();
    }

    function getItemIdFromView(view) {
        if (!view) {
            return null;
        }

        return (
            view.getAttribute('data-id') ||
            view.getAttribute('data-itemid') ||
            (view.dataset && (view.dataset.id || view.dataset.itemid)) ||
            null
        );
    }

    function getItemIdFromLocation() {
        try {
            var hash = window.location.hash || '';
            var queryIndex = hash.indexOf('?');
            if (queryIndex === -1) {
                return null;
            }

            var params = new URLSearchParams(hash.substring(queryIndex + 1));
            return params.get('id');
        } catch (e) {
            return null;
        }
    }

    function isDetailView(view) {
        if (!view || !view.classList) {
            return false;
        }

        return (
            view.classList.contains('itemDetailPage') ||
            view.classList.contains('itemDetailPagepadded') ||
            !!view.querySelector('.itemDetailPage')
        );
    }

    function findNativeCastSections(view) {
        var nodes = [];

        function add(el) {
            if (!el || el.id === SECTION_ID || el.classList.contains('fullCrewSection')) {
                return;
            }
            if (nodes.indexOf(el) !== -1) {
                return;
            }
            nodes.push(el);
        }

        var cast = view.querySelector('#castContent');
        if (cast) {
            add(cast.closest('.verticalSection') || cast.parentElement || cast);
        }

        var people = view.querySelector('.peopleItemsContainer');
        if (people) {
            add(people.closest('.verticalSection') || people.parentElement || people);
        }

        var titles = view.querySelectorAll('.sectionTitle, h2, .detailSectionHeader');
        Array.prototype.forEach.call(titles, function (titleEl) {
            var text = (titleEl.textContent || '').replace(/\s+/g, ' ').trim();
            if (/^cast\s*(?:&|and)\s*crew$/i.test(text) && !titleEl.closest('.fullCrewSection')) {
                add(titleEl.closest('.verticalSection') || titleEl.parentElement);
            }
        });

        // Vanilla Jellyfin detail fact rows for people (Director / Writer).
        // Keep Genres / Studios — those aren't duplicated in Full Crew.
        Array.prototype.forEach.call(view.querySelectorAll('.directorsGroup, .writersGroup'), function (el) {
            add(el);
        });

        return nodes;
    }

    function hideNativeCast(view) {
        findNativeCastSections(view).forEach(function (el) {
            el.classList.add('fullCrewHideNative');
            el.setAttribute('data-fullcrew-hidden', '1');
        });
    }

    function showNativeCast(view) {
        Array.prototype.forEach.call(view.querySelectorAll('[data-fullcrew-hidden="1"]'), function (el) {
            el.classList.remove('fullCrewHideNative');
            el.removeAttribute('data-fullcrew-hidden');
        });
    }

    function findInsertionPoint(view) {
        var natives = findNativeCastSections(view);
        if (natives.length) {
            return natives[0];
        }

        var cast = view.querySelector('#castContent');
        if (cast) {
            return cast.closest('.verticalSection') || cast.parentElement || cast;
        }

        var people = view.querySelector('.peopleItemsContainer');
        if (people) {
            return people.closest('.verticalSection') || people;
        }

        return (
            view.querySelector('.detailSection') ||
            view.querySelector('.itemDetailMain') ||
            view.querySelector('.mainDetailButtons') ||
            view
        );
    }

    function initials(name) {
        if (!name) {
            return '?';
        }

        var parts = name.trim().split(/\s+/).slice(0, 2);
        return parts
            .map(function (p) {
                return p.charAt(0).toUpperCase();
            })
            .join('');
    }

    function createElement(tag, className, text) {
        return Core.el(tag, className, text);
    }

    function removeExisting(view) {
        var existing = view.querySelector('#' + SECTION_ID);
        if (existing) {
            existing.remove();
        }
    }

    /** Movie / Series / Season / Episode — the only types that get cast/crew or bumper/trailer UI. */
    function isMediaDetailItemType(type) {
        return /^(Movie|Series|Season|Episode)$/i.test(String(type || ''));
    }

    function teardownDetailExtras(view) {
        if (!view) {
            return;
        }
        removeExisting(view);
        showNativeCast(view);
        removeDetailButtons(view);
    }

    function renderStatus(section, message, isError) {
        var body = section.querySelector('.fullCrewBody');
        body.innerHTML = '';
        var status = createElement(
            'div',
            'fullCrewStatus' + (isError ? ' fullCrewStatus--error' : ''),
            message
        );
        body.appendChild(status);
    }

    function renderDepartments(section, data) {
        var body = section.querySelector('.fullCrewBody');
        body.innerHTML = '';

        var meta = section.querySelector('.fullCrewMeta');
        if (meta) {
            var total = (data.Departments || data.departments || []).reduce(function (sum, d) {
                var people = d.People || d.people || [];
                return sum + people.length;
            }, 0);
            meta.textContent = total + ' people';
        }

        var departments = data.Departments || data.departments || [];
        if (!departments.length) {
            renderStatus(section, 'No cast or crew credits found.', false);
            return;
        }

        var accordion = createElement('div', 'fullCrewAccordion');

        departments.forEach(function (dept, index) {
            var name = dept.Name || dept.name || 'Other';
            var people = dept.People || dept.people || [];
            var deptEl = createElement('div', 'fullCrewDept' + (index === 0 ? ' is-open' : ''));

            var toggle = createElement('button', 'fullCrewDeptToggle');
            toggle.type = 'button';
            toggle.setAttribute('aria-expanded', index === 0 ? 'true' : 'false');

            var left = createElement('span');
            left.appendChild(createElement('span', 'fullCrewDeptName', name));
            left.appendChild(document.createTextNode(' '));
            left.appendChild(createElement('span', 'fullCrewDeptCount', '(' + people.length + ')'));

            toggle.appendChild(left);
            toggle.appendChild(createElement('span', 'fullCrewChevron'));

            var deptBody = createElement('div', 'fullCrewDeptBody');
            var list = createElement('ul', 'fullCrewPeople');

            people.forEach(function (person) {
                var personName = person.Name || person.name || 'Unknown';
                var role = person.Role || person.role || '';
                var roles = person.Roles || person.roles;
                if (!roles || !roles.length) {
                    roles = String(role)
                        .split(/\s*·\s*|\n+/)
                        .map(function (part) { return part.trim(); })
                        .filter(Boolean);
                }
                var profileUrl = person.ProfileUrl || person.profileUrl;
                var tmdbId = person.TmdbPersonId || person.tmdbPersonId;
                var card;

                if (tmdbId) {
                    card = createElement('a', 'fullCrewPerson');
                    card.href = 'https://www.themoviedb.org/person/' + encodeURIComponent(String(tmdbId));
                    card.target = '_blank';
                    card.rel = 'noopener noreferrer';
                    card.title = personName + ' on TMDB';
                } else {
                    card = createElement('div', 'fullCrewPerson');
                }

                if (profileUrl) {
                    var img = createElement('img', 'fullCrewAvatar');
                    img.src = profileUrl;
                    img.alt = personName;
                    img.loading = 'lazy';
                    img.referrerPolicy = 'no-referrer';
                    img.onerror = function () {
                        var fallback = createElement('div', 'fullCrewAvatar fullCrewAvatarFallback', initials(personName));
                        if (img.parentNode) {
                            img.parentNode.replaceChild(fallback, img);
                        }
                    };
                    card.appendChild(img);
                } else {
                    card.appendChild(createElement('div', 'fullCrewAvatar fullCrewAvatarFallback', initials(personName)));
                }

                var text = createElement('div', 'fullCrewPersonText');
                text.appendChild(createElement('div', 'fullCrewPersonName', personName));
                if (roles.length) {
                    var roleList = createElement('div', 'fullCrewPersonRoles');
                    roles.forEach(function (entry) {
                        roleList.appendChild(createElement('div', 'fullCrewPersonRole', entry));
                    });
                    text.appendChild(roleList);
                }
                card.appendChild(text);

                var li = createElement('li');
                li.appendChild(card);
                list.appendChild(li);
            });

            deptBody.appendChild(list);
            deptEl.appendChild(toggle);
            deptEl.appendChild(deptBody);

            toggle.addEventListener('click', function () {
                var open = deptEl.classList.toggle('is-open');
                toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
            });

            accordion.appendChild(deptEl);
        });

        body.appendChild(accordion);
    }

    function createSection() {
        var section = createElement('div', 'fullCrewSection verticalSection');
        section.id = SECTION_ID;

        var header = createElement('div', 'fullCrewHeader');
        header.appendChild(createElement('h2', 'fullCrewTitle sectionTitle', 'Cast & Crew'));
        header.appendChild(createElement('div', 'fullCrewMeta', ''));

        var body = createElement('div', 'fullCrewBody');
        section.appendChild(header);
        section.appendChild(body);
        return section;
    }

    function apiClient() {
        return Core.apiClient();
    }

    function fetchCredits(itemId) {
        return Core.getJson('FullCrew/' + encodeURIComponent(itemId));
    }

    function mount(view) {
        if (!view) {
            return;
        }

        if (!isDetailView(view) && !view.querySelector('.itemDetailImage, .detailImageContainer, .itemDetailGalleryLink')) {
            return;
        }

        syncCreditsNavState();

        var itemId = getItemIdFromView(view) || getItemIdFromLocation();
        if (!itemId) {
            return;
        }

        ensureStyles();

        // Resolve type before injecting — Person/Genre/Studio/BoxSet detail pages share
        // the same itemDetailPage shell and must not get cast/crew or bumper/trailer.
        getItemPromise(itemId).then(function (item) {
            if (!document.body.contains(view)) {
                return;
            }

            var type = item && (item.Type || item.type);
            if (!item) {
                // ApiClient not ready yet — retry on the next scan pass.
                return;
            }
            if (!isMediaDetailItemType(type)) {
                teardownDetailExtras(view);
                return;
            }

            mountDetailButtons(view, itemId);
            mountCreditsSection(view, itemId);
        });
    }

    function settleCreditsFailure(view, section, itemId) {
        noteCreditsGaveUp(itemId);
        showNativeCast(view);
        if (section && section.parentNode) {
            section.remove();
        }
    }

    function mountCreditsSection(view, itemId) {
        if (!itemId || creditsGaveUp[itemId]) {
            return;
        }

        var existing = view.querySelector('#' + SECTION_ID);
        if (existing && existing.getAttribute('data-item-id') === itemId) {
            // Same item: skip while loading, loaded, or sticky error — never remount-thrash.
            if (
                existing.getAttribute('data-loaded') === '1' ||
                existing.getAttribute('data-loading') === '1' ||
                existing.getAttribute('data-error') === '1'
            ) {
                return;
            }
            // Legacy section without state attrs — treat as settled for this id.
            return;
        }

        removeExisting(view);

        var section = createSection();
        section.setAttribute('data-item-id', itemId);
        section.setAttribute('data-loading', '1');
        var fetchGen = String(creditsFetchEpoch) + '-' + Date.now() + '-' + Math.random().toString(36).slice(2, 8);
        section.setAttribute('data-fetch-gen', fetchGen);
        renderStatus(section, 'Loading cast & crew…', false);

        var anchor = findInsertionPoint(view);
        if (anchor && anchor.parentNode) {
            // Sit where the native Cast & Crew block is (we'll hide that once data loads).
            anchor.parentNode.insertBefore(section, anchor);
        } else {
            view.appendChild(section);
        }

        function stillCurrent() {
            return (
                document.body.contains(section) &&
                section.getAttribute('data-fetch-gen') === fetchGen &&
                !creditsGaveUp[itemId]
            );
        }

        fetchCredits(itemId)
            .then(function (data) {
                if (!stillCurrent()) {
                    return;
                }

                var error = data.Error || data.error;
                if (error) {
                    settleCreditsFailure(view, section, itemId);
                    return;
                }

                var departments = data.Departments || data.departments || [];
                if (!departments.length) {
                    // Empty is terminal for this navigation — leave Jellyfin native cast alone.
                    settleCreditsFailure(view, section, itemId);
                    return;
                }

                hideNativeCast(view);
                renderDepartments(section, data);
                section.setAttribute('data-loaded', '1');
                section.removeAttribute('data-loading');
            })
            .catch(function (err) {
                if (!stillCurrent()) {
                    return;
                }
                console.warn('[FullCrew] failed to load credits', err);
                settleCreditsFailure(view, section, itemId);
            });
    }

    /* ================================================================== */
    /* Feature: Break bumper & trailer buttons                            */
    /* ================================================================== */

    var BUMPER_BTN_ID = 'fullCrewBumperButton';
    var TRAILER_BTN_ID = 'fullCrewTrailerButton';
    var itemPromiseCache = Object.create(null);

    function fetchBumper(itemId) {
        return Core.getJson('FullCrew/' + encodeURIComponent(itemId) + '/bumper');
    }

    function fetchTrailer(itemId) {
        return Core.getJson('FullCrew/' + encodeURIComponent(itemId) + '/trailer');
    }

    function extractYouTubeId(url) {
        if (!url) {
            return null;
        }
        var m = String(url).match(/(?:youtu\.be\/|v=|embed\/|shorts\/)([A-Za-z0-9_-]{11})/);
        return m ? m[1] : null;
    }

    function getItemPromise(itemId) {
        if (!itemId) {
            return Promise.resolve(null);
        }

        if (Object.prototype.hasOwnProperty.call(itemPromiseCache, itemId)) {
            return itemPromiseCache[itemId];
        }

        var client = apiClient();
        if (!client || typeof client.getItem !== 'function') {
            // Client may not be ready yet during early scan — do not cache a permanent miss.
            return Promise.resolve(null);
        }

        try {
            var userId = client.getCurrentUserId && client.getCurrentUserId();
            if (!userId) {
                return Promise.resolve(null);
            }
            itemPromiseCache[itemId] = Promise.resolve(client.getItem(userId, itemId)).catch(function () {
                delete itemPromiseCache[itemId];
                return null;
            });
            return itemPromiseCache[itemId];
        } catch (e) {
            return Promise.resolve(null);
        }
    }

    function tryPlayNativeTrailers(item) {
        try {
            if (window.PlaybackManager && typeof window.PlaybackManager.playTrailers === 'function') {
                window.PlaybackManager.playTrailers(item);
                return true;
            }
        } catch (e) {
            /* ignore */
        }

        try {
            if (typeof window.require === 'function') {
                // Sync require may throw; async path is handled by returning false.
                var pm = window.require('playbackManager');
                pm = pm && (pm.default || pm);
                if (pm && typeof pm.playTrailers === 'function') {
                    pm.playTrailers(item);
                    return true;
                }
            }
        } catch (e2) {
            /* ignore */
        }

        return false;
    }

    function playResolvedClip(data, fallbackTitle) {
        var error = data && (data.Error || data.error);
        var videoId = data && (data.YouTubeVideoId || data.youTubeVideoId);
        var youtubeUrl = data && (data.YouTubeUrl || data.youTubeUrl);
        var searchUrl = data && (data.SearchUrl || data.searchUrl);
        if (error && !videoId && !youtubeUrl && !searchUrl) {
            console.warn('[FullCrew]', error);
            return;
        }

        var source = (data && (data.Source || data.source)) || '';
        var localId = data && (data.LocalItemId || data.localItemId);
        var title = (data && (data.Title || data.title)) || fallbackTitle;

        if (source === 'Local' && localId) {
            return playLocalItem(localId).catch(function (err) {
                console.warn('[FullCrew] local play failed', err);
                if (!openYouTubeEmbed(videoId, title) && (youtubeUrl || searchUrl)) {
                    openExternal(youtubeUrl || searchUrl);
                }
            });
        }

        if (openYouTubeEmbed(videoId, title)) {
            return;
        }

        openExternal(youtubeUrl || searchUrl);
    }

    function playLocalItem(localItemId) {
        var client = apiClient();
        if (!client || !localItemId) {
            return Promise.reject(new Error('No ApiClient'));
        }

        var userId = client.getCurrentUserId && client.getCurrentUserId();
        var itemPromise = userId
            ? client.getItem(userId, localItemId)
            : client.getJSON(client.getUrl('Items/' + localItemId));

        return Promise.resolve(itemPromise).then(function (item) {
            return new Promise(function (resolve, reject) {
                function playWith(pm) {
                    try {
                        pm.play({ items: [item] });
                        resolve();
                    } catch (e) {
                        reject(e);
                    }
                }

                if (window.PlaybackManager && typeof window.PlaybackManager.play === 'function') {
                    playWith(window.PlaybackManager);
                    return;
                }

                if (typeof window.require === 'function') {
                    window.require(['playbackManager'], function (pm) {
                        playWith(pm.default || pm);
                    }, reject);
                    return;
                }

                reject(new Error('playbackManager unavailable'));
            });
        });
    }

    function openExternal(url) {
        if (!url) {
            return;
        }
        window.open(url, '_blank', 'noopener,noreferrer');
    }

    var bumperOverlayReturnFocus = null;

    function closeBumperOverlay() {
        var overlay = document.getElementById('fullCrewBumperOverlay');
        if (overlay && overlay.parentNode) {
            overlay.parentNode.removeChild(overlay);
        }
        document.removeEventListener('keydown', onBumperOverlayKeydown, true);
        var restore = bumperOverlayReturnFocus;
        bumperOverlayReturnFocus = null;
        if (restore && typeof restore.focus === 'function' && document.contains(restore)) {
            try {
                restore.focus();
            } catch (e) {
                /* ignore */
            }
        }
    }

    function onBumperOverlayKeydown(e) {
        if (!e) {
            return;
        }
        if (e.key === 'Escape' || e.keyCode === 27) {
            closeBumperOverlay();
            return;
        }

        if (e.key !== 'Tab' && e.keyCode !== 9) {
            return;
        }

        var overlay = document.getElementById('fullCrewBumperOverlay');
        if (!overlay) {
            return;
        }

        var nodes = overlay.querySelectorAll('button, [href], iframe, input, select, textarea, [tabindex]:not([tabindex="-1"])');
        var list = Array.prototype.filter.call(nodes, function (el) {
            return !el.disabled && el.getAttribute('tabindex') !== '-1';
        });
        if (!list.length) {
            e.preventDefault();
            return;
        }

        var first = list[0];
        var last = list[list.length - 1];
        var active = document.activeElement;
        if (e.shiftKey) {
            if (active === first || !overlay.contains(active)) {
                e.preventDefault();
                last.focus();
            }
        } else if (active === last || !overlay.contains(active)) {
            e.preventDefault();
            first.focus();
        }
    }

    function openYouTubeEmbed(videoId, title) {
        if (!videoId) {
            return false;
        }

        var returnFocus = document.activeElement;
        // Drop any prior overlay without restoring focus; we keep returnFocus for this open.
        bumperOverlayReturnFocus = null;
        closeBumperOverlay();
        bumperOverlayReturnFocus = returnFocus;

        var overlay = document.createElement('div');
        overlay.id = 'fullCrewBumperOverlay';
        overlay.className = 'fullCrewBumperOverlay';
        overlay.setAttribute('role', 'dialog');
        overlay.setAttribute('aria-modal', 'true');
        overlay.setAttribute('aria-label', title || 'Break bumper');

        var backdrop = document.createElement('div');
        backdrop.className = 'fullCrewBumperBackdrop';
        backdrop.addEventListener('click', closeBumperOverlay);

        var panel = document.createElement('div');
        panel.className = 'fullCrewBumperPanel';

        var header = document.createElement('div');
        header.className = 'fullCrewBumperOverlayHeader';
        header.appendChild(createElement('div', 'fullCrewBumperOverlayTitle', title || 'Break bumper'));

        var closeBtn = document.createElement('button');
        closeBtn.type = 'button';
        closeBtn.className = 'fullCrewBumperClose';
        closeBtn.setAttribute('aria-label', 'Close');
        closeBtn.textContent = '×';
        closeBtn.addEventListener('click', closeBumperOverlay);
        header.appendChild(closeBtn);

        var frameWrap = document.createElement('div');
        frameWrap.className = 'fullCrewBumperFrameWrap';

        var iframe = document.createElement('iframe');
        iframe.className = 'fullCrewBumperFrame';
        iframe.src = 'https://www.youtube-nocookie.com/embed/' + encodeURIComponent(videoId)
            + '?autoplay=1&rel=0&modestbranding=1';
        iframe.title = title || 'Break bumper';
        iframe.allow = 'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share';
        iframe.allowFullscreen = true;
        iframe.setAttribute('allowfullscreen', 'true');
        iframe.referrerPolicy = 'strict-origin-when-cross-origin';

        frameWrap.appendChild(iframe);
        panel.appendChild(header);
        panel.appendChild(frameWrap);
        overlay.appendChild(backdrop);
        overlay.appendChild(panel);
        document.body.appendChild(overlay);
        document.addEventListener('keydown', onBumperOverlayKeydown, true);
        closeBtn.focus();
        return true;
    }

    function handleBumperClick(itemId, button) {
        if (!itemId || !button || button.getAttribute('data-busy') === '1') {
            return;
        }

        button.setAttribute('data-busy', '1');
        button.classList.add('fullCrewBumperBusy');

        fetchBumper(itemId)
            .then(function (data) {
                return playResolvedClip(data, 'Break bumper');
            })
            .catch(function (err) {
                console.warn('[FullCrew] bumper request failed', err);
            })
            .then(function () {
                button.removeAttribute('data-busy');
                button.classList.remove('fullCrewBumperBusy');
            });
    }

    function handleTrailerClick(itemId, button) {
        if (!itemId || !button || button.getAttribute('data-busy') === '1') {
            return;
        }

        button.setAttribute('data-busy', '1');
        button.classList.add('fullCrewBumperBusy');

        getItemPromise(itemId)
            .then(function (item) {
                if (item) {
                    var hasLocal = !!(item.LocalTrailerCount);
                    var remotes = item.RemoteTrailers || item.remoteTrailers || [];
                    if ((hasLocal || (remotes && remotes.length)) && tryPlayNativeTrailers(item)) {
                        return;
                    }
                    if (remotes && remotes.length) {
                        var first = remotes[0] || {};
                        var url = first.Url || first.url || '';
                        var videoId = extractYouTubeId(url);
                        if (openYouTubeEmbed(videoId, first.Name || first.name || 'Trailer')) {
                            return;
                        }
                        if (url) {
                            openExternal(url);
                            return;
                        }
                    }
                }

                return fetchTrailer(itemId).then(function (data) {
                    return playResolvedClip(data, 'Trailer');
                });
            })
            .catch(function (err) {
                console.warn('[FullCrew] trailer request failed', err);
            })
            .then(function () {
                button.removeAttribute('data-busy');
                button.classList.remove('fullCrewBumperBusy');
            });
    }

    function insertAfterAnchor(row, button, anchor) {
        if (anchor && anchor.parentNode === row) {
            if (anchor.nextSibling) {
                row.insertBefore(button, anchor.nextSibling);
            } else {
                row.appendChild(button);
            }
            return;
        }
        row.appendChild(button);
    }

    function mountTrailerButton(row, itemId) {
        var nativeVisible = row.querySelector('.btnPlayTrailer:not(.hide)');
        var existing = row.querySelector('#' + TRAILER_BTN_ID);

        // Vanilla Jellyfin already shows Trailer — don't duplicate it.
        if (nativeVisible) {
            if (existing) {
                existing.remove();
            }
            return nativeVisible;
        }

        if (existing) {
            if (existing.getAttribute('data-item-id') === itemId) {
                return existing;
            }
            existing.remove();
        }

        var button = document.createElement('button');
        button.id = TRAILER_BTN_ID;
        button.type = 'button';
        button.className = 'button-flat btn detailButton fullCrewTrailerButton';
        button.setAttribute('data-item-id', itemId);
        button.setAttribute('title', 'Trailer');
        button.setAttribute('aria-label', 'Trailer');

        var content = document.createElement('div');
        content.className = 'detailButton-content';

        var icon = document.createElement('span');
        icon.className = 'material-icons detailButton-icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = 'theaters';

        var label = document.createElement('div');
        label.className = 'detailButton-text';
        label.textContent = 'Trailer';

        content.appendChild(icon);
        content.appendChild(label);
        button.appendChild(content);

        button.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            handleTrailerClick(itemId, button);
        });

        var play = row.querySelector('.btnPlay:not(.hide), .btnPlayState:not(.hide)');
        insertAfterAnchor(row, button, play);
        return button;
    }

    function mountBumperButton(row, itemId, trailerAnchor) {
        var existing = row.querySelector('#' + BUMPER_BTN_ID);
        if (existing) {
            if (existing.getAttribute('data-item-id') === itemId) {
                // Keep Bumper immediately after Trailer/Play if Trailer was restored later.
                if (trailerAnchor && existing.previousSibling !== trailerAnchor) {
                    insertAfterAnchor(row, existing, trailerAnchor);
                }
                return existing;
            }
            existing.remove();
        }

        var button = document.createElement('button');
        button.id = BUMPER_BTN_ID;
        button.type = 'button';
        button.className = 'button-flat btn detailButton fullCrewBumperButton';
        button.setAttribute('data-item-id', itemId);
        button.setAttribute('title', 'Play a nostalgia break bumper');
        button.setAttribute('aria-label', 'Break bumper');

        var content = document.createElement('div');
        content.className = 'detailButton-content';

        var icon = document.createElement('span');
        icon.className = 'material-icons detailButton-icon fullCrewBumperIcon';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = 'tv';

        var label = document.createElement('div');
        label.className = 'detailButton-text';
        label.textContent = 'Bumper';

        content.appendChild(icon);
        content.appendChild(label);
        button.appendChild(content);

        button.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            handleBumperClick(itemId, button);
        });

        var play = row.querySelector('.btnPlay:not(.hide), .btnPlayState:not(.hide)');
        insertAfterAnchor(row, button, trailerAnchor || play);
        return button;
    }

    function removeDetailButtons(view) {
        if (!view) {
            return;
        }
        var scope = view.querySelector('.mainDetailButtons') || view;
        Array.prototype.forEach.call(scope.querySelectorAll('#' + BUMPER_BTN_ID + ', #' + TRAILER_BTN_ID), function (el) {
            el.remove();
        });
    }

    function mountDetailButtons(view, itemId) {
        var row = view.querySelector('.mainDetailButtons');
        if (!row) {
            return;
        }

        getItemPromise(itemId).then(function (item) {
            if (!document.body.contains(row)) {
                return;
            }

            var type = item && (item.Type || item.type);
            // Bumper/Trailer only belong on playable media — not Person/Genre/etc.
            if (!isMediaDetailItemType(type)) {
                removeDetailButtons(view);
                return;
            }

            var trailer = mountTrailerButton(row, itemId);
            mountBumperButton(row, itemId, trailer);
        });
    }

    function scan() {
        var views = document.querySelectorAll('.itemDetailPage, .view:not(.hide), .mainAnimatedPage:not(.hide)');
        if (!views.length) {
            var page = document.querySelector('.itemDetailPage');
            if (page) {
                mount(page);
            }
            return;
        }

        Array.prototype.forEach.call(views, function (view) {
            if (view.classList.contains('hide')) {
                return;
            }
            mount(view);
        });
    }

    /* ================================================================== */
    /* Feature: Library Stats                                             */
    /* ================================================================== */

    var STATS_TAB_ID = 'fullCrewStatsTab';
    var STATS_PAGE_ID = 'fullCrewStatsPage';
    var STUDIO_PAGE_ID = 'fullCrewStudioPage';
    var STATS_HASH = '#/fullcrew/stats';
    var STATS_VIEW_KEY = 'fullCrew.statsViewMode';
    var STATS_DETAIL_VIEW_KEY = 'fullCrew.statsDetailViewMode';
    var PLUGIN_UNIQUE_ID = PLUGIN_GUID;
    var CHART_COLORS = [
        '#00a4dc',
        '#52b54b',
        '#e8a317',
        '#e57373',
        '#26a69a',
        '#ff7043',
        '#90a4ae',
        '#42a5f5',
        '#66bb6a',
        '#ffa726'
    ];

    var PEOPLE_CATEGORY_KEYS = {
        Actor: 'actors',
        Director: 'directors',
        Writer: 'writers',
        Creator: 'creators',
        Producer: 'producers',
        GuestStar: 'guestStars',
        Composer: 'composers',
        Editor: 'editors',
        Artist: 'artists',
        Author: 'authors',
        AlbumArtist: 'albumArtists',
        CoverArtist: 'coverArtists',
        Unknown: 'unknown'
    };

    var statsEnabled = true;
    var statsConfigLoaded = false;
    var statsConfigPromise = null;
    var statsFetchInFlight = null;
    var statsCachedData = null;
    var statsCategoryCache = {};
    var statsCategoryFetchInFlight = {};
    var statsBucketItemsCache = {};
    var statsBucketItemsFetchInFlight = {};
    var statsDetailBuckets = null;
    var statsDetailCategoryKey = null;

    var BUCKET_ITEMS_CATEGORY_KEYS = {
        types: 1, resolutions: 1, hdr: 1, videoCodecs: 1, audioChannels: 1, audioCodecs: 1,
        genres: 1, studios: 1, collections: 1, decades: 1, ratings: 1, community: 1, tags: 1, languages: 1
    };

    function normalizeStatsHash(hash) {
        return Core.normalizeHash(hash);
    }

    function parseStatsRoute() {
        var hash = normalizeStatsHash(window.location.hash || '');
        if (hash === STATS_HASH) {
            return { kind: 'overview' };
        }
        var itemsMatch = /^#\/fullcrew\/stats\/([^/]+)\/items\/(.+)$/.exec(hash);
        if (itemsMatch) {
            var catRaw = itemsMatch[1];
            var bucketRaw = itemsMatch[2];
            var cat; var bucket;
            try { cat = decodeURIComponent(catRaw); } catch (e1) { cat = catRaw; }
            try { bucket = decodeURIComponent(bucketRaw); } catch (e2) { bucket = bucketRaw; }
            return { kind: 'bucket', category: cat, bucket: bucket };
        }
        var match = /^#\/fullcrew\/stats\/([^/]+)$/.exec(hash);
        if (match) {
            try {
                return { kind: 'detail', category: decodeURIComponent(match[1]) };
            } catch (e) {
                return { kind: 'detail', category: match[1] };
            }
        }
        return null;
    }

    function isStatsRoute() {
        return parseStatsRoute() != null;
    }

    function statsDetailHash(categoryKey) {
        return STATS_HASH + '/' + encodeURIComponent(categoryKey);
    }

    function statsBucketItemsHash(categoryKey, bucketName) {
        return STATS_HASH + '/' + encodeURIComponent(categoryKey) + '/items/' + encodeURIComponent(String(bucketName || ''));
    }

    function categorySupportsBucketItems(categoryKey) {
        if (!categoryKey) { return false; }
        if (BUCKET_ITEMS_CATEGORY_KEYS[categoryKey]) { return true; }
        var values = Object.keys(PEOPLE_CATEGORY_KEYS).map(function (k) { return PEOPLE_CATEGORY_KEYS[k]; });
        return values.indexOf(categoryKey) !== -1;
    }

    function peopleCategoryKey(kind) {
        if (!kind) {
            return null;
        }
        if (PEOPLE_CATEGORY_KEYS[kind]) {
            return PEOPLE_CATEGORY_KEYS[kind];
        }
        var lower = String(kind).charAt(0).toLowerCase() + String(kind).slice(1);
        return lower + (lower.charAt(lower.length - 1) === 's' ? '' : 's');
    }

    function getStatsViewMode(forDetail) {
        var key = forDetail ? STATS_DETAIL_VIEW_KEY : STATS_VIEW_KEY;
        try {
            var stored = window.localStorage.getItem(key);
            if (stored === 'pie' || stored === 'bar' || stored === 'list') {
                return stored;
            }
        } catch (e) {
            /* ignore */
        }
        // Detail defaults to list for long rankings; overview prefers labeled bars.
        return forDetail ? 'list' : 'bar';
    }

    function setStatsViewMode(mode, forDetail) {
        if (mode !== 'pie' && mode !== 'bar' && mode !== 'list') {
            mode = forDetail ? 'list' : 'bar';
        }
        try {
            window.localStorage.setItem(forDetail ? STATS_DETAIL_VIEW_KEY : STATS_VIEW_KEY, mode);
        } catch (e) {
            /* ignore */
        }
        return mode;
    }

    function prop(obj, pascal, camel) {
        return Core.prop(obj, pascal, camel);
    }

    function normalizeBuckets(list) {
        return (list || []).map(function (item) {
            var childrenRaw = prop(item, 'Children', 'children') || prop(item, 'Branches', 'branches') || [];
            var children = normalizeBuckets(childrenRaw);
            return {
                name: prop(item, 'Name', 'name') || 'Unknown',
                count: Number(prop(item, 'Count', 'count')) || 0,
                percent: Number(prop(item, 'Percent', 'percent')) || 0,
                itemId: prop(item, 'ItemId', 'itemId') || prop(item, 'Id', 'id') || null,
                itemType: prop(item, 'ItemType', 'itemType') || null,
                children: children
            };
        }).filter(function (item) {
            return item.count > 0 || item.percent > 0;
        });
    }

    /** Soft-truncate only extremely long labels; never clip to a single character. */
    function displayBucketName(name) {
        var text = String(name == null ? '' : name).trim() || 'Unknown';
        if (text.length <= 40) {
            return text;
        }
        return text.slice(0, 39) + '\u2026';
    }

    function detailsHashForItem(itemId) {
        return Core.detailsHashForItem(itemId);
    }

    function studioPageHash(bucket) {
        var name = encodeURIComponent(String(bucket.name || 'studio'));
        var parts = [];
        if (bucket.itemId) {
            parts.push('id=' + encodeURIComponent(String(bucket.itemId)));
        }
        if (bucket.children && bucket.children.length) {
            parts.push(
                'branches=' +
                    bucket.children
                        .map(function (c) {
                            return encodeURIComponent(String(c.name || ''));
                        })
                        .filter(Boolean)
                        .join(',')
            );
        }
        return '#/fullcrew/studio/' + name + (parts.length ? '?' + parts.join('&') : '');
    }

    function isStudioBucket(bucket) {
        if (!bucket) {
            return false;
        }
        if (String(bucket.itemType || '') === 'Studio') {
            return true;
        }
        // Clustered parents always carry children (studios-only for now).
        return !!(bucket.children && bucket.children.length);
    }

    /**
     * Navigate to a Jellyfin entity page — delegates to Core (Emby.Page / appRouter / hash).
     */
    function navigateToItem(itemId) {
        return Core.navigateToItem(itemId);
    }

    function createBucketNameEl(tagName, className, bucket, categoryKey) {
        var label = displayBucketName(bucket.name);
        var el;

        if (isStudioBucket(bucket)) {
            el = createElement('a', className + ' fullCrewStatsItemLink', label);
            el.href = studioPageHash(bucket);
            el.setAttribute('title', bucket.name + ' — studio page');
            el.addEventListener('click', function (ev) {
                if (ev.defaultPrevented || ev.button !== 0 || ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) {
                    return;
                }
                ev.preventDefault();
                window.location.hash = studioPageHash(bucket);
            });
            return el;
        }

        var itemId = bucket.itemId;
        if (itemId) {
            el = createElement('a', className + ' fullCrewStatsItemLink', label);
            el.href = detailsHashForItem(itemId);
            el.setAttribute('title', bucket.name + ' — open in Jellyfin');
            el.addEventListener('click', function (ev) {
                if (ev.defaultPrevented || ev.button !== 0 || ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) {
                    return;
                }
                if (navigateToItem(itemId)) {
                    ev.preventDefault();
                }
            });
            return el;
        }

        if (categoryKey && String(bucket.name || '') === 'Other') {
            el = createElement('a', className + ' fullCrewStatsItemLink', label);
            el.href = statsDetailHash(categoryKey);
            el.setAttribute('title', 'View full ' + categoryKey + ' list');
            el.addEventListener('click', function (ev) {
                if (ev.defaultPrevented || ev.button !== 0 || ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) {
                    return;
                }
                ev.preventDefault();
                window.location.hash = statsDetailHash(categoryKey);
            });
            return el;
        }

        if (categoryKey && categorySupportsBucketItems(categoryKey) && bucket.name) {
            var itemsHash = statsBucketItemsHash(categoryKey, bucket.name);
            el = createElement('a', className + ' fullCrewStatsItemLink', label);
            el.href = itemsHash;
            el.setAttribute('title', bucket.name + ' — list titles in this bucket');
            el.addEventListener('click', function (ev) {
                if (ev.defaultPrevented || ev.button !== 0 || ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) {
                    return;
                }
                ev.preventDefault();
                window.location.hash = itemsHash;
            });
            return el;
        }

        el = createElement(tagName, className, label);
        if (label !== bucket.name) {
            el.title = bucket.name;
        }
        return el;
    }

    function bucketHasChildren(bucket) {
        return !!(bucket && bucket.children && bucket.children.length);
    }

    function createClusterToggle(expanded) {
        var btn = createElement('button', 'fullCrewStatsClusterToggle');
        btn.type = 'button';
        btn.setAttribute('aria-expanded', expanded ? 'true' : 'false');
        btn.setAttribute('aria-label', expanded ? 'Collapse studio variants' : 'Expand studio variants');
        btn.appendChild(createElement('span', 'fullCrewStatsClusterChevron', expanded ? '▾' : '▸'));
        return btn;
    }

    function appendClusteredRankItem(list, bucket, depth, categoryKey) {
        var hasKids = bucketHasChildren(bucket);
        var li = createElement('li', 'fullCrewStatsRankItem' + (hasKids ? ' fullCrewStatsRankItem--cluster' : '') + (depth ? ' fullCrewStatsRankItem--child' : ''));
        if (depth) {
            li.style.paddingLeft = Math.min(1.5, depth * 1.1) + 'rem';
        }

        var row = createElement('div', 'fullCrewStatsRankRow');
        var nameWrap = createElement('div', 'fullCrewStatsRankNameWrap');
        var childList = null;
        var toggle = null;

        if (hasKids) {
            toggle = createClusterToggle(false);
            nameWrap.appendChild(toggle);
        }

        nameWrap.appendChild(createBucketNameEl('span', 'fullCrewStatsRankName', bucket, categoryKey));
        row.appendChild(nameWrap);
        row.appendChild(
            createElement(
                'span',
                'fullCrewStatsRankMeta',
                bucket.count + ' — ' + formatPercent(bucket.percent)
                    + (hasKids ? ' · ' + bucket.children.length + ' variants' : '')
            )
        );
        li.appendChild(row);

        if (hasKids) {
            childList = createElement('ol', 'fullCrewStatsRankList fullCrewStatsRankList--nested is-collapsed');
            bucket.children.forEach(function (child) {
                appendClusteredRankItem(childList, child, (depth || 0) + 1, categoryKey);
            });
            li.appendChild(childList);
            toggle.addEventListener('click', function (ev) {
                ev.preventDefault();
                ev.stopPropagation();
                var open = childList.classList.contains('is-collapsed');
                childList.classList.toggle('is-collapsed', !open);
                toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
                toggle.setAttribute('aria-label', open ? 'Collapse studio variants' : 'Expand studio variants');
                var chevron = toggle.querySelector('.fullCrewStatsClusterChevron');
                if (chevron) {
                    chevron.textContent = open ? '▾' : '▸';
                }
                li.classList.toggle('is-expanded', open);
            });
        }

        list.appendChild(li);
    }

    function appendClusteredBarRow(bars, bucket, max, depth, colorIndex, categoryKey) {
        var hasKids = bucketHasChildren(bucket);
        var wrap = createElement('div', 'fullCrewStatsBarCluster' + (depth ? ' fullCrewStatsBarCluster--child' : ''));
        var row = createElement('div', 'fullCrewStatsBarRow');
        var labelWrap = createElement('div', 'fullCrewStatsBarLabelWrap');
        var childHost = null;
        var toggle = null;

        if (hasKids) {
            toggle = createClusterToggle(false);
            labelWrap.appendChild(toggle);
        }

        labelWrap.appendChild(createBucketNameEl('div', 'fullCrewStatsBarLabel', bucket, categoryKey));
        row.appendChild(labelWrap);

        var track = createElement('div', 'fullCrewStatsBarTrack');
        var fill = createElement('div', 'fullCrewStatsBarFill');
        fill.style.width = Math.max(2, (bucket.count / max) * 100) + '%';
        fill.style.background = CHART_COLORS[colorIndex % CHART_COLORS.length];
        track.appendChild(fill);
        row.appendChild(track);
        row.appendChild(
            createElement(
                'div',
                'fullCrewStatsBarValue',
                bucket.count + ' — ' + formatPercent(bucket.percent)
            )
        );
        wrap.appendChild(row);

        if (hasKids) {
            childHost = createElement('div', 'fullCrewStatsBarChildren is-collapsed');
            bucket.children.forEach(function (child, idx) {
                appendClusteredBarRow(childHost, child, max, (depth || 0) + 1, colorIndex, categoryKey);
            });
            wrap.appendChild(childHost);
            toggle.addEventListener('click', function (ev) {
                ev.preventDefault();
                ev.stopPropagation();
                var open = childHost.classList.contains('is-collapsed');
                childHost.classList.toggle('is-collapsed', !open);
                toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
                var chevron = toggle.querySelector('.fullCrewStatsClusterChevron');
                if (chevron) {
                    chevron.textContent = open ? '▾' : '▸';
                }
            });
        }

        bars.appendChild(wrap);
    }

    function fetchPluginConfig() {
        var client = apiClient();
        if (!client || typeof client.getPluginConfiguration !== 'function') {
            return Promise.resolve(null);
        }
        return Promise.resolve(client.getPluginConfiguration(PLUGIN_UNIQUE_ID)).catch(function () {
            return null;
        });
    }

    function refreshStatsEnabled() {
        if (statsConfigPromise) {
            return statsConfigPromise;
        }
        statsConfigPromise = fetchPluginConfig().then(function (config) {
            statsConfigLoaded = true;
            if (!config) {
                statsEnabled = true;
                return statsEnabled;
            }
            var flag = prop(config, 'EnableLibraryStats', 'enableLibraryStats');
            statsEnabled = flag !== false;
            return statsEnabled;
        });
        return statsConfigPromise;
    }

    function fetchStats() {
        if (statsCachedData) {
            return Promise.resolve(statsCachedData);
        }
        if (statsFetchInFlight) {
            return statsFetchInFlight;
        }

        statsFetchInFlight = Core.getJson('FullCrew/stats')
            .then(function (data) {
                statsCachedData = data;
                statsFetchInFlight = null;
                return data;
            })
            .catch(function (err) {
                statsFetchInFlight = null;
                throw err;
            });

        return statsFetchInFlight;
    }

    function fetchStatsCategory(category) {
        var key = String(category || '');
        if (statsCategoryCache[key]) {
            return Promise.resolve(statsCategoryCache[key]);
        }
        if (statsCategoryFetchInFlight[key]) {
            return statsCategoryFetchInFlight[key];
        }

        statsCategoryFetchInFlight[key] = Core.getJson('FullCrew/stats/' + encodeURIComponent(key))
            .then(function (data) {
                statsCategoryCache[key] = data;
                delete statsCategoryFetchInFlight[key];
                return data;
            })
            .catch(function (err) {
                delete statsCategoryFetchInFlight[key];
                throw err;
            });

        return statsCategoryFetchInFlight[key];
    }

    function fetchStatsBucketItems(category, bucket) {
        var cat = String(category || '');
        var name = String(bucket || '');
        var cacheKey = cat + '\0' + name;
        if (statsBucketItemsCache[cacheKey]) {
            return Promise.resolve(statsBucketItemsCache[cacheKey]);
        }
        if (statsBucketItemsFetchInFlight[cacheKey]) {
            return statsBucketItemsFetchInFlight[cacheKey];
        }
        var path = 'FullCrew/stats/' + encodeURIComponent(cat) + '/items?bucket=' + encodeURIComponent(name);
        statsBucketItemsFetchInFlight[cacheKey] = Core.getJson(path)
            .then(function (data) {
                statsBucketItemsCache[cacheKey] = data;
                delete statsBucketItemsFetchInFlight[cacheKey];
                return data;
            })
            .catch(function (err) {
                delete statsBucketItemsFetchInFlight[cacheKey];
                throw err;
            });
        return statsBucketItemsFetchInFlight[cacheKey];
    }

    function findFavouritesTab(root) {
        var buttons = (root || document).querySelectorAll('.headerTabs .emby-tab-button, .headerTabs button, .headerTabs a');
        for (var i = 0; i < buttons.length; i++) {
            var el = buttons[i];
            if (el.id === STATS_TAB_ID) {
                continue;
            }
            var text = tabLabelText(el);
            if (/^favou?rites$/i.test(text)) {
                return el;
            }
        }
        return null;
    }

    function findHomeTab(root) {
        var buttons = (root || document).querySelectorAll('.headerTabs .emby-tab-button, .headerTabs button, .headerTabs a');
        for (var i = 0; i < buttons.length; i++) {
            var el = buttons[i];
            if (el.id === STATS_TAB_ID) {
                continue;
            }
            var text = tabLabelText(el);
            if (/^home$/i.test(text)) {
                return el;
            }
        }
        return null;
    }

    function tabLabelText(el) {
        if (!el) {
            return '';
        }
        var fg = el.querySelector('.emby-button-foreground');
        var clone = (fg || el).cloneNode(true);
        Array.prototype.forEach.call(clone.querySelectorAll('.material-icons, .material-symbols-outlined'), function (icon) {
            if (icon.parentNode) {
                icon.parentNode.removeChild(icon);
            }
        });
        return (clone.textContent || '').replace(/\s+/g, ' ').trim();
    }

    function detectNativeTabIcon(sampleTab, fallback) {
        if (!sampleTab) {
            return { name: fallback, className: 'material-icons' };
        }
        var icon = sampleTab.querySelector('.material-icons, .material-symbols-outlined');
        if (!icon) {
            return { name: fallback, className: 'material-icons' };
        }
        return {
            name: fallback,
            className: icon.className || 'material-icons'
        };
    }

    function buildTabForeground(label, iconSpec) {
        var fg = document.createElement('div');
        fg.className = 'emby-button-foreground';
        if (iconSpec && iconSpec.name) {
            var icon = document.createElement('span');
            icon.className = iconSpec.className || 'material-icons';
            icon.setAttribute('aria-hidden', 'true');
            icon.textContent = iconSpec.name;
            fg.appendChild(icon);
            fg.appendChild(document.createTextNode(' '));
        }
        fg.appendChild(document.createTextNode(label));
        return fg;
    }

    function styleStatsTabLikeNative(tab, sampleTab, selected) {
        if (!tab) {
            return tab;
        }

        var wantSelected = selected ? '1' : '0';
        if (
            tab.getAttribute('data-fullcrew-styled') === '1' &&
            tab.getAttribute('data-fullcrew-selected') === wantSelected &&
            tab.querySelector('.emby-button-foreground')
        ) {
            if (selected) {
                tab.classList.add('emby-tab-button-active');
            } else {
                tab.classList.remove('emby-tab-button-active');
            }
            return tab;
        }

        var classes = 'emby-tab-button emby-button';
        if (sampleTab && sampleTab.className) {
            classes = String(sampleTab.className)
                .replace(/\bemby-tab-button-active\b/g, '')
                .replace(/\blastFocused\b/g, '')
                .replace(/\s+/g, ' ')
                .trim();
            if (classes.indexOf('emby-tab-button') === -1) {
                classes += ' emby-tab-button';
            }
            if (classes.indexOf('emby-button') === -1) {
                classes += ' emby-button';
            }
        }
        if (selected) {
            classes += ' emby-tab-button-active';
        }
        tab.className = classes.replace(/\s+/g, ' ').trim();

        if (sampleTab && sampleTab.getAttribute('is')) {
            tab.setAttribute('is', sampleTab.getAttribute('is'));
        } else if (!tab.getAttribute('is')) {
            tab.setAttribute('is', 'emby-button');
        }

        if (sampleTab) {
            Array.prototype.forEach.call(sampleTab.attributes, function (attr) {
                if (!attr || !attr.name) {
                    return;
                }
                if (attr.name === 'id' || attr.name === 'href' || attr.name === 'class' || attr.name === 'data-index') {
                    return;
                }
                if (attr.name.indexOf('data-fullcrew') === 0) {
                    return;
                }
                if (!tab.hasAttribute(attr.name)) {
                    tab.setAttribute(attr.name, attr.value);
                }
            });
        }

        var iconSpec = detectNativeTabIcon(sampleTab, 'insights');
        tab.innerHTML = '';
        tab.appendChild(buildTabForeground('Stats', iconSpec));
        tab.setAttribute('data-fullcrew-styled', '1');
        tab.setAttribute('data-fullcrew-selected', wantSelected);
        return tab;
    }

    function createStatsTabElement(sampleTab, selected) {
        var tag = sampleTab && sampleTab.tagName ? sampleTab.tagName.toLowerCase() : 'button';
        var tab = document.createElement(tag === 'a' ? 'a' : 'button');
        if (tag === 'a') {
            tab.href = STATS_HASH;
        } else {
            tab.type = 'button';
        }
        tab.id = STATS_TAB_ID;
        tab.setAttribute('data-fullcrew-stats-tab', '1');
        tab.setAttribute('title', 'Stats');
        tab.setAttribute('aria-label', 'Stats');
        styleStatsTabLikeNative(tab, sampleTab, selected);
        tab.addEventListener('click', onStatsTabClick, true);
        return tab;
    }

    function getTabSlider() {
        var headerTabs = document.querySelector('.headerTabs');
        if (!headerTabs) {
            return null;
        }
        return headerTabs.querySelector('.emby-tabs-slider') || headerTabs.querySelector('[is="emby-tabs"]') || headerTabs;
    }

    function ensureHeaderTabsVisible() {
        var headerTabs = document.querySelector('.headerTabs');
        if (headerTabs) {
            headerTabs.classList.remove('hide');
        }
        document.body.classList.add('withSectionTabs');
    }

    function setCustomDocumentTitle(active, titleText) {
        if (active) {
            Core.PageTitle.claim(titleText);
        } else {
            Core.PageTitle.release();
        }
    }

    function setStatsDocumentTitle(active) {
        setCustomDocumentTitle(active, 'Stats');
    }

    function buildStandaloneStatsTabs(slider) {
        slider.innerHTML = '';

        var sample = findFavouritesTab() || findHomeTab();

        function makeTab(id, label, href, active, iconName) {
            var btn = document.createElement(href ? 'a' : 'button');
            if (href) {
                btn.href = href;
            } else {
                btn.type = 'button';
            }
            if (id) {
                btn.id = id;
            }
            var classes = 'emby-tab-button emby-button';
            if (sample && sample.className) {
                classes = String(sample.className)
                    .replace(/\bemby-tab-button-active\b/g, '')
                    .replace(/\blastFocused\b/g, '')
                    .replace(/\s+/g, ' ')
                    .trim();
            }
            if (active) {
                classes += ' emby-tab-button-active';
            }
            btn.className = classes.replace(/\s+/g, ' ').trim();
            if (sample && sample.getAttribute('is')) {
                btn.setAttribute('is', sample.getAttribute('is'));
            } else {
                btn.setAttribute('is', 'emby-button');
            }
            var iconSpec = detectNativeTabIcon(sample, iconName);
            iconSpec.name = iconName;
            btn.appendChild(buildTabForeground(label, iconSpec));
            return btn;
        }

        var home = makeTab(null, 'Home', '#/home.html', false, 'home');
        var fav = makeTab(null, 'Favourites', '#/home.html', false, 'favorite');
        fav.addEventListener('click', function () {
            try {
                window.sessionStorage.setItem('fullCrew.openFavorites', '1');
            } catch (e) {
                /* ignore */
            }
        });
        var stats = makeTab(STATS_TAB_ID, 'Stats', STATS_HASH, true, 'insights');
        stats.setAttribute('data-fullcrew-stats-tab', '1');
        stats.setAttribute('data-fullcrew-styled', '1');
        stats.setAttribute('data-fullcrew-selected', '1');
        stats.addEventListener('click', onStatsTabClick);

        slider.appendChild(home);
        slider.appendChild(fav);
        slider.appendChild(stats);
    }

    function onStatsTabClick(e) {
        if (e) {
            e.preventDefault();
            e.stopPropagation();
            if (typeof e.stopImmediatePropagation === 'function') {
                e.stopImmediatePropagation();
            }
        }
        if (window.location.hash !== STATS_HASH) {
            window.location.hash = STATS_HASH;
        } else {
            syncStatsUi();
        }
    }

    function wireNativeTabExit(tab) {
        if (!tab || tab.getAttribute('data-fullcrew-exit') === '1') {
            return;
        }
        tab.setAttribute('data-fullcrew-exit', '1');
        tab.addEventListener('click', function () {
            if (isStatsRoute()) {
                // Native home tabs will navigate away; ensure our page tears down on hashchange.
                window.setTimeout(function () {
                    if (!isStatsRoute()) {
                        tearDownStatsPage();
                    }
                }, 50);
            }
        }, true);
    }

    function injectStatsTab() {
        if (!statsEnabled) {
            removeStatsTab();
            return;
        }

        ensureStyles();
        ensureHeaderTabsVisible();

        var existing = document.getElementById(STATS_TAB_ID);
        if (isStatsRoute()) {
            var slider = getTabSlider();
            if (!slider) {
                return;
            }
            if (!existing || !findFavouritesTab(slider)) {
                // Home view tore down native tabs — rebuild a matching strip.
                var headerTabs = document.querySelector('.headerTabs');
                if (headerTabs && !headerTabs.querySelector('[is="emby-tabs"], .emby-tabs-slider')) {
                    headerTabs.innerHTML =
                        '<div is="emby-tabs" class="tabs-viewmenubar emby-tabs">' +
                        '<div class="emby-tabs-slider" style="white-space:nowrap;"></div></div>';
                    slider = headerTabs.querySelector('.emby-tabs-slider');
                } else {
                    slider = getTabSlider();
                }
                if (slider && (!existing || !document.body.contains(existing))) {
                    buildStandaloneStatsTabs(slider);
                    existing = document.getElementById(STATS_TAB_ID);
                }
            } else if (existing) {
                styleStatsTabLikeNative(existing, findFavouritesTab(slider) || findHomeTab(slider), true);
            }
            setStatsTabSelected(true);
            setStatsDocumentTitle(true);
            return;
        }

        if (existing && document.body.contains(existing)) {
            styleStatsTabLikeNative(existing, findFavouritesTab() || findHomeTab(), false);
            setStatsTabSelected(false);
            // Do not touch document title on normal Home/Favourites — avoids Stats blink.
            if (Core.PageTitle.isOwned() && !isStudioRoute()) {
                setStatsDocumentTitle(false);
            }
            wireNativeTabExit(findHomeTab());
            wireNativeTabExit(findFavouritesTab());
            return;
        }

        var fav = findFavouritesTab();
        if (!fav || !fav.parentNode) {
            return;
        }

        var tab = createStatsTabElement(fav, false);

        if (fav.nextSibling) {
            fav.parentNode.insertBefore(tab, fav.nextSibling);
        } else {
            fav.parentNode.appendChild(tab);
        }

        wireNativeTabExit(findHomeTab());
        wireNativeTabExit(fav);
        setStatsTabSelected(false);
    }

    function removeStatsTab() {
        var tab = document.getElementById(STATS_TAB_ID);
        if (tab && tab.parentNode) {
            tab.parentNode.removeChild(tab);
        }
        setStatsDocumentTitle(false);
    }

    function setStatsTabSelected(selected) {
        var tab = document.getElementById(STATS_TAB_ID);
        if (!tab) {
            return;
        }

        var buttons = document.querySelectorAll('.headerTabs .emby-tab-button');
        Array.prototype.forEach.call(buttons, function (btn) {
            if (btn.id === STATS_TAB_ID) {
                return;
            }
            if (selected) {
                btn.classList.remove('emby-tab-button-active');
            }
        });

        if (selected) {
            tab.classList.add('emby-tab-button-active');
        } else {
            tab.classList.remove('emby-tab-button-active');
        }
    }

    function findStatsMountPoint() {
        return Core.findMount();
    }

    function tearDownStatsPage() {
        // Do not clear ROUTE_PENDING here — Stats→Studio relies on pending chrome
        // staying up through this teardown until the studio shell is attached.
        document.documentElement.classList.remove(STATS_BODY_CLASS);
        document.body.classList.remove(STATS_BODY_CLASS);
        var page = document.getElementById(STATS_PAGE_ID);
        if (page && page.parentNode) {
            page.parentNode.removeChild(page);
        }
        statsDetailBuckets = null;
        setStatsTabSelected(false);
        if (!isStudioRoute()) {
            setStatsDocumentTitle(false);
        }
    }

    function mountStatsPage() {
        if (!statsEnabled) {
            tearDownStatsPage();
            removeStatsTab();
            return;
        }

        var route = parseStatsRoute();
        if (!route) {
            tearDownStatsPage();
            return;
        }

        ensureStyles();
        applyRouteChrome(peekFullCrewRoute(), { pending: false, setTitle: false });
        ensureHeaderTabsVisible();
        injectStatsTab();
        setStatsTabSelected(true);
        setStatsDocumentTitle(true);

        document.body.classList.add(STATS_BODY_CLASS);
        document.documentElement.classList.add(STATS_BODY_CLASS);
        clearRoutePending();

        var routeKey =
            route.kind === 'bucket'
                ? 'bucket:' + route.category + ':' + route.bucket
                : route.kind === 'detail'
                  ? 'detail:' + route.category
                  : 'overview';
        var mount = findStatsMountPoint();
        var page = document.getElementById(STATS_PAGE_ID);
        if (page && page.getAttribute('data-route') === routeKey) {
            // Already on the correct shell — don't remount (avoids blink).
            if (page.getAttribute('data-loaded') === '1' || page.getAttribute('data-loading') === '1') {
                return;
            }
        }

        if (page && page.parentNode) {
            page.parentNode.removeChild(page);
            page = null;
        }
        statsDetailBuckets = null;
        statsDetailCategoryKey = null;

        if (route.kind === 'bucket') {
            page = createStatsBucketShell(route.category, route.bucket);
            page.setAttribute('data-route', routeKey);
            page.setAttribute('data-loading', '1');
            mount.appendChild(page);
            Core.bindPageLoad(page, fetchStatsBucketItems(route.category, route.bucket), {
                stillValid: isStatsRoute,
                warn: '[FullCrew] failed to load stats bucket items ' + route.category + '/' + route.bucket,
                onSuccess: function (data) {
                    renderStatsBucketContent(page, data, route);
                },
                onError: function () {
                    var body = page.querySelector('.fullCrewStatsBody');
                    if (body) {
                        body.innerHTML = '';
                        body.appendChild(
                            createElement(
                                'div',
                                'fullCrewStatsStatus fullCrewStatsStatus--error',
                                'Could not load titles for this bucket.'
                            )
                        );
                    }
                    var titleEl = page.querySelector('.fullCrewStatsTitle');
                    if (titleEl) {
                        titleEl.textContent = route.bucket;
                    }
                }
            });
            return;
        }

        if (route.kind === 'detail') {
            page = createStatsDetailShell(route.category);
            page.setAttribute('data-route', routeKey);
            page.setAttribute('data-loading', '1');
            mount.appendChild(page);
            Core.bindPageLoad(page, fetchStatsCategory(route.category), {
                stillValid: isStatsRoute,
                warn: '[FullCrew] failed to load stats category ' + route.category,
                onSuccess: function (data) {
                    renderStatsDetailContent(page, data);
                },
                onError: function () {
                    var body = page.querySelector('.fullCrewStatsBody');
                    if (body) {
                        body.innerHTML = '';
                        body.appendChild(
                            createElement(
                                'div',
                                'fullCrewStatsStatus fullCrewStatsStatus--error',
                                'Could not load this category. It may be unknown or the plugin API is unavailable.'
                            )
                        );
                    }
                    var titleEl = page.querySelector('.fullCrewStatsTitle');
                    if (titleEl) {
                        titleEl.textContent = route.category;
                    }
                }
            });
            return;
        }

        page = createStatsPageShell();
        page.setAttribute('data-route', routeKey);
        page.setAttribute('data-loading', '1');
        mount.appendChild(page);

        Core.bindPageLoad(page, fetchStats(), {
            stillValid: isStatsRoute,
            warn: '[FullCrew] failed to load library stats',
            onSuccess: function (data) {
                renderStatsContent(page, data);
            },
            errorMessage: 'Could not load library stats. Is the plugin API available?'
        });
    }

    function maybeOpenFavoritesFromFlag() {
        try {
            if (window.sessionStorage.getItem('fullCrew.openFavorites') !== '1') {
                return;
            }
            window.sessionStorage.removeItem('fullCrew.openFavorites');
        } catch (e) {
            return;
        }

        window.setTimeout(function () {
            var fav = findFavouritesTab();
            if (fav) {
                fav.click();
            }
        }, 400);
    }

    /** True when the overlay for this Full Crew route is already in the DOM. */
    function routePageMounted(route) {
        if (!route) {
            return false;
        }
        if (route.kind === 'studio') {
            return !!document.getElementById(STUDIO_PAGE_ID);
        }
        if (route.kind === 'stats') {
            return !!document.getElementById(STATS_PAGE_ID);
        }
        return !!(document.getElementById(STATS_PAGE_ID) || document.getElementById(STUDIO_PAGE_ID));
    }

    function claimRouteTitle(route) {
        if (!route) {
            return;
        }
        if (route.kind === 'studio') {
            setCustomDocumentTitle(true, route.title || 'Studio');
        } else if (route.kind === 'stats') {
            setStatsDocumentTitle(true);
        } else if (route.title) {
            setCustomDocumentTitle(true, route.title);
        }
    }

    function syncStatsUi() {
        var early = peekFullCrewRoute();
        if (early) {
            // Pending until *this* route's page exists — not "any" Full Crew page.
            // Otherwise Stats→Studio clears pending while Stats is still mounted, then
            // tearDownStats leaves a frame of Jellyfin 404 chrome (the studio blink).
            applyRouteChrome(early, { pending: !routePageMounted(early), setTitle: true });
            claimRouteTitle(early);
        } else {
            applyRouteChrome(null);
            // Drop title ownership immediately so Home never keeps "Stats"/"Jellyfin" stickers.
            Core.PageTitle.release();
        }

        if (!statsConfigLoaded) {
            // Optimistic mount: default EnableLibraryStats is true — don't wait on API.
            if (early && statsEnabled) {
                if (early.kind === 'studio') {
                    mountStudioPage();
                } else if (early.kind === 'stats') {
                    mountStatsPage();
                }
            } else {
                injectStatsTab();
            }
            refreshStatsEnabled().then(syncStatsUi);
            return;
        }

        if (!statsEnabled) {
            tearDownStatsPage();
            tearDownStudioPage();
            removeStatsTab();
            applyRouteChrome(null);
            Core.PageTitle.release();
            return;
        }

        if (isStudioRoute()) {
            tearDownStatsPage();
            mountStudioPage();
            return;
        }

        tearDownStudioPage();

        if (isStatsRoute()) {
            mountStatsPage();
            return;
        }

        tearDownStatsPage();
        injectStatsTab();
        maybeOpenFavoritesFromFlag();
    }

    function formatPercent(value) {
        var n = Number(value);
        if (!isFinite(n)) {
            return '0%';
        }
        if (Math.abs(n - Math.round(n)) < 0.05) {
            return Math.round(n) + '%';
        }
        return n.toFixed(1) + '%';
    }

    function formatRuntimeTicks(ticks) {
        var n = Number(ticks);
        if (!isFinite(n) || n <= 0) {
            return null;
        }
        var totalMinutes = Math.round(n / 600000000);
        if (totalMinutes < 60) {
            return totalMinutes + 'm';
        }
        var hours = Math.floor(totalMinutes / 60);
        var minutes = totalMinutes % 60;
        if (hours < 48) {
            return minutes ? hours + 'h ' + minutes + 'm' : hours + 'h';
        }
        var days = Math.floor(hours / 24);
        var remHours = hours % 24;
        return remHours ? days + 'd ' + remHours + 'h' : days + 'd';
    }

    function collectStatsSections(data) {
        var sections = [
            { title: 'Types', categoryKey: 'types', keyPascal: 'Types', keyCamel: 'types' },
            { title: 'Resolutions', categoryKey: 'resolutions', keyPascal: 'Resolutions', keyCamel: 'resolutions' },
            { title: 'HDR / range', categoryKey: 'hdr', keyPascal: 'VideoRanges', keyCamel: 'videoRanges' },
            { title: 'Video codecs', categoryKey: 'videoCodecs', keyPascal: 'VideoCodecs', keyCamel: 'videoCodecs' },
            { title: 'Audio channels', categoryKey: 'audioChannels', keyPascal: 'AudioChannels', keyCamel: 'audioChannels' },
            { title: 'Audio codecs', categoryKey: 'audioCodecs', keyPascal: 'AudioCodecs', keyCamel: 'audioCodecs' },
            { title: 'Genres', categoryKey: 'genres', keyPascal: 'Genres', keyCamel: 'genres', hint: 'Share of genre tags · top names (full list via title)' },
            { title: 'Studios', categoryKey: 'studios', keyPascal: 'Studios', keyCamel: 'studios', hint: 'Share of studio credits · top names (full list via title)' },
            { title: 'Collections', categoryKey: 'collections', keyPascal: 'Collections', keyCamel: 'collections', hint: 'Share of collection memberships' },
            { title: 'Years', categoryKey: 'decades', keyPascal: 'Decades', keyCamel: 'decades' },
            { title: 'Official ratings', categoryKey: 'ratings', keyPascal: 'OfficialRatings', keyCamel: 'officialRatings' },
            { title: 'Community scores', categoryKey: 'community', keyPascal: 'CommunityRatings', keyCamel: 'communityRatings' },
            { title: 'Tags', categoryKey: 'tags', keyPascal: 'Tags', keyCamel: 'tags', hint: 'Share of tag assignments · top names (full list via title)' },
            { title: 'Languages', categoryKey: 'languages', keyPascal: 'Languages', keyCamel: 'languages' }
        ];

        var peopleGroups = prop(data, 'PeopleByRole', 'peopleByRole') || [];
        peopleGroups.forEach(function (group) {
            var role = prop(group, 'Role', 'role') || prop(group, 'Kind', 'kind') || 'People';
            var kind = prop(group, 'Kind', 'kind') || role;
            var people = normalizeBuckets(prop(group, 'People', 'people'));
            if (!people.length) {
                return;
            }
            sections.push({
                title: 'Top ' + role,
                buckets: people,
                keyPascal: 'People:' + kind,
                categoryKey: peopleCategoryKey(kind),
                hint: 'Each movie/series counts once (not per episode) · share of ' + String(role).toLowerCase() + ' credits'
            });
        });

        return sections;
    }

    /**
     * Overview: keep API buckets, but never show a broken/dominating Other.
     * Detail pages pass omitOther=false so the full ranked list is shown as-is.
     */
    function chartDisplayBuckets(buckets, omitDominantOther) {
        var items = (buckets || []).slice();
        if (!omitDominantOther || !items.length) {
            return items;
        }

        var other = null;
        for (var i = 0; i < items.length; i++) {
            if (String(items[i].name || '') === 'Other') {
                other = items[i];
                break;
            }
        }
        if (!other) {
            return items;
        }

        var otherPct = Number(other.percent) || 0;
        if (otherPct > 40 || otherPct > 100) {
            return items.filter(function (b) {
                return String(b.name || '') !== 'Other';
            });
        }

        return items;
    }

    function drawPieChart(canvas, buckets) {
        var ctx = canvas.getContext('2d');
        var dpr = window.devicePixelRatio || 1;
        var size = 180;
        canvas.width = size * dpr;
        canvas.height = size * dpr;
        canvas.style.width = size + 'px';
        canvas.style.height = size + 'px';
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

        var total = buckets.reduce(function (sum, b) {
            return sum + (b.count || 0);
        }, 0);
        if (!total) {
            ctx.clearRect(0, 0, size, size);
            return;
        }

        var cx = size / 2;
        var cy = size / 2;
        var radius = size / 2 - 4;
        var start = -Math.PI / 2;

        buckets.forEach(function (bucket, index) {
            var slice = (bucket.count / total) * Math.PI * 2;
            ctx.beginPath();
            ctx.moveTo(cx, cy);
            ctx.arc(cx, cy, radius, start, start + slice);
            ctx.closePath();
            ctx.fillStyle = CHART_COLORS[index % CHART_COLORS.length];
            ctx.fill();
            start += slice;
        });

        // Inner hole for a subtle donut look that fits dark UI.
        ctx.beginPath();
        ctx.arc(cx, cy, radius * 0.48, 0, Math.PI * 2);
        ctx.fillStyle = 'rgba(16, 16, 16, 0.92)';
        ctx.fill();
    }

    function renderChartInto(container, buckets, mode, omitDominantOther, categoryKey) {
        container.innerHTML = '';
        var items = chartDisplayBuckets(buckets, !!omitDominantOther);
        if (!items.length) {
            container.appendChild(createElement('div', 'fullCrewStatsEmpty', 'No data'));
            return;
        }

        if (mode === 'list') {
            var list = createElement('ol', 'fullCrewStatsRankList');
            items.forEach(function (bucket) {
                appendClusteredRankItem(list, bucket, 0, categoryKey);
            });
            container.appendChild(list);
            return;
        }

        if (mode === 'bar') {
            var max = Math.max.apply(
                null,
                items.map(function (b) {
                    return b.count;
                }).concat([1])
            );
            var bars = createElement('div', 'fullCrewStatsBars');
            items.forEach(function (bucket, index) {
                appendClusteredBarRow(bars, bucket, max, 0, index, categoryKey);
            });
            container.appendChild(bars);
            return;
        }

        // pie
        var wrap = createElement('div', 'fullCrewStatsPieWrap');
        var canvas = document.createElement('canvas');
        canvas.className = 'fullCrewStatsPieCanvas';
        canvas.setAttribute('aria-hidden', 'true');
        wrap.appendChild(canvas);

        var legend = createElement('ul', 'fullCrewStatsLegend');
        items.forEach(function (bucket, index) {
            var li = createElement('li', 'fullCrewStatsLegendItem' + (bucketHasChildren(bucket) ? ' fullCrewStatsLegendItem--cluster' : ''));
            var swatch = createElement('span', 'fullCrewStatsSwatch');
            swatch.style.background = CHART_COLORS[index % CHART_COLORS.length];
            var nameRow = createElement('div', 'fullCrewStatsLegendNameRow');
            if (bucketHasChildren(bucket)) {
                var toggle = createClusterToggle(false);
                nameRow.appendChild(toggle);
                var nested = createElement('ul', 'fullCrewStatsLegendNested is-collapsed');
                bucket.children.forEach(function (child) {
                    var childLi = createElement('li', 'fullCrewStatsLegendItem fullCrewStatsLegendItem--child');
                    childLi.appendChild(createBucketNameEl('span', 'fullCrewStatsLegendName', child, categoryKey));
                    childLi.appendChild(
                        createElement(
                            'span',
                            'fullCrewStatsLegendMeta',
                            child.count + ' — ' + formatPercent(child.percent)
                        )
                    );
                    nested.appendChild(childLi);
                });
                toggle.addEventListener('click', function (ev) {
                    ev.preventDefault();
                    ev.stopPropagation();
                    var open = nested.classList.contains('is-collapsed');
                    nested.classList.toggle('is-collapsed', !open);
                    toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
                    var chevron = toggle.querySelector('.fullCrewStatsClusterChevron');
                    if (chevron) {
                        chevron.textContent = open ? '▾' : '▸';
                    }
                });
                li.appendChild(swatch);
                nameRow.appendChild(createBucketNameEl('span', 'fullCrewStatsLegendName', bucket, categoryKey));
                li.appendChild(nameRow);
                li.appendChild(
                    createElement(
                        'span',
                        'fullCrewStatsLegendMeta',
                        bucket.count + ' — ' + formatPercent(bucket.percent)
                    )
                );
                li.appendChild(nested);
            } else {
                li.appendChild(swatch);
                li.appendChild(createBucketNameEl('span', 'fullCrewStatsLegendName', bucket, categoryKey));
                li.appendChild(
                    createElement(
                        'span',
                        'fullCrewStatsLegendMeta',
                        bucket.count + ' — ' + formatPercent(bucket.percent)
                    )
                );
            }
            legend.appendChild(li);
        });
        wrap.appendChild(legend);
        container.appendChild(wrap);
        drawPieChart(canvas, items);
    }

    function renderStatsContent(page, data) {
        var body = page.querySelector('.fullCrewStatsBody');
        if (!body) {
            return;
        }
        body.innerHTML = '';

        var movieCount = Number(prop(data, 'MovieCount', 'movieCount')) || 0;
        var seriesCount = Number(prop(data, 'SeriesCount', 'seriesCount')) || 0;
        var totalCount = Number(prop(data, 'TotalCount', 'totalCount')) || movieCount + seriesCount;
        var animationPercent = Number(prop(data, 'AnimationPercent', 'animationPercent')) || 0;
        var insights = prop(data, 'Insights', 'insights') || [];
        var movieAvgRuntime = formatRuntimeTicks(prop(data, 'MovieRuntimeTicksAverage', 'movieRuntimeTicksAverage'));
        var movieRuntimeSamples = Number(prop(data, 'MovieRuntimeSampleCount', 'movieRuntimeSampleCount')) || 0;
        var seriesAvgRuntime = formatRuntimeTicks(prop(data, 'SeriesRuntimeTicksAverage', 'seriesRuntimeTicksAverage'));
        var seriesRuntimeSamples = Number(prop(data, 'SeriesRuntimeSampleCount', 'seriesRuntimeSampleCount')) || 0;
        var mediaInfoSampleCount = Number(prop(data, 'MediaInfoSampleCount', 'mediaInfoSampleCount')) || 0;
        var mode = getStatsViewMode(false);

        var summary = createElement('div', 'fullCrewStatsSummary');
        var chips = [
            { label: 'Titles', value: String(totalCount) },
            { label: 'Movies', value: String(movieCount) },
            { label: 'Series', value: String(seriesCount) },
            { label: 'Animation', value: formatPercent(animationPercent) }
        ];
        if (mediaInfoSampleCount > 0) {
            chips.push({ label: 'With media info', value: String(mediaInfoSampleCount) });
        }
        if (movieAvgRuntime && movieRuntimeSamples > 0) {
            chips.push({ label: 'Avg movie', value: movieAvgRuntime });
        }
        if (seriesAvgRuntime && seriesRuntimeSamples > 0) {
            chips.push({ label: 'Avg series', value: seriesAvgRuntime });
        }
        chips.forEach(function (chip) {
            var el = createElement('div', 'fullCrewStatsChip');
            el.appendChild(createElement('div', 'fullCrewStatsChipValue', chip.value));
            el.appendChild(createElement('div', 'fullCrewStatsChipLabel', chip.label));
            summary.appendChild(el);
        });
        body.appendChild(summary);

        if (insights.length) {
            var insightSection = createElement('section', 'fullCrewStatsSection');
            insightSection.appendChild(createElement('h3', 'fullCrewStatsSectionTitle', 'Insights'));
            var ul = createElement('ul', 'fullCrewStatsInsights');
            insights.forEach(function (line) {
                ul.appendChild(createElement('li', null, String(line)));
            });
            insightSection.appendChild(ul);
            body.appendChild(insightSection);
        }

        var sections = collectStatsSections(data);
        var grid = createElement('div', 'fullCrewStatsGrid');
        sections.forEach(function (section) {
            var buckets = section.buckets || normalizeBuckets(prop(data, section.keyPascal, section.keyCamel));
            if (!buckets.length) {
                return;
            }
            var card = createElement('section', 'fullCrewStatsSection fullCrewStatsChartSection');
            if (section.categoryKey) {
                var titleLink = createElement('a', 'fullCrewStatsSectionTitle fullCrewStatsSectionTitleLink', section.title);
                titleLink.href = statsDetailHash(section.categoryKey);
                titleLink.setAttribute('aria-label', 'View all ' + section.title);
                titleLink.title = 'View full list';
                card.appendChild(titleLink);
            } else {
                card.appendChild(createElement('h3', 'fullCrewStatsSectionTitle', section.title));
            }
            if (section.hint) {
                card.appendChild(createElement('p', 'fullCrewStatsSectionHint', section.hint));
            }
            var chartHost = createElement('div', 'fullCrewStatsChartHost');
            chartHost.setAttribute('data-section', section.keyPascal);
            if (section.categoryKey) {
                chartHost.setAttribute('data-category', section.categoryKey);
            }
            renderChartInto(chartHost, buckets, mode, true, section.categoryKey || null);
            card.appendChild(chartHost);
            card._buckets = buckets;
            grid.appendChild(card);
        });
        body.appendChild(grid);

        var generatedAt = prop(data, 'GeneratedAt', 'generatedAt');
        if (generatedAt) {
            var footerParts = ['Updated ' + String(generatedAt)];
            footerParts.push('People credits count each movie/series once (not per episode)');
            if (mediaInfoSampleCount > 0) {
                footerParts.push(
                    'Resolution / HDR / codec / audio % of ' +
                    mediaInfoSampleCount +
                    ' titles with media info (series: one early episode sample)'
                );
            }
            var footer = createElement('div', 'fullCrewStatsFooter', footerParts.join(' · '));
            body.appendChild(footer);
        }
    }

    function reRenderStatsCharts(page, forDetail) {
        if (!page) {
            return;
        }
        var mode = getStatsViewMode(!!forDetail);
        if (forDetail) {
            var detailHost = page.querySelector('.fullCrewStatsChartHost');
            if (!detailHost) {
                return;
            }
            var filterInput = page.querySelector('.fullCrewStatsSearch');
            var query = filterInput ? String(filterInput.value || '').trim().toLowerCase() : '';
            var buckets = statsDetailBuckets || [];
            if (query) {
                buckets = buckets.map(function (b) {
                    var nameHit = String(b.name || '').toLowerCase().indexOf(query) !== -1;
                    var childHits = (b.children || []).filter(function (c) {
                        return String(c.name || '').toLowerCase().indexOf(query) !== -1;
                    });
                    if (nameHit) {
                        return b;
                    }
                    if (childHits.length) {
                        return {
                            name: b.name,
                            count: b.count,
                            percent: b.percent,
                            itemId: b.itemId,
                            itemType: b.itemType,
                            children: childHits
                        };
                    }
                    return null;
                }).filter(Boolean);
            }
            renderChartInto(detailHost, buckets, mode, false, statsDetailCategoryKey);
            return;
        }

        if (!statsCachedData) {
            return;
        }
        var hosts = page.querySelectorAll('.fullCrewStatsChartHost');
        Array.prototype.forEach.call(hosts, function (host) {
            var section = host.closest('.fullCrewStatsChartSection');
            var buckets = section && section._buckets;
            var categoryKey = host.getAttribute('data-category') || null;
            if (!buckets) {
                var key = host.getAttribute('data-section');
                var map = {
                    Types: 'types',
                    Resolutions: 'resolutions',
                    VideoRanges: 'videoRanges',
                    VideoCodecs: 'videoCodecs',
                    AudioChannels: 'audioChannels',
                    AudioCodecs: 'audioCodecs',
                    Genres: 'genres',
                    Studios: 'studios',
                    Collections: 'collections',
                    Decades: 'decades',
                    OfficialRatings: 'officialRatings',
                    CommunityRatings: 'communityRatings',
                    Tags: 'tags',
                    Languages: 'languages'
                };
                if (key && key.indexOf('People:') === 0) {
                    var kind = key.slice('People:'.length);
                    var groups = prop(statsCachedData, 'PeopleByRole', 'peopleByRole') || [];
                    for (var i = 0; i < groups.length; i++) {
                        var g = groups[i];
                        if ((prop(g, 'Kind', 'kind') || prop(g, 'Role', 'role')) === kind) {
                            buckets = normalizeBuckets(prop(g, 'People', 'people'));
                            break;
                        }
                    }
                    if (!categoryKey) {
                        categoryKey = peopleCategoryKey(kind);
                    }
                } else {
                    buckets = normalizeBuckets(prop(statsCachedData, key, map[key]));
                }
            }
            renderChartInto(host, buckets || [], mode, true, categoryKey);
        });
    }

    function createStatsViewToggle(page, forDetail) {
        var toggle = createElement('div', 'fullCrewStatsToggle');
        toggle.setAttribute('role', 'group');
        toggle.setAttribute('aria-label', 'Chart view');

        ['pie', 'bar', 'list'].forEach(function (mode) {
            var btn = createElement('button', 'fullCrewStatsToggleBtn', mode.charAt(0).toUpperCase() + mode.slice(1));
            btn.type = 'button';
            btn.setAttribute('data-mode', mode);
            if (mode === getStatsViewMode(forDetail)) {
                btn.classList.add('is-active');
            }
            btn.addEventListener('click', function () {
                setStatsViewMode(mode, forDetail);
                Array.prototype.forEach.call(toggle.querySelectorAll('.fullCrewStatsToggleBtn'), function (el) {
                    el.classList.toggle('is-active', el.getAttribute('data-mode') === mode);
                });
                reRenderStatsCharts(page, forDetail);
            });
            toggle.appendChild(btn);
        });
        return toggle;
    }

    function createStatsPageShell() {
        var page = createElement('div', 'fullCrewPage fullCrewStatsPage');
        page.id = STATS_PAGE_ID;
        page.setAttribute('role', 'main');
        page.setAttribute('data-route', 'overview');

        var header = createElement('div', 'fullCrewStatsHeader');
        header.appendChild(createElement('h1', 'fullCrewStatsTitle', 'Library Stats'));
        header.appendChild(createStatsViewToggle(page, false));
        page.appendChild(header);

        var body = createElement('div', 'fullCrewStatsBody');
        body.appendChild(createElement('div', 'fullCrewStatsStatus', 'Loading library stats…'));
        page.appendChild(body);
        return page;
    }

    function createStatsDetailShell(category) {
        var page = createElement('div', 'fullCrewPage fullCrewStatsPage fullCrewStatsPage--detail');
        page.id = STATS_PAGE_ID;
        page.setAttribute('role', 'main');
        page.setAttribute('data-route', 'detail:' + category);
        page.setAttribute('data-category', category);

        var header = createElement('div', 'fullCrewStatsHeader');
        var titleWrap = createElement('div', 'fullCrewStatsTitleWrap');
        var back = createElement('a', 'fullCrewStatsBack', '← Stats');
        back.href = STATS_HASH;
        titleWrap.appendChild(back);
        titleWrap.appendChild(createElement('h1', 'fullCrewStatsTitle', 'Loading…'));
        header.appendChild(titleWrap);
        header.appendChild(createStatsViewToggle(page, true));
        page.appendChild(header);

        var body = createElement('div', 'fullCrewStatsBody');
        body.appendChild(createElement('div', 'fullCrewStatsStatus', 'Loading full ranking…'));
        page.appendChild(body);
        return page;
    }


    function createStatsBucketShell(category, bucket) {
        var page = createElement('div', 'fullCrewPage fullCrewStatsPage fullCrewStatsPage--bucket');
        page.id = STATS_PAGE_ID;
        page.setAttribute('role', 'main');
        page.setAttribute('data-route', 'bucket:' + category + ':' + bucket);
        page.setAttribute('data-category', category);
        page.setAttribute('data-bucket', bucket);

        var header = createElement('div', 'fullCrewStatsHeader');
        var titleWrap = createElement('div', 'fullCrewStatsTitleWrap');
        var back = createElement('a', 'fullCrewStatsBack', '← ' + category);
        back.href = statsDetailHash(category);
        titleWrap.appendChild(back);
        titleWrap.appendChild(createElement('h1', 'fullCrewStatsTitle', bucket || 'Loading…'));
        header.appendChild(titleWrap);
        page.appendChild(header);

        var body = createElement('div', 'fullCrewStatsBody');
        body.appendChild(createElement('div', 'fullCrewStatsStatus', 'Loading titles in this bucket…'));
        page.appendChild(body);
        return page;
    }

    function renderStatsDetailContent(page, data) {
        var body = page.querySelector('.fullCrewStatsBody');
        if (!body) {
            return;
        }
        body.innerHTML = '';

        var title = prop(data, 'Title', 'title') || prop(data, 'Category', 'category') || 'Category';
        var hint = prop(data, 'DenominatorHint', 'denominatorHint');
        var truncated = !!(prop(data, 'Truncated', 'truncated'));
        var totalBuckets = Number(prop(data, 'TotalBuckets', 'totalBuckets')) || 0;
        var buckets = normalizeBuckets(prop(data, 'Buckets', 'buckets'));
        statsDetailBuckets = buckets;

        var titleEl = page.querySelector('.fullCrewStatsTitle');
        if (titleEl) {
            titleEl.textContent = title;
        }

        if (hint) {
            body.appendChild(createElement('p', 'fullCrewStatsSectionHint', hint));
        }

        var toolbar = createElement('div', 'fullCrewStatsDetailToolbar');
        var search = createElement('input', 'fullCrewStatsSearch');
        search.type = 'search';
        search.placeholder = 'Filter…';
        search.setAttribute('aria-label', 'Filter ranking');
        search.addEventListener('input', function () {
            reRenderStatsCharts(page, true);
        });
        toolbar.appendChild(search);

        var countLabel = createElement(
            'div',
            'fullCrewStatsDetailCount',
            buckets.length + (truncated ? ' of ' + totalBuckets : '') + ' entries'
        );
        toolbar.appendChild(countLabel);
        body.appendChild(toolbar);

        if (truncated) {
            body.appendChild(
                createElement(
                    'p',
                    'fullCrewStatsSectionHint',
                    'Showing top 2000 of ' + totalBuckets + ' for safety.'
                )
            );
        }

        var chartHost = createElement('div', 'fullCrewStatsChartHost fullCrewStatsChartHost--detail');
        body.appendChild(chartHost);
        renderChartInto(chartHost, buckets, getStatsViewMode(true));

        var generatedAt = prop(data, 'GeneratedAt', 'generatedAt');
        if (generatedAt) {
            body.appendChild(createElement('div', 'fullCrewStatsFooter', 'Updated ' + String(generatedAt)));
        }
    }


    function renderStatsBucketContent(page, data, route) {
        var body = page.querySelector('.fullCrewStatsBody');
        if (!body) { return; }
        body.innerHTML = '';

        var bucketName = prop(data, 'Bucket', 'bucket') || (route && route.bucket) || 'Bucket';
        var categoryTitle = prop(data, 'CategoryTitle', 'categoryTitle') || prop(data, 'Category', 'category') || (route && route.category) || 'Stats';
        var categoryKey = prop(data, 'Category', 'category') || (route && route.category) || '';
        var items = prop(data, 'Items', 'items') || [];
        var totalCount = Number(prop(data, 'TotalCount', 'totalCount'));
        if (!isFinite(totalCount)) { totalCount = items.length; }
        var truncated = !!(prop(data, 'Truncated', 'truncated'));

        var titleEl = page.querySelector('.fullCrewStatsTitle');
        if (titleEl) { titleEl.textContent = bucketName; }
        var back = page.querySelector('.fullCrewStatsBack');
        if (back && categoryKey) {
            back.textContent = '← ' + categoryTitle;
            back.href = statsDetailHash(categoryKey);
        }

        body.appendChild(createElement(
            'p',
            'fullCrewStatsSectionHint',
            'Items in this bucket · ' + categoryTitle + ' · ' + totalCount +
                (truncated ? ' (showing first ' + items.length + ')' : '') +
                ' title' + (totalCount === 1 ? '' : 's')
        ));

        if (!items.length) {
            body.appendChild(createElement('div', 'fullCrewStatsEmpty', 'No titles in this bucket.'));
            return;
        }

        var movies = [];
        var shows = [];
        items.forEach(function (t) {
            var type = String(prop(t, 'Type', 'type') || '');
            var card = {
                id: prop(t, 'Id', 'id'),
                name: prop(t, 'Name', 'name'),
                type: type,
                year: prop(t, 'ProductionYear', 'productionYear'),
                imageTag: prop(t, 'ImageTag', 'imageTag')
            };
            if (/series/i.test(type)) { shows.push(Core.Ui.posterCard(card)); }
            else { movies.push(Core.Ui.posterCard(card)); }
        });

        if (movies.length && shows.length) {
            body.appendChild(Core.Ui.posterRow('Movies', movies));
            body.appendChild(Core.Ui.posterRow('Shows', shows));
        } else {
            body.appendChild(Core.Ui.posterRow('In your library', movies.length ? movies : shows));
        }

        var generatedAt = prop(data, 'GeneratedAt', 'generatedAt');
        if (generatedAt) {
            body.appendChild(createElement('div', 'fullCrewStatsFooter', 'Updated ' + String(generatedAt)));
        }
    }

    /* ================================================================== */
    /* Feature: Studio detail page                                        */
    /* ================================================================== */

    var studioPageCtrl = new Core.CustomPage({
        id: STUDIO_PAGE_ID,
        bodyClass: STUDIO_BODY_CLASS,
        ownsTitle: true
    });

    function parseStudioRoute() {
        var raw = window.location.hash || '';
        var qIndex = raw.indexOf('?');
        var path = normalizeStatsHash(qIndex >= 0 ? raw.slice(0, qIndex) : raw);
        var query = qIndex >= 0 ? raw.slice(qIndex + 1) : '';
        var match = /^#\/fullcrew\/studio\/(.+)$/.exec(path);
        if (!match) {
            return null;
        }

        var name;
        try {
            name = decodeURIComponent(match[1]);
        } catch (e) {
            name = match[1];
        }

        var params = Core.parseQuery(query);
        var branches = [];
        if (params.branches) {
            branches = params.branches.split(/[|,]/).map(function (s) {
                return s.trim();
            }).filter(Boolean);
        }

        return {
            name: name,
            id: params.id || null,
            branches: branches
        };
    }

    function isStudioRoute() {
        return parseStudioRoute() != null;
    }

    function tearDownStudioPage() {
        studioPageCtrl.tearDown();
    }

    function fetchStudioPage(route) {
        // Prefer query-string name so ApiClient.getUrl cannot double-encode path spaces
        // (e.g. "Studio Ghibli" → %2520) and break matching.
        var query = ['name=' + encodeURIComponent(route.name)];
        if (route.id) {
            query.push('id=' + encodeURIComponent(route.id));
        }
        if (route.branches && route.branches.length) {
            query.push('branches=' + route.branches.map(encodeURIComponent).join(','));
        }
        return Core.getJson('FullCrew/studio?' + query.join('&'));
    }

    function primaryImageUrl(itemId) {
        return Core.primaryImageUrl(itemId);
    }

    function renderStudioPageContent(page, data) {
        var body = page.querySelector('.fullCrewStudioBody');
        if (!body) {
            return;
        }
        body.innerHTML = '';

        var name = prop(data, 'Name', 'name') || 'Studio';
        setCustomDocumentTitle(true, name);

        var profile = createElement('div', 'fullCrewStudioProfile');
        var logoUrl = prop(data, 'LogoUrl', 'logoUrl');
        if (logoUrl) {
            var logo = createElement('img', 'fullCrewStudioLogo');
            logo.src = logoUrl;
            logo.alt = name;
            logo.onerror = function () {
                var fallback = createElement('div', 'fullCrewStudioLogoFallback', name.charAt(0) || '?');
                if (logo.parentNode) {
                    logo.parentNode.replaceChild(fallback, logo);
                }
            };
            profile.appendChild(logo);
        } else {
            profile.appendChild(createElement('div', 'fullCrewStudioLogoFallback', name.charAt(0) || '?'));
        }
        profile.appendChild(createElement('h1', 'fullCrewStudioName', name));
        body.appendChild(profile);

        var overview = prop(data, 'Overview', 'overview');
        if (overview) {
            body.appendChild(createElement('p', 'fullCrewStudioOverview', overview));
        }

        var stats = prop(data, 'Stats', 'stats');
        var firstY = stats ? prop(stats, 'FirstReleaseYear', 'firstReleaseYear') : null;
        var meta = Core.Ui.metaRows([
            { label: 'First release', value: firstY ? String(firstY) : null },
            { label: 'Headquarters', value: prop(data, 'Headquarters', 'headquarters') },
            { label: 'Country', value: prop(data, 'OriginCountry', 'originCountry') },
            { label: 'Parent', value: prop(data, 'ParentCompany', 'parentCompany') }
        ]);
        if (meta) {
            body.appendChild(meta);
        }

        var links = createElement('div', 'fullCrewStudioLinks');
        var tmdbUrl = prop(data, 'TmdbUrl', 'tmdbUrl');
        var homepage = prop(data, 'Homepage', 'homepage');
        if (tmdbUrl) {
            links.appendChild(Core.Ui.badgeLink('TMDb', tmdbUrl));
        }
        if (homepage) {
            links.appendChild(Core.Ui.badgeLink('Homepage', homepage));
        }
        if (links.childNodes.length) {
            body.appendChild(links);
        }

        var coCredit = prop(data, 'CoCreditHint', 'coCreditHint');
        if (coCredit) {
            var coName = prop(coCredit, 'Name', 'name');
            if (coName) {
                var note = createElement('p', 'fullCrewStudioCoCredit');
                note.appendChild(document.createTextNode('Also often credited with '));
                var coLink = createElement('a', 'fullCrewStudioCoCreditLink', coName);
                coLink.href = studioPageHash({ name: coName, itemType: 'Studio' });
                coLink.addEventListener('click', function (ev) {
                    if (ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) { return; }
                    ev.preventDefault();
                    window.location.hash = studioPageHash({ name: coName, itemType: 'Studio' });
                });
                note.appendChild(coLink);
                body.appendChild(note);
            }
        }

        if (stats && (prop(stats, 'TotalCount', 'totalCount') || 0) > 0) {
            body.appendChild(renderStudioQuietStats(stats));
        }

        var branches = prop(data, 'Branches', 'branches') || [];
        if (branches.length > 1) {
            var variants = createElement('p', 'fullCrewStudioVariants');
            variants.appendChild(document.createTextNode('Also credited as '));
            branches.forEach(function (b, i) {
                if (i > 0) {
                    variants.appendChild(document.createTextNode(i === branches.length - 1 ? ' and ' : ', '));
                }
                var a = createElement('a', 'fullCrewStudioVariantLink', String(b));
                a.href = studioPageHash({ name: b, itemType: 'Studio' });
                a.addEventListener('click', function (ev) {
                    if (ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) { return; }
                    ev.preventDefault();
                    window.location.hash = studioPageHash({ name: b, itemType: 'Studio' });
                });
                variants.appendChild(a);
            });
            body.appendChild(variants);
        }

        var titles = prop(data, 'Titles', 'titles') || [];
        var movies = titles.filter(function (t) {
            return String(prop(t, 'Type', 'type') || '').toLowerCase() === 'movie';
        });
        var shows = titles.filter(function (t) {
            return String(prop(t, 'Type', 'type') || '').toLowerCase() === 'series';
        });

        if (!titles.length) {
            body.appendChild(
                createElement('div', 'fullCrewStatsEmpty', 'No movies or series credited to this studio in your library.')
            );
        } else {
            if (movies.length) {
                body.appendChild(renderStudioTitleRow('Movies', movies));
            }
            if (shows.length) {
                body.appendChild(renderStudioTitleRow('Shows', shows));
            }
            if (!movies.length && !shows.length) {
                body.appendChild(renderStudioTitleRow('In your library', titles));
            }
        }

        var missing = prop(data, 'MissingPopular', 'missingPopular') || [];
        if (missing.length) {
            var missSection = createElement('section', 'fullCrewStudioSection fullCrewStudioSection--secondary');
            missSection.appendChild(createElement('h3', 'fullCrewStudioSectionTitle', 'Missing popular titles'));
            missSection.appendChild(createElement(
                'p',
                'fullCrewStudioHint',
                'Popular on TMDB for this company, not matched in your library.'
            ));
            var missList = createElement('ul', 'fullCrewStudioMissing');
            missing.forEach(function (m) {
                var li = createElement('li');
                var label = prop(m, 'Name', 'name') || 'Untitled';
                var year = prop(m, 'Year', 'year');
                var media = prop(m, 'MediaType', 'mediaType');
                var text = label;
                if (year) { text += ' (' + year + ')'; }
                if (media) { text += ' · ' + media; }
                var href = prop(m, 'TmdbUrl', 'tmdbUrl');
                if (href) {
                    li.appendChild(Core.Ui.externalLink(text, href));
                } else {
                    li.textContent = text;
                }
                missList.appendChild(li);
            });
            missSection.appendChild(missList);
            body.appendChild(missSection);
        }
    }

    function renderStudioQuietStats(stats) {
        var wrap = createElement('div', 'fullCrewStudioQuietStats');
        var bits = [];
        var movies = prop(stats, 'MovieCount', 'movieCount') || 0;
        var series = prop(stats, 'SeriesCount', 'seriesCount') || 0;
        var total = prop(stats, 'TotalCount', 'totalCount') || 0;
        if (total) {
            bits.push(total + ' in library');
        }
        if (movies) {
            bits.push(movies + ' movie' + (movies === 1 ? '' : 's'));
        }
        if (series) {
            bits.push(series + ' show' + (series === 1 ? '' : 's'));
        }
        var firstY = prop(stats, 'FirstReleaseYear', 'firstReleaseYear');
        var newestY = prop(stats, 'NewestReleaseYear', 'newestReleaseYear');
        if (firstY && newestY && firstY !== newestY) {
            bits.push(firstY + '–' + newestY);
        }
        var avg = prop(stats, 'AverageCommunityRating', 'averageCommunityRating');
        if (avg) {
            bits.push('avg ★ ' + Number(avg).toFixed(1));
        }
        if (bits.length) {
            wrap.appendChild(createElement('div', 'fullCrewStudioQuietLine', bits.join(' · ')));
        }

        var details = createElement('div', 'fullCrewStudioQuietDetails');
        appendStudioQuietLinked(details, 'Highest', prop(stats, 'HighestRatedTitle', 'highestRatedTitle'), prop(stats, 'HighestRatedTitleId', 'highestRatedTitleId'), prop(stats, 'HighestRatedValue', 'highestRatedValue') ? '★ ' + Number(prop(stats, 'HighestRatedValue', 'highestRatedValue')).toFixed(1) : null);
        appendStudioQuietLinked(details, 'Oldest', prop(stats, 'OldestTitle', 'oldestTitle'), prop(stats, 'OldestTitleId', 'oldestTitleId'), firstY ? String(firstY) : null);
        appendStudioQuietLinked(details, 'Newest', prop(stats, 'NewestTitle', 'newestTitle'), prop(stats, 'NewestTitleId', 'newestTitleId'), newestY ? String(newestY) : null);
        if (details.childNodes.length) {
            wrap.appendChild(details);
        }
        return wrap;
    }

    function appendStudioQuietLinked(parent, label, title, id, extra) {
        if (!title) {
            return;
        }
        var row = createElement('div', 'fullCrewStudioQuietDetail');
        row.appendChild(createElement('span', 'fullCrewStudioQuietDetailLabel', label));
        var value = createElement('span', 'fullCrewStudioQuietDetailValue');
        if (id) {
            value.appendChild(Core.Ui.itemLink(title, id));
        } else {
            value.appendChild(document.createTextNode(title));
        }
        if (extra) {
            value.appendChild(document.createTextNode(' · ' + extra));
        }
        row.appendChild(value);
        parent.appendChild(row);
    }

    function renderStudioTitleRow(heading, titles) {
        var cards = titles.map(function (t) {
            return Core.Ui.posterCard({
                id: prop(t, 'Id', 'id'),
                name: prop(t, 'Name', 'name'),
                type: prop(t, 'Type', 'type'),
                year: prop(t, 'ProductionYear', 'productionYear'),
                rating: prop(t, 'CommunityRating', 'communityRating'),
                imageTag: prop(t, 'ImageTag', 'imageTag')
            });
        });
        return Core.Ui.posterRow(heading, cards);
    }

    function mountStudioPage() {
        var route = parseStudioRoute();
        if (!route) {
            tearDownStudioPage();
            return;
        }

        ensureStyles();
        // Keep 404 chrome hidden through Stats teardown → studio shell attach.
        applyRouteChrome(peekFullCrewRoute(), { pending: true, setTitle: false });
        setCustomDocumentTitle(true, route.name);
        ensureHeaderTabsVisible();
        injectStatsTab();

        var existing = document.getElementById(STUDIO_PAGE_ID);
        var routeKey = route.name + '|' + (route.id || '') + '|' + (route.branches || []).join(',');
        if (existing && existing.getAttribute('data-route') === routeKey &&
            (existing.getAttribute('data-loaded') === '1' ||
                existing.getAttribute('data-loading') === '1' ||
                existing.getAttribute('data-error') === '1')) {
            document.documentElement.classList.add(STUDIO_BODY_CLASS);
            document.body.classList.add(STUDIO_BODY_CLASS);
            clearRoutePending();
            setStatsTabSelected(false);
            setCustomDocumentTitle(true, existing.getAttribute('data-studio-name') || route.name);
            return;
        }

        // Drop Stats overlay only after studio chrome is pending (avoids 404 frame).
        tearDownStatsPage();

        var fetchGen = String(Date.now()) + '-' + Math.random().toString(36).slice(2, 8);
        var page = studioPageCtrl.mountShell(routeKey, function () {
            var p = createElement('div', 'fullCrewPage fullCrewStudioPage');
            p.id = STUDIO_PAGE_ID;
            p.setAttribute('role', 'main');
            p.setAttribute('data-loading', '1');
            p.setAttribute('data-fetch-gen', fetchGen);

            var header = createElement('div', 'fullCrewStudioHeader');
            var back = createElement('a', 'fullCrewStudioBack', '← Stats · Studios');
            back.href = statsDetailHash('studios');
            header.appendChild(back);
            p.appendChild(header);

            var body = createElement('div', 'fullCrewStudioBody');
            body.appendChild(Core.Ui.status('Loading studio…'));
            p.appendChild(body);
            return p;
        });
        clearRoutePending();
        applyRouteChrome(peekFullCrewRoute(), { pending: false, setTitle: false });
        setStatsTabSelected(false);
        setCustomDocumentTitle(true, route.name);

        Core.bindPageLoad(page, fetchStudioPage(route), {
            stillValid: function () {
                return page.getAttribute('data-fetch-gen') === fetchGen && isStudioRoute();
            },
            warn: '[FullCrew] studio page failed',
            onSuccess: function (data) {
                var loadedName = prop(data, 'Name', 'name') || route.name;
                page.removeAttribute('data-error');
                page.setAttribute('data-studio-name', loadedName);
                renderStudioPageContent(page, data);
            },
            onError: function () {
                page.setAttribute('data-error', '1');
                var body = page.querySelector('.fullCrewStudioBody');
                if (body) {
                    body.innerHTML = '';
                    body.appendChild(Core.Ui.status('Could not load studio details.', true));
                }
                setCustomDocumentTitle(true, route.name);
            }
        });
    }

    /* ================================================================== */
    /* Boot — single scan loop owns all feature mount/teardown            */
    /* ================================================================== */

    function mutationTouchesFullCrewOnly(mutations) {
        if (!mutations || !mutations.length) {
            return false;
        }
        for (var i = 0; i < mutations.length; i++) {
            var m = mutations[i];
            var nodes = [];
            if (m.target) {
                nodes.push(m.target);
            }
            if (m.addedNodes && m.addedNodes.length) {
                Array.prototype.push.apply(nodes, m.addedNodes);
            }
            if (m.removedNodes && m.removedNodes.length) {
                Array.prototype.push.apply(nodes, m.removedNodes);
            }
            for (var j = 0; j < nodes.length; j++) {
                var n = nodes[j];
                if (!n || n.nodeType !== 1) {
                    if (n && n.nodeType === 3) {
                        // text change inside header title — PageTitle observer handles it
                        continue;
                    }
                    return false;
                }
                if (n.id === STATS_PAGE_ID || n.id === STUDIO_PAGE_ID || n.id === STATS_TAB_ID ||
                    n.id === CRITICAL_STYLE_ID || n.id === STYLE_ID || n.id === SECTION_ID) {
                    continue;
                }
                if (n.classList && (n.classList.contains('fullCrewPage') || n.classList.contains('fullCrewStatsPage') ||
                    n.classList.contains('fullCrewSection'))) {
                    continue;
                }
                if (n.closest && (n.closest('#' + STATS_PAGE_ID) || n.closest('#' + STUDIO_PAGE_ID) ||
                    n.closest('#' + STATS_TAB_ID) || n.closest('#' + SECTION_ID))) {
                    continue;
                }
                return false;
            }
        }
        return true;
    }

    function scanAll() {
        scan();
        syncStatsUi();
        if (peekFullCrewRoute()) {
            Core.PageTitle.tick();
        } else if (Core.PageTitle.isOwned()) {
            Core.PageTitle.release();
        }
    }

    function start() {
        ensureStyles();

        // Immediate route sync — don't wait on config for hash chrome.
        var early = peekFullCrewRoute();
        if (early) {
            applyRouteChrome(early, { pending: true, setTitle: true });
            claimRouteTitle(early);
        }

        refreshStatsEnabled().then(function () {
            scanAll();
        });
        scanAll();

        var observer = new MutationObserver(function (mutations) {
            if (mutationTouchesFullCrewOnly(mutations)) {
                if (Core.PageTitle.isOwned()) {
                    Core.PageTitle.tick();
                }
                return;
            }
            window.clearTimeout(start._timer);
            start._timer = window.setTimeout(scanAll, 180);
        });

        if (document.body) {
            observer.observe(document.body, {
                childList: true,
                subtree: true
            });
        }

        window.addEventListener('hashchange', function () {
            syncCreditsNavState();
            var route = peekFullCrewRoute();
            if (route) {
                applyRouteChrome(route, { pending: true, setTitle: true });
                claimRouteTitle(route);
                scanAll();
            } else {
                applyRouteChrome(null);
                Core.PageTitle.release();
                window.setTimeout(scanAll, 80);
            }
        });

        document.addEventListener('viewshow', function (e) {
            var detail = e && e.detail;
            var view = (detail && detail.element) || (e && e.target);
            if (peekFullCrewRoute()) {
                window.setTimeout(function () {
                    if (view) {
                        mount(view);
                    }
                    syncStatsUi();
                    Core.PageTitle.tick();
                }, 30);
                return;
            }
            // Non–Full Crew (Home / Favourites / …): never tick title ownership.
            Core.PageTitle.release();
            if (view) {
                window.setTimeout(function () {
                    mount(view);
                    syncStatsUi();
                }, 200);
            } else {
                window.setTimeout(scanAll, 200);
            }
        }, true);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
