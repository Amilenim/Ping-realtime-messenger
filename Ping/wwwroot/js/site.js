// ==========================================================================
// 1. НАВІГАЦІЯ (Безшовний перехід - SPA)
// ==========================================================================
function navigateTo(url, element) {
    document.querySelectorAll('.nav-link-custom').forEach(el => el.classList.remove('active'));
    if (element) element.classList.add('active');

    $.ajax({
        url: url,
        type: 'GET',
        headers: { "X-Requested-With": "XMLHttpRequest" },
        success: function (data) {
            $('#dynamic-content').html(data);
            window.history.pushState({ path: url }, '', url);

            const container = document.querySelector('.messages-list');
            if (container) container.scrollTop = container.scrollHeight;
        },
        error: function () {
            alert("Помилка при завантаженні сторінки!");
        }
    });
}

window.onpopstate = function (e) {
    if (e.state) {
        navigateTo(window.location.pathname);
    }
};

$(document).ready(function () {
    const urlParams = new URLSearchParams(window.location.search);
    const route = urlParams.get('route');
    if (route) {
        const cleanRoute = route.split('?')[0];
        const matchingLink = $(`.nav-link-custom[onclick*="${cleanRoute}"], .nav-link-custom[onclick*="${route}"]`).get(0);
        navigateTo(route, matchingLink);
    }
});

// ==========================================================================
// 2. ПРОФІЛЬ ТА АВАТАРКИ
// ==========================================================================
function getDeterministicColor(name) {
    if (!name) return "#766ac8";
    let hash = 0;
    for (let i = 0; i < name.length; i++) {
        hash = name.charCodeAt(i) + ((hash << 5) - hash);
    }
    let hue = Math.abs(hash % 360);
    return `hsl(${hue}, 60%, 50%)`;
}

$(document).off('change', '#avatarInput').on('change', '#avatarInput', function () {
    var file = this.files[0];
    if (!file) return;

    var formData = new FormData();
    formData.append('avatar', file);

    $.ajax({
        url: '/Settings/UploadAvatar',
        type: 'POST',
        data: formData,
        contentType: false,
        processData: false,
        success: function (response) {
            if (response.success) {
                $('#avatarContent').html(`<img src="${response.avatarUrl}" class="tg-avatar-img" alt="Avatar" />`);
                $('#btnDeleteAvatar').fadeIn(250);
            } else {
                alert(response.message || "Помилка при завантаженні фото");
            }
        },
        error: function () {
            alert("Помилка зв'язку із сервером");
        }
    });
});

$(document).off('click', '#avatarWrapper').on('click', '#avatarWrapper', function (e) {
    if (e.target.closest('#avatarInput')) return;
    $('#avatarInput').click();
});

if (typeof window.initPhoneInputs === 'function') {
    window.initPhoneInputs();
}

function togglePasswordSection() {
    var $section = $('#passwordSection');
    var $btn = $('#passwordToggleBtn');

    $section.slideToggle(300, function () {
        if ($section.is(':visible')) {
            $btn.text('Звернути');
        } else {
            $btn.text('Змініти пароль');
            $('#CurrentPassword').val('');
            $('#NewPassword').val('');
            $('#ConfirmPassword').val('');
        }
    });
}

function toggleEdit(btn, inputId) {
    const input = document.getElementById(inputId);
    if (!input) return;

    if (input.hasAttribute('readonly')) {
        input.removeAttribute('readonly');
        input.focus();
        btn.classList.add('active-editing');
        btn.innerHTML = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"></polyline></svg>`;
    } else {
        input.setAttribute('readonly', 'readonly');
        btn.classList.remove('active-editing');
        btn.innerHTML = `<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"></path><path d="M18.5 2.5a2.121 2.121 0 1 1 3 3L12 15l-4 1 1-4 9.5-9.5z"></path></svg>`;
    }
}

function submitProfileSettings(form) {
    const phoneInput = document.getElementById('profilePhone');
    const phoneError = document.getElementById('phoneJsError');
    if (phoneError) phoneError.style.display = 'none';

    if (phoneInput && phoneInput.__itiPlugin && phoneInput.value.trim() !== "") {
        if (!phoneInput.__itiPlugin.isValidNumber()) {
            if (phoneError) phoneError.style.display = 'block';
            return;
        }
        phoneInput.value = phoneInput.__itiPlugin.getNumber();
    }

    $.ajax({
        url: form.action,
        type: form.method,
        data: $(form).serialize(),
        success: function (result) {
            $('#dynamic-content').html(result);
        },
        error: function () {
            alert("Помилка при збереженні даних профілю.");
        }
    });
}


//Функція для динамічного оновлення та переміщення чату вгору
function updateChatListOrder(chatId, messageText) {
    const $chatList = $('#chat-list');
    const $chatItem = $chatList.find(`[data-chat-id="${chatId}"]`);

    if ($chatItem.length) {
        $chatItem.find(`#last-msg-${chatId}`).text(messageText);

        $chatItem.fadeOut(150, function () {
            $chatList.prepend($chatItem);
            $chatItem.fadeIn(150);
        });
    } else {}
}


// ==========================================================================
// 3. ГЛОБАЛЬНИЙ КОРЕКТОР ПЛАШОК ЧАТА (Захищений від SPA-переходів)
// ==========================================================================
window.updateChatListOrder = function (chatId, messageText) {
    const $chatList = $('#chat-list');
    const $chatItem = $chatList.find(`[data-chat-id="${chatId}"]`);

    if ($chatItem.length) {
        let txt = (messageText || "").trim();
        let previewText = txt || "Немає повідомлень";

        if (txt.startsWith('[voice]') || txt.includes('/uploads/voice/') || txt.includes('.amr') || txt.includes('.wav') || txt.includes('.mp3')) {
            previewText = 'Голосове повідомлення';
        } else if (txt.startsWith('[image]')) {
            previewText = 'Зображення';
        } else if (txt.includes('.gif') || txt.includes('giphy.com')) {
            previewText = 'GIF';
        }

        $chatItem.find(`#last-msg-${chatId}`).text(previewText);

        if ($chatList.children().first().attr('data-chat-id') != chatId) {
            $chatItem.fadeOut(150, function () {
                $chatList.prepend($chatItem);
                $chatItem.fadeIn(150);
            });
        }
    }
};