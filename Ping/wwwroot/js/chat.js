var connection = new signalR.HubConnectionBuilder().withUrl("/chatHub").build();
window.connection = connection;
window.chatIsEditing = false;
window.chatActiveEditId = null;

function scrollMessagesToBottom(smooth = true) {
    const list = document.getElementById('messagesList');
    if (!list) return;
    list.scrollTo({ top: list.scrollHeight, behavior: smooth ? 'smooth' : 'auto' });
}

function processDOMMessages(targetNode) {
    if (!targetNode) return;
    const textNodes = targetNode.querySelectorAll('.message-bubble .text');
    textNodes.forEach(node => {
        const content = node.textContent.trim();
        if (content.startsWith('http') && (content.includes('.gif') || content.includes('giphy.com'))) {
            if (!node.querySelector('img')) {
                node.innerHTML = `<img src="${content}" alt="GIF" style="max-width: 200px; border-radius: 8px; display: block;" />`;
            }
        } else if (window.twemoji) {
            window.twemoji.parse(node, { className: 'msg-twemoji' });
        }
    });
}

function initChatPartial() {
    const messagesList = document.getElementById('messagesList');
    if (!messagesList) return;
    processDOMMessages(messagesList);
    scrollMessagesToBottom(false);
    setTimeout(() => scrollMessagesToBottom(false), 150);
    if (typeof window.initVoicePlayers === 'function') window.initVoicePlayers();
}

(function () {
    const init = () => {
        const dynamic = document.getElementById('dynamic-content');
        if (!dynamic) return;
        const observer = new MutationObserver(mutations => {
            let hasNewBubbles = false;
            mutations.forEach(m => {
                m.addedNodes.forEach(node => {
                    if (node.nodeType !== 1) return;
                    if (node.classList && node.classList.contains('message-bubble')) {
                        hasNewBubbles = true;
                        processDOMMessages(node.parentElement || node);
                    } else if (node.querySelector && node.querySelector('.message-bubble')) {
                        processDOMMessages(node);
                    }
                    const imgs = node.querySelectorAll ? node.querySelectorAll('img') : [];
                    imgs.forEach(img => { img.onload = () => scrollMessagesToBottom(true); });
                });
            });
            if (hasNewBubbles) {
                if (typeof window.initVoicePlayers === 'function') window.initVoicePlayers();
                setTimeout(() => scrollMessagesToBottom(true), 50);
            }
        });
        observer.observe(dynamic, { childList: true, subtree: true });
    };
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
})();

$(document).ready(function () {
    const searchInput = $('#chat-search');
    const searchDropdown = $('#search-results-dropdown');

    initChatPartial();

    searchInput.on('keyup', function () {
        const value = $(this).val().toLowerCase().trim();
        $(".nav-column .nav-link-custom").filter(function () {
            $(this).toggle($(this).text().toLowerCase().indexOf(value) > -1);
        });
        if (value.length >= 2) {
            $.ajax({
                url: '/Messages/SearchUsers', type: 'GET', data: { query: value },
                success: function (users) {
                    searchDropdown.empty();
                    if (users.length > 0) {
                        users.forEach(u => {
                            searchDropdown.append(`
                                <li>
                                    <a class="dropdown-item d-flex align-items-center p-2" href="#" onclick="startNewChat(${u.id}); return false;" style="border-bottom: 1px solid #eee;">
                                        <div class="avatar-circle me-3" style="width: 40px; height: 40px; font-size: 16px;">${u.name.substring(0, 1).toUpperCase()}</div>
                                        <div class="chat-info">
                                            <div class="chat-name text-dark"><strong>${u.name}</strong></div>
                                            <div class="chat-last-msg text-muted" style="font-size: 0.8rem;">@${u.username}</div>
                                        </div>
                                    </a>
                                </li>`);
                        });
                        searchDropdown.show();
                    } else {
                        searchDropdown.html('<li class="p-3 text-muted text-center">Пользователи не найдены</li>').show();
                    }
                }
            });
        } else {
            searchDropdown.hide();
        }
    });

    $(document).click(function (e) {
        if (!$(e.target).closest('.search-wrapper').length) searchDropdown.hide();
    });
});

