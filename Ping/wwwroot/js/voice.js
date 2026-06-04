(function () {
    'use strict';
    if (window.__voiceInitialized) return;
    window.__voiceInitialized = true;

    const PLAY_ICON = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M8 5v14l11-7z"/></svg>';
    const PAUSE_ICON = '<svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M6 19h4V5H6v14zm8-14v14h4V5h-4z"/></svg>';

    function formatAudioTime(seconds) {
        if (isNaN(seconds) || !isFinite(seconds)) return "0:00";
        const m = Math.floor(seconds / 60);
        const s = Math.floor(seconds % 60).toString().padStart(2, '0');
        return `${m}:${s}`;
    }

    function formatRecordTime(seconds) {
        const m = Math.floor(seconds / 60).toString().padStart(2, '0');
        const s = Math.floor(seconds % 60).toString().padStart(2, '0');
        return `${m}:${s}`;
    }

    function initVoicePlayers() {
        document.querySelectorAll('.voice-player').forEach(player => {
            if (player.dataset.initialized) return;
            player.dataset.initialized = "true";

            const audioSrc = player.getAttribute('data-src');
            const timeDisplay = player.querySelector('.voice-time');
            const progressBar = player.querySelector('.voice-progress');
            const thumb = player.querySelector('.voice-thumb');
            const track = player.querySelector('.voice-track');
            const playBtn = player.querySelector('.voice-play-btn');
            if (!audioSrc) return;

            if (playBtn) playBtn.innerHTML = PLAY_ICON;

            const inlineAudio = player.querySelector('audio');
            if (inlineAudio) inlineAudio.remove();

            const audio = new Audio();
            audio.preload = 'metadata';
            player.audioObject = audio;

            const updateDuration = () => {
                if (timeDisplay && isFinite(audio.duration) && audio.duration > 0) {
                    timeDisplay.textContent = formatAudioTime(audio.duration);
                }
            };

            audio.addEventListener('loadedmetadata', updateDuration);
            audio.addEventListener('durationchange', updateDuration);
            audio.addEventListener('timeupdate', () => {
                const pct = (audio.currentTime / audio.duration) * 100 || 0;
                if (progressBar) progressBar.style.width = `${pct}%`;
                if (thumb) thumb.style.left = `${pct}%`;
                if (timeDisplay) timeDisplay.textContent = formatAudioTime(audio.currentTime);
            });
            audio.addEventListener('ended', () => {
                if (progressBar) progressBar.style.width = '0%';
                if (thumb) thumb.style.left = '0%';
                if (timeDisplay) timeDisplay.textContent = formatAudioTime(audio.duration);
                if (playBtn) playBtn.innerHTML = PLAY_ICON;
            });

            audio.src = audioSrc;
            audio.load();
            if (audio.readyState >= 1) updateDuration();

            if (playBtn) {
                playBtn.addEventListener('click', (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    document.querySelectorAll('.voice-player').forEach(p => {
                        if (p !== player && p.audioObject && !p.audioObject.paused) {
                            p.audioObject.pause();
                            const b = p.querySelector('.voice-play-btn');
                            if (b) b.innerHTML = PLAY_ICON;
                        }
                    });
                    if (audio.paused) {
                        audio.play();
                        playBtn.innerHTML = PAUSE_ICON;
                    } else {
                        audio.pause();
                        playBtn.innerHTML = PLAY_ICON;
                    }
                });
            }

            if (track) {
                track.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const rect = track.getBoundingClientRect();
                    const pct = (e.clientX - rect.left) / rect.width;
                    if (audio.duration) audio.currentTime = pct * audio.duration;
                });
            }
        });
    }
    window.initVoicePlayers = initVoicePlayers;

    let mediaRecorder = null;
    let audioChunks = [];
    let recordInterval = null;
    let recordSeconds = 0;
    let isRecording = false;
    let shouldSaveVoice = false;

    async function startRecording() {
        try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            mediaRecorder = new MediaRecorder(stream);

            mediaRecorder.ondataavailable = e => {
                if (e.data.size > 0) audioChunks.push(e.data);
            };

            mediaRecorder.onstop = async () => {
                stream.getTracks().forEach(track => track.stop());
                if (!shouldSaveVoice) { audioChunks = []; return; }
                if (audioChunks.length === 0) return;

                const audioBlob = new Blob(audioChunks, { type: 'audio/webm' });
                audioChunks = [];

                const formData = new FormData();
                formData.append('audioFile', audioBlob);

                try {
                    const res = await fetch('/Messages/UploadVoice', { method: 'POST', body: formData });
                    const data = await res.json();
                    if (data.url && window.connection) {
                        const chatId = document.getElementById('currentChatId')?.value;
                        const senderId = document.getElementById('currentUserId')?.value;
                        if (chatId && senderId) {
                            window.connection.invoke("SendMessage", parseInt(chatId), parseInt(senderId), "[voice]" + data.url);
                        }
                    }
                } catch (err) {
                    console.error("Ошибка загрузки аудио:", err);
                }
            };

            audioChunks = [];
            shouldSaveVoice = true;
            mediaRecorder.start();
            isRecording = true;
            toggleRecordingUI(true);

            recordSeconds = 0;
            const recordTimer = document.getElementById('recordTimer');
            if (recordTimer) recordTimer.textContent = "00:00";
            recordInterval = setInterval(() => {
                recordSeconds++;
                const rt = document.getElementById('recordTimer');
                if (rt) rt.textContent = formatRecordTime(recordSeconds);
            }, 1000);
        } catch (err) {
            alert("Не удалось получить доступ к микрофону.");
            console.error(err);
        }
    }

    function stopRecording(save) {
        if (!isRecording) return;
        isRecording = false;
        clearInterval(recordInterval);
        shouldSaveVoice = save;

        if (mediaRecorder && mediaRecorder.state !== 'inactive') {
            mediaRecorder.stop();
        }
        toggleRecordingUI(false);
    }

    function toggleRecordingUI(recording) {
        const input = document.getElementById('messageInput');
        const recordingPanel = document.getElementById('recordingPanel');
        const micBtn = document.getElementById('micBtn');
        const panelToggleBtn = document.getElementById('panel-toggle-btn');
        const imageBtn = document.getElementById('imageBtn');

        if (input) input.style.display = recording ? 'none' : 'block';
        if (panelToggleBtn) panelToggleBtn.style.display = recording ? 'none' : 'block';
        if (imageBtn) imageBtn.style.display = recording ? 'none' : 'block';
        if (recordingPanel) recordingPanel.style.display = recording ? 'flex' : 'none';
        if (micBtn) micBtn.classList.toggle('recording', recording);
    }

    document.addEventListener('click', (e) => {
        if (e.target.closest('#micBtn')) {
            if (!isRecording) startRecording();
            else stopRecording(true);
            return;
        }
        if (e.target.closest('#cancelRecordBtn')) {
            stopRecording(false);
        }
    });
})();