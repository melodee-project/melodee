(function () {
    const state = {
        monacoReady: null,
        editors: new Map()
    };

    function getMonacoBaseUrl() {
        return window.__melodeeMonacoBaseUrl || "https://cdn.jsdelivr.net/npm/monaco-editor@0.52.0/min/vs";
    }

    function ensureScriptLoaded(src) {
        return new Promise((resolve, reject) => {
            const existing = document.querySelector(`script[src="${src}"]`);
            if (existing) {
                existing.addEventListener("load", resolve, { once: true });
                existing.addEventListener("error", reject, { once: true });
                if (existing.dataset.loaded === "true") {
                    resolve();
                }
                return;
            }

            const script = document.createElement("script");
            script.src = src;
            script.async = true;
            script.addEventListener("load", () => {
                script.dataset.loaded = "true";
                resolve();
            }, { once: true });
            script.addEventListener("error", reject, { once: true });
            document.head.appendChild(script);
        });
    }

    function ensureMonaco() {
        if (state.monacoReady) {
            return state.monacoReady;
        }

        state.monacoReady = new Promise(async (resolve, reject) => {
            try {
                if (window.monaco && window.monaco.editor) {
                    resolve(window.monaco);
                    return;
                }

                const baseUrl = getMonacoBaseUrl();
                await ensureScriptLoaded(`${baseUrl}/loader.js`);

                if (!window.require || !window.require.config) {
                    reject(new Error("Monaco loader did not initialize require.js"));
                    return;
                }

                window.require.config({ paths: { vs: baseUrl } });
                window.require(["vs/editor/editor.main"], () => {
                    if (window.monaco && window.monaco.editor) {
                        resolve(window.monaco);
                    } else {
                        reject(new Error("Monaco failed to initialize"));
                    }
                });
            } catch (e) {
                reject(e);
            }
        });

        return state.monacoReady;
    }

    function debounce(fn, delayMs) {
        let handle = null;
        return (...args) => {
            if (handle) {
                clearTimeout(handle);
            }
            handle = setTimeout(() => fn(...args), delayMs);
        };
    }

    async function create(elementId, dotNetRef, options) {
        const monaco = await ensureMonaco();
        const container = document.getElementById(elementId);
        if (!container) {
            return;
        }

        const theme = options?.theme || "vs-dark";
        monaco.editor.setTheme(theme);

        const editor = monaco.editor.create(container, {
            value: options?.value || "",
            language: options?.language || "javascript",
            readOnly: options?.readOnly === true,
            minimap: { enabled: false },
            scrollBeyondLastLine: false,
            renderWhitespace: "selection",
            automaticLayout: false,
            tabSize: 2,
            insertSpaces: true,
            fontSize: 13,
            lineNumbers: "on",
            roundedSelection: true
        });

        const editorState = {
            editor,
            dotNetRef,
            suppress: false,
            resizeObserver: null
        };

        const notify = debounce(() => {
            try {
                if (editorState.suppress) {
                    return;
                }
                dotNetRef.invokeMethodAsync("NotifyValueChanged", editor.getValue());
            } catch {
            }
        }, 200);

        editor.onDidChangeModelContent(() => notify());

        if (window.ResizeObserver) {
            editorState.resizeObserver = new ResizeObserver(() => {
                try {
                    editor.layout();
                } catch {
                }
            });
            editorState.resizeObserver.observe(container);
        }

        state.editors.set(elementId, editorState);
    }

    function setValue(elementId, value) {
        const editorState = state.editors.get(elementId);
        if (!editorState) {
            return;
        }
        const editor = editorState.editor;
        const nextValue = value ?? "";
        if (editor.getValue() === nextValue) {
            return;
        }
        editorState.suppress = true;
        try {
            editor.setValue(nextValue);
        } finally {
            editorState.suppress = false;
        }
    }

    function setLanguageAndTheme(elementId, language, theme) {
        const editorState = state.editors.get(elementId);
        if (!editorState) {
            return;
        }

        try {
            if (theme) {
                window.monaco?.editor?.setTheme(theme);
            }

            const model = editorState.editor.getModel();
            if (model && language) {
                window.monaco?.editor?.setModelLanguage(model, language);
            }
        } catch {
        }
    }

    function dispose(elementId) {
        const editorState = state.editors.get(elementId);
        if (!editorState) {
            return;
        }

        try {
            editorState.resizeObserver?.disconnect();
        } catch {
        }

        try {
            editorState.editor?.dispose();
        } catch {
        }

        try {
            editorState.dotNetRef?.dispose();
        } catch {
        }

        state.editors.delete(elementId);
    }

    window.melodeeMonacoEditor = {
        create,
        setValue,
        setLanguageAndTheme,
        dispose
    };
})();

