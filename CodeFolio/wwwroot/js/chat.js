/**
 * chat.js — Portfolio AI assistant widget
 * Handles toggle, message submission, fetch to /api/ai/chat,
 * loading state, error display, and in-memory message history.
 * No external dependencies.
 */

(function () {
    'use strict';

    const toggle    = document.getElementById('chat-toggle');
    const panel     = document.getElementById('chat-panel');
    const closeBtn  = document.getElementById('chat-close');
    const form      = document.getElementById('chat-form');
    const input     = document.getElementById('chat-input');
    const messages  = document.getElementById('chat-messages');
    const typing    = document.getElementById('chat-typing');
    const errorBox  = document.getElementById('chat-error');
    const sendBtn   = form ? form.querySelector('.chat-send-btn') : null;

    if (!toggle || !panel) return;

    let isOpen = false;
    let isBusy = false;

    function openPanel() {
        panel.hidden = false;
        toggle.setAttribute('aria-expanded', 'true');
        isOpen = true;
        input.focus();
        scrollToBottom();
    }

    function closePanel() {
        panel.hidden = true;
        toggle.setAttribute('aria-expanded', 'false');
        isOpen = false;
        toggle.focus();
    }

    toggle.addEventListener('click', () => isOpen ? closePanel() : openPanel());
    closeBtn.addEventListener('click', closePanel);

    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && isOpen) closePanel();
    });

    function appendMessage(text, role) {
        const div = document.createElement('div');
        div.className = `chat-message ${role}-message`;
        const p = document.createElement('p');
        p.textContent = text;
        div.appendChild(p);
        messages.appendChild(div);
        scrollToBottom();
    }

    function scrollToBottom() {
        messages.scrollTop = messages.scrollHeight;
    }

    function setBusy(busy) {
        isBusy = busy;
        typing.hidden = !busy;
        input.disabled = busy;
        if (sendBtn) sendBtn.disabled = busy;
        if (!busy) scrollToBottom();
    }

    function showError(message) {
        errorBox.textContent = message;
        errorBox.hidden = false;
    }

    function clearError() {
        errorBox.textContent = '';
        errorBox.hidden = true;
    }

    form.addEventListener('submit', async function (e) {
        e.preventDefault();

        if (isBusy) return;

        const message = input.value.trim();
        if (!message) return;

        clearError();
        appendMessage(message, 'user');
        input.value = '';
        setBusy(true);

        try {
            const response = await fetch('/api/ai/chat', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify({ message })
            });

            if (response.status === 429) {
                showError('Too many messages. Please wait a moment before trying again.');
                return;
            }

            if (response.status === 503) {
                showError('The AI assistant is not available right now. Please use the contact form.');
                return;
            }

            if (!response.ok) {
                showError('Something went wrong. Please try again.');
                return;
            }

            const data = await response.json();

            if (data.success && data.reply) {
                appendMessage(data.reply, 'assistant');
            } else {
                showError(data.error || 'No response received. Please try again.');
            }

        } catch (err) {
            console.error('Chat request failed:', err);
            showError('Network error. Please check your connection and try again.');
        } finally {
            setBusy(false);
            input.focus();
        }
    });

})();
