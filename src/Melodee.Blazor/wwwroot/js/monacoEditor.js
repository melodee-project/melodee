(function () {
    const state = {
        monacoReady: null,
        editors: new Map(),
        completionsByModelUri: new Map(),
        completionProviderRegistered: false,
        completionProviderDisposable: null
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
                    registerCompletionProvider(window.monaco);
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
                        registerCompletionProvider(window.monaco);
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

    function layoutWhenVisible(container, editor) {
        let attempts = 0;
        const maxAttempts = 600; // ~10s at 60fps (tabs/flex layouts can settle late)

        function tick() {
            attempts++;
            if (!container || !container.isConnected) {
                return;
            }

            const width = container.clientWidth;
            const height = container.clientHeight;
            if (width > 0 && height > 0) {
                try {
                    editor.layout();
                } catch {
                }
                return;
            }

            if (attempts < maxAttempts) {
                requestAnimationFrame(tick);
            }
        }

        requestAnimationFrame(tick);
    }

    function normalizeCompletionSchema(schema) {
        const normalized = {
            ctx: Array.isArray(schema?.ctx) ? schema.ctx : [],
            scriptConfig: Array.isArray(schema?.scriptConfig) ? schema.scriptConfig : []
        };
        return normalized;
    }

    function registerCompletionProvider(monaco) {
        if (state.completionProviderRegistered) {
            return;
        }

        state.completionProviderRegistered = true;

        try {
            state.completionProviderDisposable = monaco.languages.registerCompletionItemProvider("javascript", {
                triggerCharacters: ["."],
                provideCompletionItems: (model, position) => {
                    try {
                        const modelUri = model?.uri?.toString?.();
                        if (!modelUri) {
                            return { suggestions: [] };
                        }

                        const schema = state.completionsByModelUri.get(modelUri);
                        if (!schema) {
                            return { suggestions: [] };
                        }

                        const linePrefix = model.getValueInRange({
                            startLineNumber: position.lineNumber,
                            startColumn: 1,
                            endLineNumber: position.lineNumber,
                            endColumn: position.column
                        });

                        const match = /\b(ctx|scriptConfig)\.([A-Za-z0-9_$]*)$/.exec(linePrefix);
                        if (!match) {
                            return { suggestions: [] };
                        }

                        const objectName = match[1];
                        const prefix = match[2] || "";
                        const word = model.getWordUntilPosition(position);
                        const range = new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn);

                        const items = objectName === "ctx" ? schema.ctx : schema.scriptConfig;
                        const suggestions = items
                            .filter(item => {
                                const label = item?.label || item?.Label;
                                if (!label) {
                                    return false;
                                }
                                if (!prefix) {
                                    return true;
                                }
                                return label.startsWith(prefix);
                            })
                            .map(item => {
                                const label = item?.label || item?.Label;
                                const detail = item?.detail || item?.Detail;
                                const documentation = item?.documentation || item?.Documentation;
                                const insertText = item?.insertText || item?.InsertText || label;

                                return {
                                    label,
                                    kind: monaco.languages.CompletionItemKind.Property,
                                    insertText,
                                    detail: detail || undefined,
                                    documentation: documentation ? { value: documentation } : undefined,
                                    range
                                };
                            });

                        return { suggestions };
                    } catch {
                        return { suggestions: [] };
                    }
                }
            });
        } catch {
        }
    }

    async function create(elementId, dotNetRef, options) {
        const monaco = await ensureMonaco();
        const container = document.getElementById(elementId);
        if (!container) {
            return;
        }

        const theme = options?.theme || "vs-dark";
        monaco.editor.setTheme(theme);

        const editorState = {
            editor: null,
            dotNetRef,
            suppress: true,
            resizeObserver: null,
            modelUri: null
        };

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

        editorState.editor = editor;
        try {
            editorState.modelUri = editor.getModel()?.uri?.toString?.() || null;
        } catch {
            editorState.modelUri = null;
        }

        if (editorState.modelUri) {
            state.completionsByModelUri.set(editorState.modelUri, normalizeCompletionSchema(options?.completions));
        }

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

        // Suppress initial model-change notifications that can occur during editor creation.
        // This prevents overwriting an already-loaded bound value with an empty initial value
        // during first render.
        setTimeout(() => {
            editorState.suppress = false;
        }, 0);

        // Monaco needs a layout() after the editor is visible and has non-zero size.
        // This is especially important when the editor is created inside tab content.
        layoutWhenVisible(container, editor);
        setTimeout(() => {
            try { editor.layout(); } catch { }
        }, 250);
        setTimeout(() => {
            try { editor.layout(); } catch { }
        }, 1000);

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

    function setCompletions(elementId, completions) {
        const editorState = state.editors.get(elementId);
        if (!editorState) {
            return;
        }

        const modelUri = editorState.modelUri;
        if (!modelUri) {
            return;
        }

        state.completionsByModelUri.set(modelUri, normalizeCompletionSchema(completions));
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

    function layout(elementId) {
        const editorState = state.editors.get(elementId);
        if (!editorState) {
            return;
        }

        try {
            editorState.editor?.layout();
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

        if (editorState.modelUri) {
            state.completionsByModelUri.delete(editorState.modelUri);
        }

        state.editors.delete(elementId);
    }

    window.melodeeMonacoEditor = {
        create,
        setCompletions,
        setValue,
        setLanguageAndTheme,
        layout,
        dispose
    };
})();
