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
                if (role) {
                    text.appendChild(createElement('div', 'fullCrewPersonRole', role));
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

        var existing = view.querySelector('#' + SECTION_ID);
        if (existing && existing.getAttribute('data-item-id') === itemId) {
            return;
        }

        ensureStyles();
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

    function start() {
        ensureStyles();
        scan();

        var observer = new MutationObserver(function () {
            window.clearTimeout(start._timer);
            start._timer = window.setTimeout(scan, 250);
        });

        observer.observe(document.body, {
            childList: true,
            subtree: true
        });

        window.addEventListener('hashchange', function () {
            window.setTimeout(scan, 300);
        });

        document.addEventListener('viewshow', function (e) {
            var detail = e && e.detail;
            var view = (detail && detail.element) || (e && e.target);
            if (view) {
                window.setTimeout(function () {
                    mount(view);
                }, 200);
            } else {
                window.setTimeout(scan, 200);
            }
        }, true);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
