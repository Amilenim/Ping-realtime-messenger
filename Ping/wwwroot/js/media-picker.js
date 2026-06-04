(function () {
    'use strict';
    if (window.__mediaPickerInitialized) return;
    window.__mediaPickerInitialized = true;

    const GIPHY_API_KEY = "0TkvrkdR0UAfuVSiag7eWW9AwSKMm3FQ";
    let gifTypingTimer = null;
    let pickerObserverAttached = false;

    function setupPicker() {
        const picker = document.querySelector('emoji-picker');
        if (!picker) return;

        if (!picker.shadowRoot?.querySelector('.twemoji-style-loaded')) {
            const style = document.createElement('style');
            style.className = 'twemoji-style-loaded';
            style.textContent = `
            .twemoji { width: 1.7rem !important; height: 1.7rem !important; pointer-events: none; }
            :host { overflow-x: hidden !important; }
            .picker { overflow-x: hidden !important; }
            .tabpanel { overflow-x: hidden !important; }
        `;
            if (picker.shadowRoot) picker.shadowRoot.appendChild(style);

            if (!pickerObserverAttached && picker.shadowRoot) {
                pickerObserverAttached = true;
                const obs = new MutationObserver(() => {
                    const emojiButtons = picker.shadowRoot.querySelectorAll('.emoji');
                    emojiButtons.forEach(btn => {
                        if (!btn.querySelector('.twemoji') && window.twemoji) {
                            window.twemoji.parse(btn, { className: 'twemoji' });
                        }
                    });
                });
                obs.observe(picker.shadowRoot, { subtree: true, childList: true });
            }
        }

        if (picker.dataset.emojiHandlerAttached !== 'true') {
            picker.dataset.emojiHandlerAttached = 'true';
            console.log('[media-picker] навешиваю emoji-click на', picker);

            picker.addEventListener('emoji-click', (event) => {
                console.log('[media-picker] emoji-click detail:', event.detail);

                const messageInput = document.getElementById('messageInput');
                if (!messageInput) {
                    console.warn('[media-picker] не нашёл #messageInput');
                    return;
                }

                const detail = event.detail || {};
                const emoji = detail.unicode || detail.emoji?.unicode;
                if (!emoji) {
                    console.warn('[media-picker] нет unicode в detail:', detail);
                    return;
                }

                const start = messageInput.selectionStart;
                const end = messageInput.selectionEnd;
                const text = messageInput.value;
                messageInput.value = text.slice(0, start) + emoji + text.slice(end);
                messageInput.selectionStart = messageInput.selectionEnd = start + emoji.length;
                messageInput.focus();
            });
        }
    }

    function loadGifs(query = '') {
        const gifResults = document.getElementById('gif-results');
        if (!gifResults) return;

        const url = query.trim()
            ? `https://api.giphy.com/v1/gifs/search?api_key=${GIPHY_API_KEY}&q=${encodeURIComponent(query)}&limit=21`
            : `https://api.giphy.com/v1/gifs/trending?api_key=${GIPHY_API_KEY}&limit=21`;

        fetch(url)
            .then(r => r.json())
            .then(data => {
                gifResults.innerHTML = '';
                data.data.forEach(gif => {
                    const img = document.createElement('img');
                    img.src = gif.images.fixed_height_small.url;
                    img.style.cssText = 'width:100%;height:85px;object-fit:cover;cursor:pointer;border-radius:6px;transition:transform .15s ease;';
                    img.onmouseenter = () => img.style.transform = 'scale(1.03)';
                    img.onmouseleave = () => img.style.transform = 'scale(1)';
                    img.onclick = () => {
                        const messageInput = document.getElementById('messageInput');
                        if (!messageInput) return;
                        messageInput.value = gif.images.original.url;
                        if (typeof window.sendMessageAction === 'function') {
                            window.sendMessageAction();
                        }
                        const panel = document.getElementById('tgMediaPicker');
                        if (panel) panel.style.display = 'none';
                    };
                    gifResults.appendChild(img);
                });
            })
            .catch(err => console.error("Ошибка загрузки GIF:", err));
    }

    document.addEventListener('click', (e) => {
        const toggleBtn = e.target.closest('#panel-toggle-btn');
        if (toggleBtn) {
            e.preventDefault();
            const panel = document.getElementById('tgMediaPicker');
            if (!panel) return;
            const isHidden = panel.style.display === 'none' || panel.style.display === '';
            panel.style.display = isHidden ? 'flex' : 'none';
            if (isHidden) {
                setupPicker();
                const activeTab = document.querySelector('.tg-tab-btn.active');
                if (activeTab && activeTab.getAttribute('data-tab') === 'tab-gifs') loadGifs();
            }
            return;
        }

        const tabBtn = e.target.closest('.tg-tab-btn');
        if (tabBtn) {
            document.querySelectorAll('.tg-tab-btn').forEach(b => b.classList.remove('active'));
            document.querySelectorAll('.tg-tab-content').forEach(c => c.classList.remove('active'));
            tabBtn.classList.add('active');
            const targetId = tabBtn.getAttribute('data-tab');
            const target = document.getElementById(targetId);
            if (target) target.classList.add('active');
            if (targetId === 'tab-gifs') {
                const gifSearch = document.getElementById('gif-search');
                loadGifs(gifSearch ? gifSearch.value : '');
            }
            return;
        }

        const panel = document.getElementById('tgMediaPicker');
        if (panel && panel.style.display === 'flex') {
            if (!panel.contains(e.target) && !e.target.closest('#panel-toggle-btn')) {
                panel.style.display = 'none';
            }
        }
    });

    document.addEventListener('keyup', (e) => {
        if (e.target && e.target.id === 'gif-search') {
            clearTimeout(gifTypingTimer);
            gifTypingTimer = setTimeout(() => loadGifs(e.target.value), 400);
        }
    });
})();