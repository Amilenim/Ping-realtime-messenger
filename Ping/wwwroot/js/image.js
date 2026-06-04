(function () {
    'use strict';
    if (window.__imagePickerInitialized) return;
    window.__imagePickerInitialized = true;

    const MAX_SIDE = 1280;
    const JPEG_QUALITY = 0.82;

    function compressImage(file, callback) {
        const img = new Image();
        const url = URL.createObjectURL(file);
        img.onload = function () {
            URL.revokeObjectURL(url);
            let w = img.naturalWidth;
            let h = img.naturalHeight;
            if (w > MAX_SIDE || h > MAX_SIDE) {
                if (w > h) { h = Math.round(h * MAX_SIDE / w); w = MAX_SIDE; }
                else       { w = Math.round(w * MAX_SIDE / h); h = MAX_SIDE; }
            }
            const canvas = document.createElement('canvas');
            canvas.width = w;
            canvas.height = h;
            canvas.getContext('2d').drawImage(img, 0, 0, w, h);
            canvas.toBlob(callback, 'image/jpeg', JPEG_QUALITY);
        };
        img.onerror = function () { URL.revokeObjectURL(url); callback(null); };
        img.src = url;
    }

    async function uploadAndSend(file) {
        const imageBtn = document.getElementById('imageBtn');
        if (imageBtn) imageBtn.disabled = true;

        compressImage(file, async function (blob) {
            if (!blob) {
                if (imageBtn) imageBtn.disabled = false;
                return;
            }
            const formData = new FormData();
            formData.append('imageFile', blob, 'image.jpg');
            try {
                const res = await fetch('/Messages/UploadImage', { method: 'POST', body: formData });
                if (!res.ok) throw new Error('Upload failed');
                const data = await res.json();
                if (data.url && window.connection) {
                    const chatId = document.getElementById('currentChatId')?.value;
                    const senderId = document.getElementById('currentUserId')?.value;
                    if (chatId && senderId) {
                        window.connection.invoke("SendMessage", parseInt(chatId), parseInt(senderId), "[image]" + data.url);
                    }
                }
            } catch (err) {
                console.error("Ошибка загрузки изображения:", err);
            } finally {
                if (imageBtn) imageBtn.disabled = false;
            }
        });
    }

    document.addEventListener('click', (e) => {
        if (e.target.closest('#imageBtn')) {
            const input = document.getElementById('imageInput');
            if (input) { input.value = ''; input.click(); }
        }
    });

    document.addEventListener('change', (e) => {
        if (e.target && e.target.id === 'imageInput') {
            const file = e.target.files[0];
            if (file) uploadAndSend(file);
        }
    });
})();
