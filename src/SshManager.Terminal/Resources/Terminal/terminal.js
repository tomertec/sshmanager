(function() {
    'use strict';

    let terminal = null;
    let fitAddon = null;
    let resizeTimeout = null;
    const RESIZE_DEBOUNCE_MS = 100;

    // Initialize terminal when DOM is ready
    function init() {
        const container = document.getElementById('terminal-container');
        if (!container) {
            console.error('Terminal container not found');
            return;
        }

        // Create terminal instance
        terminal = new Terminal({
            cursorBlink: true,
            cursorStyle: 'block',
            fontSize: 14,
            fontFamily: 'Cascadia Code, Consolas, monospace',
            allowTransparency: true,
            scrollback: 10000,
            theme: {
                background: '#1e1e1e',
                foreground: '#d4d4d4',
                cursor: '#d4d4d4',
                selectionBackground: '#264f78',
                black: '#000000',
                red: '#cd3131',
                green: '#0dbc79',
                yellow: '#e5e510',
                blue: '#2472c8',
                magenta: '#bc3fbc',
                cyan: '#11a8cd',
                white: '#e5e5e5',
                brightBlack: '#666666',
                brightRed: '#f14c4c',
                brightGreen: '#23d18b',
                brightYellow: '#f5f543',
                brightBlue: '#3b8eea',
                brightMagenta: '#d670d6',
                brightCyan: '#29b8db',
                brightWhite: '#ffffff'
            }
        });

        // Create and load fit addon
        fitAddon = new FitAddon.FitAddon();
        terminal.loadAddon(fitAddon);

        // Open terminal in container
        terminal.open(container);

        // Initial fit
        fitAddon.fit();

        // Listen for user input
        terminal.onData(function(data) {
            postMessageToHost({ type: 'input', data: data });
        });

        // Listen for terminal resize
        terminal.onResize(function(size) {
            postMessageToHost({ type: 'resized', cols: size.cols, rows: size.rows });
        });

        // Setup message listener for C# communication
        setupMessageListener();

        // Handle window resize with debounce
        window.addEventListener('resize', handleWindowResize);

        // Send ready message
        postMessageToHost({ type: 'ready' });

        // Send initial size
        postMessageToHost({ type: 'resized', cols: terminal.cols, rows: terminal.rows });
    }

    // Debounced window resize handler
    function handleWindowResize() {
        if (resizeTimeout) {
            clearTimeout(resizeTimeout);
        }
        resizeTimeout = setTimeout(function() {
            if (fitAddon && terminal) {
                fitAddon.fit();
            }
        }, RESIZE_DEBOUNCE_MS);
    }

    // Setup listener for messages from C#
    function setupMessageListener() {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.addEventListener('message', function(event) {
                handleMessage(event.data);
            });
        } else {
            // Fallback for testing in browser
            window.addEventListener('message', function(event) {
                handleMessage(event.data);
            });
        }
    }

    // Handle incoming messages from C#
    function handleMessage(message) {
        if (!message || !message.type) {
            return;
        }

        switch (message.type) {
            case 'write':
                if (terminal && message.data) {
                    terminal.write(message.data);
                }
                break;

            case 'resize':
                if (terminal && typeof message.cols === 'number' && typeof message.rows === 'number') {
                    terminal.resize(message.cols, message.rows);
                }
                break;

            case 'setTheme':
                if (terminal && message.theme) {
                    terminal.options.theme = message.theme;
                }
                break;

            case 'setFont':
                if (terminal) {
                    if (message.fontFamily) {
                        terminal.options.fontFamily = message.fontFamily;
                    }
                    if (typeof message.fontSize === 'number') {
                        terminal.options.fontSize = message.fontSize;
                    }
                    if (fitAddon) {
                        fitAddon.fit();
                    }
                }
                break;

            case 'focus':
                if (terminal) {
                    terminal.focus();
                }
                break;

            case 'clear':
                if (terminal) {
                    terminal.clear();
                }
                break;

            case 'fit':
                if (fitAddon && terminal) {
                    fitAddon.fit();
                }
                break;

            default:
                console.warn('Unknown message type:', message.type);
        }
    }

    // Send message to C# host
    function postMessageToHost(message) {
        if (window.chrome && window.chrome.webview) {
            window.chrome.webview.postMessage(message);
        } else {
            // Fallback for testing - log to console
            console.log('postMessage to host:', message);
        }
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