function startNewChat(targetUserId) {
    $.ajax({
        url: '/Messages/StartChat', type: 'POST', data: { targetUserId: targetUserId },
        success: function (response) {
            if (response && response.chatId) {
                $('#search-results-dropdown').hide();
                $('#chat-search').val('');
                $(".nav-column .nav-link-custom").show();
                navigateTo('/Messages/Chat/' + response.chatId);
            }
        },
        error: function () { alert("Ошибка при создании диалога."); }
    });
}

function deleteChat(chatId) {
    if (!confirm("Вы уверены, что хотите удалить этот чат?")) return;
    $.ajax({
        url: '/Messages/DeleteChat', type: 'POST', data: { chatId: chatId },
        success: function () {
            $(`.nav-link-custom[onclick*='Chat/${chatId}']`).fadeOut(300, function () { $(this).remove(); });
            $('#dynamic-content').html(`<div class="h-100 d-flex align-items-center justify-content-center text-muted"><p>Чат удален.</p></div>`);
        },
        error: function () { alert("Ошибка при удалении чата."); }
    });
}

connection.on("ReceiveMessage", function (chatId, senderId, messageId, content, time) {
    const currentChatId = $('#currentChatId').val();
    const currentUserId = $('#currentUserId').val();

    if (chatId == currentChatId) {
        const isMine = senderId == currentUserId;
        const ticksHtml = isMine ? `
            <span class="tg-ticks">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                    <polyline points="7 12 12 17 22 7"></polyline>
                    <polyline points="2 12 7 17 12 12"></polyline>
                </svg>
            </span>` : '';

        let finalContent = content;
        if (content && content.startsWith('[voice]')) {
            const audioUrl = content.replace('[voice]', '');
            finalContent = `
                <div class="voice-player" data-src="${audioUrl}">
                    <button class="voice-play-btn" type="button">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>
                    </button>
                    <div class="voice-track"><div class="voice-progress" style="width:0%"></div><div class="voice-thumb" style="left:0%"></div></div>
                    <div class="voice-time">0:00</div>
                </div>`;
        } else if (content && content.startsWith('[image]')) {
            const imgUrl = content.substring(7);
            finalContent = `<img src="${imgUrl}" alt="Зображення" class="msg-image" />`;
        }

        const msgHtml = `
            <div class="message-bubble ${isMine ? 'mine' : 'theirs'}"
                 data-id="${messageId}" data-time="${new Date().getTime()}" data-mine="${isMine}">
                <div class="text">${finalContent}</div>
                <div class="msg-footer">
                    <small class="edited-label msg-meta" style="display: none;">изменено</small>
                    <small class="time msg-meta">${time}</small>
                    ${ticksHtml}
                </div>
            </div>`;

        $('#noMessages').remove();
        $('#messagesList').append(msgHtml);

        if (!isMine) connection.invoke("ReadChat", parseInt(chatId), parseInt(currentUserId));
    } else {
        const badge = $(`#unread-badge-${chatId}`);
        const currentCount = (parseInt(badge.text()) || 0) + 1;
        badge.text(currentCount).css('display', 'flex');

    window.updateChatListOrder(chatId, content);
});

connection.on("ChatRead", function (chatId, readerId) {
    const currentChatId = $('#currentChatId').val();
    const currentUserId = $('#currentUserId').val();
    if (chatId == currentChatId && readerId != currentUserId) {
        $('.mine .tg-ticks').addClass('is-read');
    }

});

connection.on("MessageEdited", function (messageId, newContent) {
    const bubble = $(`.message-bubble[data-id='${messageId}']`);
    if (bubble.length > 0) {
        bubble.find('.text').text(newContent);
        
        let label = bubble.find('.edited-label');
        if (label.length > 0) {
            label.css('display', 'inline');
        } else {
            bubble.find('.time').before('<small class="edited-label text-muted" style="font-size: 0.65rem; margin-right: 4px;">изменено</small>');
        }
    }
});

connection.start().then(function () {
    const currentChatId = $('#currentChatId').val();
    if (currentChatId) connection.invoke("JoinChat", currentChatId);
}).catch(err => console.error(err.toString()));

function sendMessageAction() {
    const chatId = $('#currentChatId').val();
    const senderId = $('#currentUserId').val();
    const content = $('#messageInput').val().trim();
    if (!content || !chatId || !senderId) return;

    if (window.chatIsEditing && window.chatActiveEditId) {
        $.ajax({
            url: '/Messages/EditMessage', type: 'POST',
            data: { messageId: window.chatActiveEditId, newContent: content },
            success: function (res) {
                const bubble = $(`.message-bubble[data-id='${window.chatActiveEditId}']`);
                bubble.find('.text').text(res.content);

                let label = bubble.find('.edited-label');
                if (label.length > 0) {
                    label.css('display', 'inline');
                } else {
                    bubble.find('.time').before('<small class="edited-label text-muted" style="font-size: 0.65rem; margin-right: 4px;">изменено</small>');
                }

                cancelEditMode();
            },
            error: function () {
                alert("Не удалось изменить сообщение.");
                cancelEditMode();
            }
        });
    } else {
        connection.invoke("SendMessage", parseInt(chatId), parseInt(senderId), content)
            .then(() => $('#messageInput').val(''))
            .catch(err => console.error(err.toString()));
    }
}
window.sendMessageAction = sendMessageAction;

window.cancelEditMode = function () {
    window.chatIsEditing = false;
    window.chatActiveEditId = null;
    $('#editModeIndicator').hide();
    $('#messageInput').val('');
};

$(document).off('click', '#sendButton').on('click', '#sendButton', sendMessageAction);
$(document).off('keydown', '#messageInput').on('keydown', '#messageInput', function (e) {
    if (e.key === 'Enter') { e.preventDefault(); sendMessageAction(); }
});

$(document).ajaxComplete(function (event, xhr, settings) {
    if (settings.url && settings.url.includes('/Messages/Chat/')) {
        const segments = settings.url.split('/');
        const chatId = segments[segments.length - 1];
        $(`#unread-badge-${chatId}`).css('display', 'none').text('0');
        if (connection.state === signalR.HubConnectionState.Connected) {
            connection.invoke("JoinChat", chatId);
        }
        setTimeout(initChatPartial, 100);
    }
});

(function () {
    if (window.__chatContextMenuInit) return;
    window.__chatContextMenuInit = true;

    let activeMessageElement = null;
    let activeMessageId = null;

    document.addEventListener('click', (e) => {
        const menu = document.getElementById('tgContextMenu');
        if (menu && e.button !== 2) menu.style.display = 'none';
        if (e.target.closest('#menuCopy')) handleCopy();
        if (e.target.closest('#menuEdit')) handleEdit();
        if (e.target.closest('#menuDelete')) handleDelete();
        if (e.target.closest('#cancelEditBtn') && window.cancelEditMode) window.cancelEditMode();
        const quickReact = e.target.closest('.quick-reaction');
        if (quickReact && activeMessageId) {
            const emoji = quickReact.getAttribute('data-emoji');
            window.toggleReaction(activeMessageId, emoji);
            document.getElementById('tgContextMenu').style.display = 'none';
            return;
        }
    });

    document.addEventListener('contextmenu', (e) => {
        const messagesList = document.getElementById('messagesList');
        if (!messagesList) return;
        const bubble = e.target.closest('.message-bubble');
        if (!bubble || !messagesList.contains(bubble)) return;

        e.preventDefault();

        const menu = document.getElementById('tgContextMenu');
        const menuCopy = document.getElementById('menuCopy');
        const menuEdit = document.getElementById('menuEdit');
        const menuDelete = document.getElementById('menuDelete');
        if (!menu || !menuCopy || !menuEdit || !menuDelete) return;

        const isMine = (bubble.getAttribute('data-mine') || '').toLowerCase() === 'true';
        const timestamp = bubble.getAttribute('data-time');
        const messageId = bubble.getAttribute('data-id');
        let diffMinutes = 0;
        if (timestamp) {
            const t = new Date(parseInt(timestamp));
            if (!isNaN(t.getTime())) diffMinutes = (new Date() - t) / 1000 / 60;
        }

        const isVoice = bubble.querySelector('.voice-player') !== null;
        const isGif = bubble.querySelector('img[alt="GIF"]') !== null;
        const isImage = bubble.querySelector('img[alt="Зображення"]') !== null;

        menuCopy.style.display = 'flex';
        menuEdit.style.display = 'flex';
        menuDelete.style.display = 'flex';

        if (!isMine || !messageId || diffMinutes > 1) {
            menuEdit.style.display = 'none';
            menuDelete.style.display = 'none';
        }

        if (isVoice) {
            menuCopy.style.display = 'none';
            menuEdit.style.display = 'none';
        }

        if (isGif) {
            menuEdit.style.display = 'none';
        }

        if (isImage) {
            menuCopy.style.display = 'none';
            menuEdit.style.display = 'none';
        }

        activeMessageId = messageId;
        activeMessageElement = bubble;

        menu.style.display = 'block';
        const menuW = menu.offsetWidth;
        const menuH = menu.offsetHeight;
        let x = e.clientX, y = e.clientY;
        if (x + menuW > window.innerWidth) x = window.innerWidth - menuW - 10;
        if (y + menuH > window.innerHeight) y = window.innerHeight - menuH - 10;
        if (x < 10) x = 10;
        if (y < 10) y = 10;
        menu.style.left = `${x}px`;
        menu.style.top = `${y}px`;
    });

    function handleCopy() {
        if (!activeMessageElement || activeMessageElement.querySelector('.voice-player')) return;
        const textContainer = activeMessageElement.querySelector('.text');
        let textToCopy = textContainer.textContent.trim();
        if (textContainer.querySelector('img[alt="GIF"]')) {
            textToCopy = textContainer.querySelector('img').src;
        }
        navigator.clipboard.writeText(textToCopy).catch(err => console.error(err));
    }

    function handleEdit() {
        if (!activeMessageElement || !activeMessageId) return;
        if (activeMessageElement.querySelector('.voice-player') || activeMessageElement.querySelector('img[alt="GIF"]') || activeMessageElement.querySelector('img[alt="Зображення"]')) return;
        const textContainer = activeMessageElement.querySelector('.text');
        const messageInput = document.getElementById('messageInput');
        const editModeIndicator = document.getElementById('editModeIndicator');
        window.chatIsEditing = true;
        window.chatActiveEditId = activeMessageId;
        if (messageInput) { messageInput.value = textContainer.textContent.trim(); messageInput.focus(); }
        if (editModeIndicator) editModeIndicator.style.display = 'block';
    }

    function handleDelete() {
        if (!activeMessageElement || !activeMessageId) return;
        if (!confirm("Удалить это сообщение?")) return;
        const idToDelete = activeMessageId;
        const formData = new FormData();
        formData.append("messageId", idToDelete);
        fetch('/Messages/DeleteMessage', { method: 'POST', body: formData })
            .then(async res => {
                if (res.ok) {
                    activeMessageElement.remove();
                    if (window.chatActiveEditId == idToDelete && window.cancelEditMode) window.cancelEditMode();
                } else {
                    const errData = await res.json().catch(() => ({ error: 'Ошибка' }));
                    alert(errData.error || "Не удалось удалить сообщение.");
                }
            })
            .catch(err => { console.error(err); alert("Ошибка соединения."); });
    }
})();

connection.on("UserOnlineStatus", function (userId, isOnline) {
    let statusEl = document.getElementById("companion-status-" + userId);

    if (statusEl) {
        if (isOnline) {
            statusEl.innerHTML = '<span class="status-online">в мережі</span>';
        } else {
            statusEl.innerHTML = '<span class="status-offline">не в мережі</span>';
        }
    }
});

window.toggleReaction = function (messageId, emoji) {
    const userId = document.getElementById('currentUserId')?.value;
    if (!userId || !window.connection) return;
    window.connection.invoke("ToggleReaction", parseInt(messageId), parseInt(userId), emoji)
        .catch(err => console.error(err));
};

connection.on("ReactionUpdated", function (messageId, reactions) {
    const bubble = document.querySelector(`.message-bubble[data-id='${messageId}']`);
    if (!bubble) return;

    let row = bubble.querySelector('.reactions-row');
    if (!reactions || reactions.length === 0) {
        if (row) row.remove();
        return;
    }

    if (!row) {
        row = document.createElement('div');
        row.className = 'reactions-row';
        row.setAttribute('data-msg-id', messageId);
        bubble.appendChild(row);
    }

    const currentUserId = parseInt(document.getElementById('currentUserId').value);
    row.innerHTML = reactions.map(r => {
        const isMine = r.userIds.includes(currentUserId);
        return `<span class="reaction-chip ${isMine ? 'mine' : ''}" 
                      onclick="toggleReaction(${messageId}, '${r.emoji}')">
                  ${r.emoji} <span class="count">${r.count}</span>
                </span>`;
    }).join('');
});