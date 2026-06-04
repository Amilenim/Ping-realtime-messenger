(function () {
    'use strict';
    if (window.__phoneInputInitialized) return;
    window.__phoneInputInitialized = true;

    const UTILS_SCRIPT = "https://cdn.jsdelivr.net/npm/intl-tel-input@21.0.7/build/js/utils.js";

    function shake(el) {
        el.style.transition = 'transform 0.1s';
        el.style.transform = "translateX(-5px)";
        setTimeout(() => el.style.transform = "translateX(5px)", 100);
        setTimeout(() => el.style.transform = "translateX(0)", 200);
    }

    function initOne(input) {
        if (input.dataset.phoneInitialized === 'true') return input.__itiPlugin;
        if (!window.intlTelInput) {
            console.warn('intlTelInput не загружен — пропускаю', input);
            return null;
        }
        input.dataset.phoneInitialized = 'true';

        const plugin = window.intlTelInput(input, {
            initialCountry: "auto",
            geoIpLookup: function (success, failure) {
                fetch("https://ipapi.co/json")
                    .then(res => res.json())
                    .then(data => success(data.country_code))
                    .catch(() => failure());
            },
            utilsScript: UTILS_SCRIPT,
        });

        input.__itiPlugin = plugin;

        const form = input.closest('form');
        if (form && !form.dataset.phoneValidationAttached && form.dataset.skipPhoneValidation !== 'true') {
            form.dataset.phoneValidationAttached = 'true';
            form.addEventListener('submit', function (e) {
                const errorEl = form.querySelector('#phoneJsError') || document.getElementById('phoneJsError');
                if (errorEl) errorEl.style.display = 'none';

                form.querySelectorAll('input[type="tel"]').forEach(phoneInput => {
                    const p = phoneInput.__itiPlugin;
                    if (!p) return;
                    if (phoneInput.value.trim() === "") return;

                    if (!p.isValidNumber()) {
                        e.preventDefault();
                        if (errorEl) errorEl.style.display = 'block';
                        shake(phoneInput);
                    } else {
                        phoneInput.value = p.getNumber();
                    }
                });
            });
        }
        return plugin;
    }

    function initAll() {
        document.querySelectorAll('input[type="tel"]').forEach(initOne);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initAll);
    } else {
        initAll();
    }

    window.initPhoneInputs = initAll;
    window.initPhoneInput = initOne;
})();