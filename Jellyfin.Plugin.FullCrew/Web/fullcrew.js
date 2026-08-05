(function () {
    'use strict';

    var SECTION_ID = 'fullCrewSection';
    var STYLE_ID = 'fullCrewStyles';

    function ensureStyles() {
        if (document.getElementById(STYLE_ID)) {
            return;
        }

        var link = document.createElement('link');
        link.id = STYLE_ID;
        link.rel = 'stylesheet';
        link.href = '/FullCrew/fullcrew.css';
        document.head.appendChild(link);
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
        var el = document.createElement(tag);
        if (className) {
            el.className = className;
        }
        if (text != null) {
            el.textContent = text;
        }
        return el;
    }

    function removeExisting(view) {
        var existing = view.querySelector('#' + SECTION_ID);
        if (existing) {
            existing.remove();
        }
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
        return window.ApiClient || (window.ApiClient && window.ApiClient) || null;
    }

    function fetchCredits(itemId) {
        var client = apiClient();
        if (client && typeof client.ajax === 'function') {
            return client.ajax({
                url: client.getUrl('FullCrew/' + itemId),
                type: 'GET',
                dataType: 'json'
            });
        }

        if (client && typeof client.getJSON === 'function') {
            return client.getJSON(client.getUrl('FullCrew/' + itemId));
        }

        var url = '/FullCrew/' + encodeURIComponent(itemId);
        return fetch(url, { credentials: 'same-origin' }).then(function (res) {
            if (!res.ok) {
                throw new Error('HTTP ' + res.status);
            }
            return res.json();
        });
    }

    function mount(view) {
        if (!view) {
            return;
        }

        if (!isDetailView(view) && !view.querySelector('.itemDetailImage, .detailImageContainer, .itemDetailGalleryLink')) {
            return;
        }

        var itemId = getItemIdFromView(view) || getItemIdFromLocation();
        if (!itemId) {
            return;
        }

        ensureStyles();
        mountDetailButtons(view, itemId);

        var existing = view.querySelector('#' + SECTION_ID);
        if (existing && existing.getAttribute('data-item-id') === itemId) {
            return;
        }

        removeExisting(view);

        var section = createSection();
        section.setAttribute('data-item-id', itemId);
        renderStatus(section, 'Loading cast & crew…', false);

        var anchor = findInsertionPoint(view);
        if (anchor && anchor.parentNode) {
            // Sit where the native Cast & Crew block is (we'll hide that once data loads).
            anchor.parentNode.insertBefore(section, anchor);
        } else {
            view.appendChild(section);
        }

        fetchCredits(itemId)
            .then(function (data) {
                if (!document.body.contains(section)) {
                    return;
                }

                var error = data.Error || data.error;
                if (error) {
                    showNativeCast(view);
                    section.remove();
                    return;
                }

                var departments = data.Departments || data.departments || [];
                if (!departments.length) {
                    showNativeCast(view);
                    section.remove();
                    return;
                }

                hideNativeCast(view);
                renderDepartments(section, data);
            })
            .catch(function (err) {
                if (!document.body.contains(section)) {
                    return;
                }
                console.warn('[FullCrew] failed to load credits', err);
                showNativeCast(view);
                section.remove();
            });
    }

    var BUMPER_BTN_ID = 'fullCrewBumperButton';
    var TRAILER_BTN_ID = 'fullCrewTrailerButton';

    function fetchBumper(itemId) {
        var client = apiClient();
        if (client && typeof client.ajax === 'function') {
            return client.ajax({
                url: client.getUrl('FullCrew/' + itemId + '/bumper'),
                type: 'GET',
                dataType: 'json'
            });
        }

        if (client && typeof client.getJSON === 'function') {
            return client.getJSON(client.getUrl('FullCrew/' + itemId + '/bumper'));
        }

        return fetch('/FullCrew/' + encodeURIComponent(itemId) + '/bumper', { credentials: 'same-origin' })
            .then(function (res) {
                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }
                return res.json();
            });
    }

    function fetchTrailer(itemId) {
        var client = apiClient();
        if (client && typeof client.ajax === 'function') {
            return client.ajax({
                url: client.getUrl('FullCrew/' + itemId + '/trailer'),
                type: 'GET',
                dataType: 'json'
            });
        }

        if (client && typeof client.getJSON === 'function') {
            return client.getJSON(client.getUrl('FullCrew/' + itemId + '/trailer'));
        }

        return fetch('/FullCrew/' + encodeURIComponent(itemId) + '/trailer', { credentials: 'same-origin' })
            .then(function (res) {
                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }
                return res.json();
            });
    }

    function extractYouTubeId(url) {
        if (!url) {
            return null;
        }
        var m = String(url).match(/(?:youtu\.be\/|v=|embed\/|shorts\/)([A-Za-z0-9_-]{11})/);
        return m ? m[1] : null;
    }

    function getItemPromise(itemId) {
        var client = apiClient();
        if (!client || typeof client.getItem !== 'function') {
            return Promise.resolve(null);
        }

        try {
            var userId = client.getCurrentUserId && client.getCurrentUserId();
            if (!userId) {
                return Promise.resolve(null);
            }
            return Promise.resolve(client.getItem(userId, itemId));
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

    function closeBumperOverlay() {
        var overlay = document.getElementById('fullCrewBumperOverlay');
        if (overlay && overlay.parentNode) {
            overlay.parentNode.removeChild(overlay);
        }
        document.removeEventListener('keydown', onBumperOverlayKeydown, true);
    }

    function onBumperOverlayKeydown(e) {
        if (e && (e.key === 'Escape' || e.keyCode === 27)) {
            closeBumperOverlay();
        }
    }

    function openYouTubeEmbed(videoId, title) {
        if (!videoId) {
            return false;
        }

        closeBumperOverlay();

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
            if (!isBumperTrailerItemType(type)) {
                return;
            }

            var trailer = mountTrailerButton(row, itemId);
            mountBumperButton(row, itemId, trailer);
        });
    }

    function isBumperTrailerItemType(type) {
        return /^(Movie|Series|Season|Episode)$/i.test(String(type || ''));
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

    /* ------------------------------------------------------------------ */
    /* Library Stats page                                                 */
    /* ------------------------------------------------------------------ */

    var STATS_TAB_ID = 'fullCrewStatsTab';
    var STATS_PAGE_ID = 'fullCrewStatsPage';
    var STATS_HASH = '#/fullcrew/stats';
    var STATS_VIEW_KEY = 'fullCrew.statsViewMode';
    var STATS_DETAIL_VIEW_KEY = 'fullCrew.statsDetailViewMode';
    var PLUGIN_UNIQUE_ID = 'a8f3c2e1-9b4d-4f6a-8e2c-1d5b7a9c0e3f';
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
    var statsDetailBuckets = null;

    function normalizeStatsHash(hash) {
        var raw = (hash || '').split('?')[0];
        if (raw.indexOf('#!/') === 0) {
            return '#' + raw.slice(2);
        }
        return raw;
    }

    function parseStatsRoute() {
        var hash = normalizeStatsHash(window.location.hash || '');
        if (hash === STATS_HASH) {
            return { kind: 'overview' };
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
        if (!obj) {
            return undefined;
        }
        if (obj[pascal] != null) {
            return obj[pascal];
        }
        return obj[camel];
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
        return '#/details?id=' + encodeURIComponent(String(itemId));
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
     * Navigate to a Jellyfin entity page (Person, Genre, Studio, BoxSet, …).
     * Prefer in-app router; fall back to hash navigation.
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
        } catch (e) {
            /* ignore */
        }

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
        } catch (e2) {
            /* ignore */
        }

        window.location.hash = detailsHashForItem(itemId);
        return true;
    }

    function createBucketNameEl(tagName, className, bucket) {
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
        } else {
            el = createElement(tagName, className, label);
            if (label !== bucket.name) {
                el.title = bucket.name;
            }
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

    function appendClusteredRankItem(list, bucket, depth) {
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

        nameWrap.appendChild(createBucketNameEl('span', 'fullCrewStatsRankName', bucket));
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
                appendClusteredRankItem(childList, child, (depth || 0) + 1);
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

    function appendClusteredBarRow(bars, bucket, max, depth, colorIndex) {
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

        labelWrap.appendChild(createBucketNameEl('div', 'fullCrewStatsBarLabel', bucket));
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
                appendClusteredBarRow(childHost, child, max, (depth || 0) + 1, colorIndex);
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

        var client = apiClient();
        var request;

        if (client && typeof client.ajax === 'function') {
            request = client.ajax({
                url: client.getUrl('FullCrew/stats'),
                type: 'GET',
                dataType: 'json'
            });
        } else if (client && typeof client.getJSON === 'function') {
            request = client.getJSON(client.getUrl('FullCrew/stats'));
        } else {
            request = fetch('/FullCrew/stats', { credentials: 'same-origin' }).then(function (res) {
                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }
                return res.json();
            });
        }

        statsFetchInFlight = Promise.resolve(request)
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

        var client = apiClient();
        var path = 'FullCrew/stats/' + encodeURIComponent(key);
        var request;

        if (client && typeof client.ajax === 'function') {
            request = client.ajax({
                url: client.getUrl(path),
                type: 'GET',
                dataType: 'json'
            });
        } else if (client && typeof client.getJSON === 'function') {
            request = client.getJSON(client.getUrl(path));
        } else {
            request = fetch('/' + path, { credentials: 'same-origin' }).then(function (res) {
                if (!res.ok) {
                    throw new Error('HTTP ' + res.status);
                }
                return res.json();
            });
        }

        statsCategoryFetchInFlight[key] = Promise.resolve(request)
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

    function setStatsDocumentTitle(active) {
        try {
            if (active) {
                if (!document.documentElement.getAttribute('data-fullcrew-prev-title')) {
                    document.documentElement.setAttribute('data-fullcrew-prev-title', document.title || '');
                }
                document.title = 'Stats';
            } else {
                var prev = document.documentElement.getAttribute('data-fullcrew-prev-title');
                if (prev != null) {
                    document.title = prev;
                    document.documentElement.removeAttribute('data-fullcrew-prev-title');
                }
            }
        } catch (e) {
            /* ignore */
        }

        var headerCandidates = document.querySelectorAll(
            '.skinHeader .headerHomeButton, .skinHeader .pageTitle, .skinHeader .headerButton.headerTitle, .headerTop .pageTitle, .headerTitle'
        );
        Array.prototype.forEach.call(headerCandidates, function (el) {
            if (!el || el.id === STATS_TAB_ID) {
                return;
            }
            var text = (el.textContent || '').replace(/\s+/g, ' ').trim();
            if (active) {
                if (/page not found/i.test(text) || text === '' || /home/i.test(text)) {
                    if (!el.getAttribute('data-fullcrew-prev-header')) {
                        el.setAttribute('data-fullcrew-prev-header', text);
                    }
                    el.textContent = 'Stats';
                }
            } else if (el.getAttribute('data-fullcrew-prev-header') != null) {
                el.textContent = el.getAttribute('data-fullcrew-prev-header');
                el.removeAttribute('data-fullcrew-prev-header');
            }
        });
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
            setStatsDocumentTitle(false);
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
        return (
            document.querySelector('.mainAnimatedPages') ||
            document.querySelector('#mainContent') ||
            document.querySelector('.mainDrawer-scrollContainer') ||
            document.body
        );
    }

    function tearDownStatsPage() {
        document.body.classList.remove('fullCrewStatsActive');
        var page = document.getElementById(STATS_PAGE_ID);
        if (page && page.parentNode) {
            page.parentNode.removeChild(page);
        }
        statsDetailBuckets = null;
        setStatsTabSelected(false);
        setStatsDocumentTitle(false);
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

    function renderChartInto(container, buckets, mode, omitDominantOther) {
        container.innerHTML = '';
        var items = chartDisplayBuckets(buckets, !!omitDominantOther);
        if (!items.length) {
            container.appendChild(createElement('div', 'fullCrewStatsEmpty', 'No data'));
            return;
        }

        if (mode === 'list') {
            var list = createElement('ol', 'fullCrewStatsRankList');
            items.forEach(function (bucket) {
                appendClusteredRankItem(list, bucket, 0);
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
                appendClusteredBarRow(bars, bucket, max, 0, index);
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
                    childLi.appendChild(createBucketNameEl('span', 'fullCrewStatsLegendName', child));
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
                nameRow.appendChild(createBucketNameEl('span', 'fullCrewStatsLegendName', bucket));
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
                li.appendChild(createBucketNameEl('span', 'fullCrewStatsLegendName', bucket));
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
            renderChartInto(chartHost, buckets, mode, true);
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
            renderChartInto(detailHost, buckets, mode);
            return;
        }

        if (!statsCachedData) {
            return;
        }
        var hosts = page.querySelectorAll('.fullCrewStatsChartHost');
        Array.prototype.forEach.call(hosts, function (host) {
            var section = host.closest('.fullCrewStatsChartSection');
            var buckets = section && section._buckets;
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
                } else {
                    buckets = normalizeBuckets(prop(statsCachedData, key, map[key]));
                }
            }
            renderChartInto(host, buckets || [], mode, true);
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
        var page = createElement('div', 'fullCrewStatsPage');
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
        var page = createElement('div', 'fullCrewStatsPage fullCrewStatsPage--detail');
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
        ensureHeaderTabsVisible();
        injectStatsTab();
        setStatsTabSelected(true);
        setStatsDocumentTitle(true);

        document.body.classList.add('fullCrewStatsActive');

        var routeKey = route.kind === 'detail' ? 'detail:' + route.category : 'overview';
        var mount = findStatsMountPoint();
        var page = document.getElementById(STATS_PAGE_ID);
        if (page && page.getAttribute('data-route') === routeKey && page.getAttribute('data-loaded') === '1') {
            return;
        }

        if (page && page.parentNode) {
            page.parentNode.removeChild(page);
            page = null;
        }
        statsDetailBuckets = null;

        if (route.kind === 'detail') {
            page = createStatsDetailShell(route.category);
            mount.appendChild(page);
            fetchStatsCategory(route.category)
                .then(function (data) {
                    if (!document.body.contains(page) || !isStatsRoute()) {
                        return;
                    }
                    renderStatsDetailContent(page, data);
                    page.setAttribute('data-loaded', '1');
                })
                .catch(function (err) {
                    if (!document.body.contains(page)) {
                        return;
                    }
                    console.warn('[FullCrew] failed to load stats category', route.category, err);
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
                });
            return;
        }

        page = createStatsPageShell();
        mount.appendChild(page);

        fetchStats()
            .then(function (data) {
                if (!document.body.contains(page) || !isStatsRoute()) {
                    return;
                }
                renderStatsContent(page, data);
                page.setAttribute('data-loaded', '1');
            })
            .catch(function (err) {
                if (!document.body.contains(page)) {
                    return;
                }
                console.warn('[FullCrew] failed to load library stats', err);
                var body = page.querySelector('.fullCrewStatsBody');
                if (body) {
                    body.innerHTML = '';
                    body.appendChild(
                        createElement(
                            'div',
                            'fullCrewStatsStatus fullCrewStatsStatus--error',
                            'Could not load library stats. Is the plugin API available?'
                        )
                    );
                }
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

    function syncStatsUi() {
        if (!statsConfigLoaded) {
            refreshStatsEnabled().then(syncStatsUi);
            return;
        }

        if (!statsEnabled) {
            tearDownStatsPage();
            tearDownStudioPage();
            removeStatsTab();
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

    /* ------------------------------------------------------------------ */
    /* Studio detail page                                                 */
    /* ------------------------------------------------------------------ */

    var STUDIO_PAGE_ID = 'fullCrewStudioPage';

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

        var params = {};
        if (query) {
            query.split('&').forEach(function (pair) {
                var i = pair.indexOf('=');
                if (i < 0) {
                    return;
                }
                var k = decodeURIComponent(pair.slice(0, i));
                var v = decodeURIComponent(pair.slice(i + 1));
                params[k] = v;
            });
        }

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
        document.body.classList.remove('fullCrewStudioActive');
        var page = document.getElementById(STUDIO_PAGE_ID);
        if (page && page.parentNode) {
            page.parentNode.removeChild(page);
        }
    }

    function fetchStudioPage(route) {
        var client = apiClient();
        var path = 'FullCrew/studio/' + encodeURIComponent(route.name);
        var query = [];
        if (route.id) {
            query.push('id=' + encodeURIComponent(route.id));
        }
        if (route.branches && route.branches.length) {
            query.push('branches=' + route.branches.map(encodeURIComponent).join(','));
        }
        if (query.length) {
            path += '?' + query.join('&');
        }

        if (client && typeof client.getJSON === 'function') {
            return client.getJSON(client.getUrl(path));
        }
        return fetch('/' + path, { credentials: 'same-origin' }).then(function (r) {
            if (!r.ok) {
                throw new Error('studio ' + r.status);
            }
            return r.json();
        });
    }

    function primaryImageUrl(itemId) {
        if (!itemId) {
            return null;
        }
        var client = apiClient();
        try {
            if (client && typeof client.getImageUrl === 'function') {
                return client.getImageUrl(itemId, { type: 'Primary', maxHeight: 360 });
            }
        } catch (e) {
            /* ignore */
        }
        var base = client && typeof client.getUrl === 'function'
            ? client.getUrl('Items/' + itemId + '/Images/Primary')
            : '/Items/' + encodeURIComponent(itemId) + '/Images/Primary';
        return base + (base.indexOf('?') >= 0 ? '&' : '?') + 'maxHeight=360&quality=90';
    }

    function renderStudioPageContent(page, data) {
        var body = page.querySelector('.fullCrewStudioBody');
        if (!body) {
            return;
        }
        body.innerHTML = '';

        var name = prop(data, 'Name', 'name') || 'Studio';
        var titleEl = page.querySelector('.fullCrewStudioTitle');
        if (titleEl) {
            titleEl.textContent = name;
        }
        try {
            document.title = name + ' — Studio';
        } catch (e) { /* ignore */ }

        var hero = createElement('div', 'fullCrewStudioHero');
        var logoUrl = prop(data, 'LogoUrl', 'logoUrl');
        if (logoUrl) {
            var logo = createElement('img', 'fullCrewStudioLogo');
            logo.src = logoUrl;
            logo.alt = name;
            hero.appendChild(logo);
        } else {
            hero.appendChild(createElement('div', 'fullCrewStudioLogoFallback', name.charAt(0) || '?'));
        }

        var meta = createElement('div', 'fullCrewStudioMeta');
        meta.appendChild(createElement('h2', 'fullCrewStudioName', name));

        var bits = [];
        var hq = prop(data, 'Headquarters', 'headquarters');
        var country = prop(data, 'OriginCountry', 'originCountry');
        var parent = prop(data, 'ParentCompany', 'parentCompany');
        if (hq) { bits.push(hq); }
        if (country) { bits.push(country); }
        if (parent) { bits.push('Parent: ' + parent); }
        if (bits.length) {
            meta.appendChild(createElement('div', 'fullCrewStudioSub', bits.join(' · ')));
        }

        var links = createElement('div', 'fullCrewStudioLinks');
        var homepage = prop(data, 'Homepage', 'homepage');
        var tmdbUrl = prop(data, 'TmdbUrl', 'tmdbUrl');
        if (tmdbUrl) {
            var tmdb = createElement('a', 'fullCrewStudioExtLink', 'TMDB');
            tmdb.href = tmdbUrl;
            tmdb.target = '_blank';
            tmdb.rel = 'noopener noreferrer';
            links.appendChild(tmdb);
        }
        if (homepage) {
            var home = createElement('a', 'fullCrewStudioExtLink', 'Homepage');
            home.href = homepage;
            home.target = '_blank';
            home.rel = 'noopener noreferrer';
            links.appendChild(home);
        }
        if (links.childNodes.length) {
            meta.appendChild(links);
        }

        hero.appendChild(meta);
        body.appendChild(hero);

        var overview = prop(data, 'Overview', 'overview');
        if (overview) {
            body.appendChild(createElement('p', 'fullCrewStudioOverview', overview));
        }

        var stats = prop(data, 'Stats', 'stats');
        if (stats && (prop(stats, 'TotalCount', 'totalCount') || 0) > 0) {
            body.appendChild(renderStudioStatsSection(stats));
        }

        var branches = prop(data, 'Branches', 'branches') || [];
        if (branches.length > 1) {
            var branchSection = createElement('section', 'fullCrewStudioSection');
            branchSection.appendChild(createElement('h3', 'fullCrewStudioSectionTitle', 'Studio variants'));
            var branchList = createElement('ul', 'fullCrewStudioBranches');
            branches.forEach(function (b) {
                var li = createElement('li');
                var a = createElement('a', 'fullCrewStatsItemLink', String(b));
                a.href = studioPageHash({ name: b, itemType: 'Studio' });
                a.addEventListener('click', function (ev) {
                    if (ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) { return; }
                    ev.preventDefault();
                    window.location.hash = studioPageHash({ name: b, itemType: 'Studio' });
                });
                li.appendChild(a);
                branchList.appendChild(li);
            });
            branchSection.appendChild(branchList);
            body.appendChild(branchSection);
        }

        var titles = prop(data, 'Titles', 'titles') || [];
        var gridSection = createElement('section', 'fullCrewStudioSection');
        gridSection.appendChild(
            createElement('h3', 'fullCrewStudioSectionTitle', 'In your library (' + titles.length + ')')
        );

        if (!titles.length) {
            gridSection.appendChild(
                createElement('div', 'fullCrewStatsEmpty', 'No movies or series credited to this studio in your library.')
            );
        } else {
            var grid = createElement('div', 'fullCrewStudioGrid');
            titles.forEach(function (t) {
                var id = prop(t, 'Id', 'id');
                var card = createElement('a', 'fullCrewStudioCard');
                card.href = detailsHashForItem(id);
                card.addEventListener('click', function (ev) {
                    if (ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) { return; }
                    if (navigateToItem(id)) { ev.preventDefault(); }
                });

                var poster = createElement('div', 'fullCrewStudioPoster');
                var imgUrl = primaryImageUrl(id);
                if (imgUrl && prop(t, 'ImageTag', 'imageTag')) {
                    var img = createElement('img', 'fullCrewStudioPosterImg');
                    img.src = imgUrl;
                    img.alt = prop(t, 'Name', 'name') || '';
                    img.loading = 'lazy';
                    poster.appendChild(img);
                } else {
                    poster.appendChild(
                        createElement('div', 'fullCrewStudioPosterFallback', (prop(t, 'Type', 'type') || 'Title').charAt(0))
                    );
                }
                card.appendChild(poster);

                var caption = createElement('div', 'fullCrewStudioCardCaption');
                caption.appendChild(createElement('div', 'fullCrewStudioCardName', prop(t, 'Name', 'name') || 'Untitled'));
                var year = prop(t, 'ProductionYear', 'productionYear');
                var type = prop(t, 'Type', 'type');
                var rating = prop(t, 'CommunityRating', 'communityRating');
                var line = [];
                if (type) { line.push(type); }
                if (year) { line.push(String(year)); }
                if (rating) { line.push('★ ' + Number(rating).toFixed(1)); }
                if (line.length) {
                    caption.appendChild(createElement('div', 'fullCrewStudioCardMeta', line.join(' · ')));
                }
                card.appendChild(caption);
                grid.appendChild(card);
            });
            gridSection.appendChild(grid);
        }
        body.appendChild(gridSection);

        var missing = prop(data, 'MissingPopular', 'missingPopular') || [];
        if (missing.length) {
            var missSection = createElement('section', 'fullCrewStudioSection');
            missSection.appendChild(createElement('h3', 'fullCrewStudioSectionTitle', 'Missing popular titles'));
            missSection.appendChild(createElement(
                'p',
                'fullCrewStudioHint',
                'Popular on TMDB for this company, not matched in your library (name match — alternate titles may hide).'
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
                    var a = createElement('a', 'fullCrewStatsItemLink', text);
                    a.href = href;
                    a.target = '_blank';
                    a.rel = 'noopener noreferrer';
                    li.appendChild(a);
                } else {
                    li.textContent = text;
                }
                missList.appendChild(li);
            });
            missSection.appendChild(missList);
            body.appendChild(missSection);
        }
    }

    function renderStudioStatsSection(stats) {
        var section = createElement('section', 'fullCrewStudioSection');
        section.appendChild(createElement('h3', 'fullCrewStudioSectionTitle', 'Your statistics'));

        var chips = createElement('div', 'fullCrewStudioStatChips');
        chips.appendChild(statChip(String(prop(stats, 'TotalCount', 'totalCount') || 0), 'titles'));
        chips.appendChild(statChip(String(prop(stats, 'MovieCount', 'movieCount') || 0), 'movies'));
        chips.appendChild(statChip(String(prop(stats, 'SeriesCount', 'seriesCount') || 0), 'series'));
        section.appendChild(chips);

        var facts = createElement('ul', 'fullCrewStudioStatFacts');
        var firstY = prop(stats, 'FirstReleaseYear', 'firstReleaseYear');
        var newestY = prop(stats, 'NewestReleaseYear', 'newestReleaseYear');
        if (firstY) { facts.appendChild(statFact('First release', String(firstY))); }
        if (newestY) { facts.appendChild(statFact('Newest release', String(newestY))); }
        var avg = prop(stats, 'AverageCommunityRating', 'averageCommunityRating');
        var ratedN = prop(stats, 'RatedTitleCount', 'ratedTitleCount') || 0;
        if (avg && ratedN) {
            facts.appendChild(statFact('Average rating', '★ ' + Number(avg).toFixed(1) + ' (' + ratedN + ' rated)'));
        }

        appendStudioLinkedFact(facts, 'Highest rated', prop(stats, 'HighestRatedTitle', 'highestRatedTitle'), prop(stats, 'HighestRatedTitleId', 'highestRatedTitleId'), prop(stats, 'HighestRatedValue', 'highestRatedValue') ? '★ ' + Number(prop(stats, 'HighestRatedValue', 'highestRatedValue')).toFixed(1) : null);
        appendStudioLinkedFact(facts, 'Oldest', prop(stats, 'OldestTitle', 'oldestTitle'), prop(stats, 'OldestTitleId', 'oldestTitleId'), firstY ? String(firstY) : null);
        appendStudioLinkedFact(facts, 'Newest', prop(stats, 'NewestTitle', 'newestTitle'), prop(stats, 'NewestTitleId', 'newestTitleId'), newestY ? String(newestY) : null);

        if (facts.childNodes.length) {
            section.appendChild(facts);
        }
        return section;
    }

    function statChip(value, label) {
        var chip = createElement('div', 'fullCrewStudioStatChip');
        chip.appendChild(createElement('div', 'fullCrewStudioStatChipValue', value));
        chip.appendChild(createElement('div', 'fullCrewStudioStatChipLabel', label));
        return chip;
    }

    function statFact(label, value) {
        var li = createElement('li', 'fullCrewStudioStatFact');
        li.appendChild(createElement('span', 'fullCrewStudioStatFactLabel', label));
        li.appendChild(createElement('span', 'fullCrewStudioStatFactValue', value));
        return li;
    }

    function appendStudioLinkedFact(list, label, title, id, extra) {
        if (!title) { return; }
        var li = createElement('li', 'fullCrewStudioStatFact');
        li.appendChild(createElement('span', 'fullCrewStudioStatFactLabel', label));
        var valueWrap = createElement('span', 'fullCrewStudioStatFactValue');
        if (id) {
            var a = createElement('a', 'fullCrewStatsItemLink', title);
            a.href = detailsHashForItem(id);
            a.addEventListener('click', function (ev) {
                if (ev.metaKey || ev.ctrlKey || ev.shiftKey || ev.altKey) { return; }
                if (navigateToItem(id)) { ev.preventDefault(); }
            });
            valueWrap.appendChild(a);
        } else {
            valueWrap.appendChild(document.createTextNode(title));
        }
        if (extra) {
            valueWrap.appendChild(document.createTextNode(' · ' + extra));
        }
        li.appendChild(valueWrap);
        list.appendChild(li);
    }

    function mountStudioPage() {
        var route = parseStudioRoute();
        if (!route) {
            tearDownStudioPage();
            return;
        }

        ensureStyles();
        ensureHeaderTabsVisible();
        injectStatsTab();

        var mount = findStatsMountPoint();
        var existing = document.getElementById(STUDIO_PAGE_ID);
        var routeKey = route.name + '|' + (route.id || '') + '|' + (route.branches || []).join(',');
        if (existing && existing.getAttribute('data-route') === routeKey && existing.getAttribute('data-loaded') === '1') {
            document.body.classList.add('fullCrewStudioActive');
            setStatsTabSelected(false);
            return;
        }

        tearDownStudioPage();
        tearDownStatsPage();

        var page = createElement('div', 'fullCrewStudioPage');
        page.id = STUDIO_PAGE_ID;
        page.setAttribute('role', 'main');
        page.setAttribute('data-route', routeKey);

        var header = createElement('div', 'fullCrewStudioHeader');
        var titleWrap = createElement('div', 'fullCrewStatsTitleWrap');
        var back = createElement('a', 'fullCrewStatsBack', '← Stats · Studios');
        back.href = statsDetailHash('studios');
        titleWrap.appendChild(back);
        titleWrap.appendChild(createElement('h1', 'fullCrewStudioTitle', 'Loading…'));
        header.appendChild(titleWrap);
        page.appendChild(header);

        var body = createElement('div', 'fullCrewStudioBody');
        body.appendChild(createElement('div', 'fullCrewStatsStatus', 'Loading studio…'));
        page.appendChild(body);

        mount.appendChild(page);
        document.body.classList.add('fullCrewStudioActive');
        setStatsTabSelected(false);

        fetchStudioPage(route)
            .then(function (data) {
                page.setAttribute('data-loaded', '1');
                renderStudioPageContent(page, data);
            })
            .catch(function (err) {
                console.warn('[FullCrew] studio page failed', err);
                body.innerHTML = '';
                body.appendChild(
                    createElement('div', 'fullCrewStatsStatus', 'Could not load studio details.')
                );
            });
    }

    function scanAll() {
        scan();
        syncStatsUi();
    }

    function start() {
        ensureStyles();
        refreshStatsEnabled().then(function () {
            scanAll();
        });
        scan();

        var observer = new MutationObserver(function () {
            window.clearTimeout(start._timer);
            start._timer = window.setTimeout(scanAll, 250);
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        window.addEventListener('hashchange', function () {
            window.setTimeout(scanAll, 200);
        });

        document.addEventListener('viewshow', function (e) {
            var detail = e && e.detail;
            var view = (detail && detail.element) || (e && e.target);
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
